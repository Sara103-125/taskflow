using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Hubs;

namespace TaskFlow.Api.Services;

/// <summary>
/// Creates the public demo account and its sample board, and resets them once a day.
///
/// The reset is "lazy": it runs at startup and whenever someone clicks "Try the demo",
/// if the demo board is older than 24 hours. A timer would not be reliable on the free
/// App Service plan, because the app is put to sleep when nobody is using it.
/// </summary>
public class DemoSeeder
{
    // Public on purpose: these are shown on the login page and in the README.
    // ".example" is a reserved domain, so these can never be real people's addresses.
    public const string DemoEmail = "demo@taskflow.example";
    public const string DemoPassword = "Demo1234!";
    private static readonly TimeSpan ResetAfter = TimeSpan.FromHours(24);

    // Only one reset at a time, even if two recruiters click "Try the demo" together.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _users;
    private readonly IBoardNotifier _notifier;
    private readonly ILogger<DemoSeeder> _logger;

    public DemoSeeder(AppDbContext db, UserManager<AppUser> users, IBoardNotifier notifier, ILogger<DemoSeeder> logger)
    {
        _db = db;
        _users = users;
        _notifier = notifier;
        _logger = logger;
    }

    public static bool IsDemoUser(AppUser user) =>
        string.Equals(user.Email, DemoEmail, StringComparison.OrdinalIgnoreCase);

    /// <summary>Makes sure the demo user and a fresh demo board exist. Returns the demo user.</summary>
    public async Task<AppUser> EnsureDemoAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var demo = await EnsureUserAsync(DemoEmail, "Demo User", DemoPassword);
            // Teammates have no password, so nobody can log in as them.
            var sara = await EnsureUserAsync("sara@taskflow.example", "Sara", password: null);
            var ali = await EnsureUserAsync("ali@taskflow.example", "Ali", password: null);

            var ownedBoards = await _db.Boards
                .Where(b => b.Members.Any(m => m.UserId == demo.Id && m.Role == BoardRole.Owner))
                .ToListAsync();
            var demoBoard = ownedBoards.FirstOrDefault(b => b.IsDemo);
            if (demoBoard is not null && demoBoard.CreatedAt > DateTime.UtcNow - ResetAfter)
                return demo;

            // Reset: delete every board the demo account owns (including ones visitors created).
            foreach (var board in ownedBoards)
                await BoardService.RemoveBoardAsync(_db, board);
            await _db.SaveChangesAsync();
            foreach (var board in ownedBoards)
                await _notifier.SendAsync(board.Id, BoardEvents.BoardDeleted, board.Id);

            await CreateSampleBoardAsync(demo, sara, ali);
            _logger.LogInformation("Demo board (re)created");
            return demo;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<AppUser> EnsureUserAsync(string email, string displayName, string? password)
    {
        var user = await _users.FindByEmailAsync(email);
        if (user is not null) return user;

        user = new AppUser { Email = email, UserName = email, DisplayName = displayName, EmailConfirmed = true };
        var result = password is null
            ? await _users.CreateAsync(user)
            : await _users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Could not create demo user {email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        return user;
    }

    private async Task CreateSampleBoardAsync(AppUser demo, AppUser sara, AppUser ali)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var board = new Board { Name = "Website Relaunch (Demo)", IsDemo = true, CreatedAt = now };
        board.Members.Add(new BoardMember { UserId = demo.Id, Role = BoardRole.Owner });
        board.Members.Add(new BoardMember { UserId = sara.Id, Role = BoardRole.Member });
        board.Members.Add(new BoardMember { UserId = ali.Id, Role = BoardRole.Member });

        var frontend = new Label { Name = "Frontend", Color = "#2563eb" };
        var backend = new Label { Name = "Backend", Color = "#7c3aed" };
        var bug = new Label { Name = "Bug", Color = "#dc2626" };
        board.Labels.AddRange(new[] { frontend, backend, bug });

        var todo = new Column { Name = "To Do", Position = 0 };
        var doing = new Column { Name = "In Progress", Position = 1 };
        var done = new Column { Name = "Done", Position = 2 };
        board.Columns.AddRange(new[] { todo, doing, done });

        todo.Tasks.AddRange(new[]
        {
            NewTask("Design login page", "Simple form, show validation errors inline.", sara, today.AddDays(3), TaskPriority.High, 0),
            NewTask("Write API documentation", "Document every endpoint in the README.", ali, today.AddDays(10), TaskPriority.Low, 1),
            NewTask("Set up CI pipeline", "Build and test on every push to main.", demo, today.AddDays(7), TaskPriority.Medium, 2)
        });
        doing.Tasks.AddRange(new[]
        {
            NewTask("Build board view", "Columns with drag and drop.", demo, today.AddDays(2), TaskPriority.High, 0),
            NewTask("Add live updates", "Use SignalR so everyone sees changes instantly.", sara, today.AddDays(5), TaskPriority.Medium, 1)
        });
        done.Tasks.AddRange(new[]
        {
            NewTask("Create database schema", "Boards, columns, tasks, activity.", ali, null, TaskPriority.Medium, 0),
            NewTask("Project kickoff meeting", null, null, null, TaskPriority.Low, 1)
        });

        board.Activity.AddRange(new[]
        {
            NewActivity(demo, "Demo User created the board", now.AddMinutes(-50)),
            NewActivity(ali, "Ali moved 'Create database schema' to Done", now.AddMinutes(-30)),
            NewActivity(sara, "Sara added 'Add live updates' to To Do", now.AddMinutes(-20)),
            NewActivity(sara, "Sara moved 'Add live updates' to In Progress", now.AddMinutes(-10))
        });

        // Labels, checklists and comments so every feature has something to show.
        var tasks = todo.Tasks.Concat(doing.Tasks).Concat(done.Tasks).ToDictionary(t => t.Title);
        AddLabels(tasks["Design login page"], frontend);
        AddLabels(tasks["Write API documentation"], backend);
        AddLabels(tasks["Build board view"], frontend);
        AddLabels(tasks["Add live updates"], frontend, backend);
        AddLabels(tasks["Create database schema"], backend);
        AddLabels(tasks["Set up CI pipeline"], backend, bug);

        tasks["Build board view"].Checklist.AddRange(new[]
        {
            new ChecklistItem { Text = "Columns layout", IsDone = true, Position = 0 },
            new ChecklistItem { Text = "Drag and drop between columns", IsDone = true, Position = 1 },
            new ChecklistItem { Text = "Reorder columns", Position = 2 },
            new ChecklistItem { Text = "Empty state", Position = 3 }
        });
        tasks["Design login page"].Checklist.AddRange(new[]
        {
            new ChecklistItem { Text = "Wireframe", IsDone = true, Position = 0 },
            new ChecklistItem { Text = "Validation messages", Position = 1 }
        });

        tasks["Build board view"].Comments.AddRange(new[]
        {
            new Comment { AuthorId = sara.Id, Body = "@Demo User can you also make the cards show the checklist progress?", CreatedAt = now.AddMinutes(-25) },
            new Comment { AuthorId = ali.Id, Body = "The API already returns the counts, so it's just UI work.", CreatedAt = now.AddMinutes(-15) }
        });
        foreach (var task in tasks.Values)
            task.History.Add(new TaskHistory { UserName = "Demo User", Change = "created this task", CreatedAt = now.AddMinutes(-60) });
        tasks["Add live updates"].History.Add(new TaskHistory
        {
            UserName = "Sara", Change = "moved this task from To Do to In Progress", CreatedAt = now.AddMinutes(-10)
        });

        _db.Boards.Add(board);
        await _db.SaveChangesAsync();

        _db.Notifications.Add(new Notification
        {
            UserId = demo.Id, BoardId = board.Id, TaskId = tasks["Build board view"].Id, CreatedAt = now.AddMinutes(-25),
            Message = "Sara mentioned you on 'Build board view'"
        });
        await _db.SaveChangesAsync();
    }

    private static void AddLabels(TaskItem task, params Label[] labels) =>
        task.TaskLabels.AddRange(labels.Select(l => new TaskLabel { Label = l }));

    private static TaskItem NewTask(string title, string? description, AppUser? assignee, DateOnly? due, TaskPriority priority, int position) =>
        new()
        {
            Title = title,
            Description = description,
            AssigneeId = assignee?.Id,
            DueDate = due,
            Priority = priority,
            Position = position
        };

    private static ActivityLog NewActivity(AppUser user, string message, DateTime at) =>
        new() { UserId = user.Id, Message = message, CreatedAt = at };
}
