using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Services;

namespace TaskFlow.Tests;

/// <summary>
/// A fresh in-memory SQLite database per test. Real relational database (FKs, cascades,
/// concurrency checks) but nothing to install, and it never touches SQL Server.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        Db = NewContext();
        Db.Database.EnsureCreated();
        // SQL Server changes a rowversion on every update by itself. SQLite doesn't, so a trigger does it.
        Db.Database.ExecuteSqlRaw(
            "CREATE TRIGGER Tasks_RowVersion AFTER UPDATE ON Tasks BEGIN " +
            "UPDATE Tasks SET RowVersion = randomblob(8) WHERE rowid = NEW.rowid; END;");
    }

    public AppDbContext Db { get; }
    public FakeNotifier Notifier { get; } = new();

    /// <summary>A second context on the same database, like a second web request.</summary>
    public AppDbContext NewContext() => new SqliteTestDbContext(_options);

    public BoardService Boards(AppDbContext? db = null) => new(db ?? Db, new BoardAccess(db ?? Db), Notifier);
    public ColumnService Columns(AppDbContext? db = null) => new(db ?? Db, new BoardAccess(db ?? Db), Notifier);
    public TaskService Tasks(AppDbContext? db = null) => new(db ?? Db, new BoardAccess(db ?? Db), Notifier);

    public CurrentUser AddUser(string name)
    {
        var email = $"{name.ToLowerInvariant()}@test.dev";
        var user = new AppUser
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            DisplayName = name
        };
        Db.Users.Add(user);
        Db.SaveChanges();
        return new CurrentUser(user.Id, name);
    }

    /// <summary>Creates a board (owned by <paramref name="owner"/>) and returns its id and default columns.</summary>
    public async Task<(int BoardId, List<Column> Columns)> CreateBoardAsync(CurrentUser owner, string name = "Board")
    {
        var board = await Boards().CreateAsync(owner, name);
        var columns = await Db.Columns.Where(c => c.BoardId == board.Id).OrderBy(c => c.Position).ToListAsync();
        return (board.Id, columns);
    }

    public Task<TaskDto> AddTaskAsync(CurrentUser user, int columnId, string title, string? assigneeId = null) =>
        Tasks().CreateAsync(user, new CreateTaskRequest(columnId, title, null, assigneeId, null));

    /// <summary>Task titles in a column, top to bottom, read fresh from the database.</summary>
    public async Task<List<string>> TitlesInColumnAsync(int columnId)
    {
        await using var db = NewContext();
        return await db.Tasks.Where(t => t.ColumnId == columnId).OrderBy(t => t.Position)
            .Select(t => t.Title).ToListAsync();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }

    private class SqliteTestDbContext : AppDbContext
    {
        public SqliteTestDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            // Give new rows a random version, like SQL Server does for rowversion.
            builder.Entity<TaskItem>().Property(t => t.RowVersion).HasDefaultValueSql("randomblob(8)");
        }
    }
}

/// <summary>Records broadcasts instead of sending them, so tests can assert on them.</summary>
public class FakeNotifier : IBoardNotifier
{
    public List<(int BoardId, string Event, object? Payload)> Sent { get; } = new();
    public List<(int BoardId, string UserId)> Removed { get; } = new();

    public Task SendAsync(int boardId, string eventName, object? payload)
    {
        Sent.Add((boardId, eventName, payload));
        return Task.CompletedTask;
    }

    public Task RemoveUserFromBoardAsync(int boardId, string userId)
    {
        Removed.Add((boardId, userId));
        return Task.CompletedTask;
    }
}
