using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Infrastructure;
using TaskFlow.Api.Services;

namespace TaskFlow.Tests;

/// <summary>Labels, checklists, comments & mentions, notifications, history, archive, "my tasks".</summary>
public class FeatureTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    private static UpdateTaskRequest Edit(TaskDto task, string? title = null, TaskPriority? priority = null,
        string? assigneeId = null, List<int>? labelIds = null) =>
        new(title ?? task.Title, task.Description, assigneeId ?? task.AssigneeId, task.DueDate,
            priority ?? task.Priority, task.RowVersion, labelIds ?? task.LabelIds);

    // ---------- Labels ----------

    [Fact]
    public async Task Labels_from_another_board_are_rejected()
    {
        var user = _t.AddUser("Sara");
        var (board1, columns) = await _t.CreateBoardAsync(user, "One");
        var (board2, _) = await _t.CreateBoardAsync(user, "Two");
        var otherBoardLabel = await _t.Labels().CreateAsync(board2, user, new LabelRequest("Bug", "#dc2626"));

        await Assert.ThrowsAsync<BadRequestException>(() => _t.Tasks().CreateAsync(user,
            new CreateTaskRequest(columns[0].Id, "Task", null, null, null, LabelIds: new List<int> { otherBoardLabel.Id })));
    }

    [Fact]
    public async Task Deleting_a_label_removes_it_from_tasks_and_boards_with_labels_can_be_deleted()
    {
        var user = _t.AddUser("Sara");
        var (boardId, columns) = await _t.CreateBoardAsync(user);
        var bug = await _t.Labels().CreateAsync(boardId, user, new LabelRequest("Bug", "#dc2626"));
        var ui = await _t.Labels().CreateAsync(boardId, user, new LabelRequest("UI", "#2563eb"));
        var task = await _t.Tasks().CreateAsync(user,
            new CreateTaskRequest(columns[0].Id, "Task", null, null, null, LabelIds: new List<int> { bug.Id, ui.Id }));
        Assert.Equal(2, task.LabelIds.Count);

        await _t.Labels().DeleteAsync(bug.Id, user);
        await using (var db = _t.NewContext())
            Assert.Equal(new[] { ui.Id }, await db.TaskLabels.Select(tl => tl.LabelId).ToListAsync());

        // Board -> Label -> TaskLabel must not trip the foreign keys.
        await _t.Boards().DeleteAsync(boardId, user);
        await using (var db = _t.NewContext())
        {
            Assert.False(await db.Boards.AnyAsync());
            Assert.False(await db.Labels.AnyAsync());
        }
    }

    // ---------- Checklist ----------

    [Fact]
    public async Task Ticking_a_checklist_item_updates_counts_without_changing_the_task_version()
    {
        var user = _t.AddUser("Sara");
        var (_, columns) = await _t.CreateBoardAsync(user);
        var task = await _t.AddTaskAsync(user, columns[0].Id, "Task");
        var item = await _t.Checklist().AddAsync(task.Id, user, "Step one");
        await _t.Checklist().AddAsync(task.Id, user, "Step two");

        await _t.Checklist().UpdateAsync(item.Id, user, new UpdateChecklistItemRequest("Step one", IsDone: true));

        await using var db = _t.NewContext();
        var fresh = await db.Tasks.Where(t => t.Id == task.Id).Select(Mappings.ToTaskDto).SingleAsync();
        Assert.Equal((1, 2), (fresh.ChecklistDone, fresh.ChecklistTotal));
        // So someone editing the task details at the same time gets no false conflict.
        Assert.Equal(task.RowVersion, fresh.RowVersion);
    }

    // ---------- Comments, mentions and notifications ----------

    [Fact]
    public async Task Mentioned_members_are_notified_but_not_the_author_or_similar_names()
    {
        var sara = _t.AddUser("Sara");
        var ali = _t.AddUser("Ali");
        var alina = _t.AddUser("Alina");
        var (boardId, columns) = await _t.CreateBoardAsync(sara);
        await _t.Boards().InviteAsync(boardId, sara, "ali@test.dev");
        await _t.Boards().InviteAsync(boardId, sara, "alina@test.dev");
        var task = await _t.AddTaskAsync(sara, columns[0].Id, "Login page");
        _t.Notifier.SentToUsers.Clear();

        await _t.Comments().AddAsync(task.Id, sara, "@ali can you review? cc @Sara");

        Assert.Single((await _t.Notifications().GetAsync(ali.Id, 10)).Items, n => n.Message == "Sara mentioned you on 'Login page'");
        Assert.Equal(0, (await _t.Notifications().GetAsync(sara.Id, 10)).UnreadCount);
        Assert.Equal(0, (await _t.Notifications().GetAsync(alina.Id, 10)).Items.Count(n => n.Message.Contains("mentioned")));
        var pushed = Assert.Single(_t.Notifier.SentToUsers);
        Assert.Equal((ali.Id, BoardEvents.NotificationAdded), (pushed.UserId, pushed.Event));
    }

    [Fact]
    public async Task Non_members_cannot_comment()
    {
        var owner = _t.AddUser("Owner");
        var stranger = _t.AddUser("Stranger");
        var (_, columns) = await _t.CreateBoardAsync(owner);
        var task = await _t.AddTaskAsync(owner, columns[0].Id, "Task");

        await Assert.ThrowsAsync<ForbiddenException>(() => _t.Comments().AddAsync(task.Id, stranger, "hi"));
    }

    [Fact]
    public async Task Assigning_someone_notifies_them_but_assigning_yourself_does_not()
    {
        var sara = _t.AddUser("Sara");
        var ali = _t.AddUser("Ali");
        var (boardId, columns) = await _t.CreateBoardAsync(sara);
        await _t.Boards().InviteAsync(boardId, sara, "ali@test.dev");

        await _t.AddTaskAsync(sara, columns[0].Id, "Mine", assigneeId: sara.Id);
        await _t.AddTaskAsync(sara, columns[0].Id, "Ali's", assigneeId: ali.Id);

        var aliInbox = await _t.Notifications().GetAsync(ali.Id, 10);
        Assert.Contains(aliInbox.Items, n => n.Message == "Sara assigned you 'Ali's'" && n.TaskId != null);
        Assert.Equal(0, (await _t.Notifications().GetAsync(sara.Id, 10)).UnreadCount);

        await _t.Notifications().MarkAllReadAsync(ali.Id);
        Assert.Equal(0, (await _t.Notifications().GetAsync(ali.Id, 10)).UnreadCount);
    }

    [Fact]
    public async Task Users_cannot_mark_someone_elses_notification_as_read()
    {
        var sara = _t.AddUser("Sara");
        var ali = _t.AddUser("Ali");
        var (boardId, _) = await _t.CreateBoardAsync(sara);
        await _t.Boards().InviteAsync(boardId, sara, "ali@test.dev");
        var aliNotification = (await _t.Notifications().GetAsync(ali.Id, 10)).Items.Single();

        await Assert.ThrowsAsync<NotFoundException>(() => _t.Notifications().MarkReadAsync(aliNotification.Id, sara.Id));
    }

    // ---------- History ----------

    [Fact]
    public async Task Editing_a_task_records_readable_history()
    {
        var sara = _t.AddUser("Sara");
        var (_, columns) = await _t.CreateBoardAsync(sara);
        var task = await _t.AddTaskAsync(sara, columns[0].Id, "Login page");

        await using (var db = _t.NewContext())
            await _t.Tasks(db).UpdateAsync(task.Id, sara, Edit(task, priority: TaskPriority.High));
        await _t.Tasks().MoveAsync(task.Id, sara, new MoveTaskRequest(columns[2].Id, 0));

        var history = (await _t.Tasks().GetDetailsAsync(task.Id, sara.Id)).History.Select(h => h.Change).ToList();
        Assert.Equal(new[]
        {
            "moved this task from To Do to Done",
            "changed priority from Medium to High",
            "created this task in To Do"
        }, history);
    }

    // ---------- Archive ----------

    [Fact]
    public async Task Archived_tasks_disappear_from_the_board_and_restore_at_the_bottom()
    {
        var user = _t.AddUser("Sara");
        var (boardId, columns) = await _t.CreateBoardAsync(user);
        var todo = columns[0].Id;
        var a = await _t.AddTaskAsync(user, todo, "A");
        await _t.AddTaskAsync(user, todo, "B");
        await _t.AddTaskAsync(user, todo, "C");

        await _t.Tasks().ArchiveAsync(a.Id, user);

        Assert.Equal(new[] { "B", "C" }, await _t.TitlesInColumnAsync(todo));
        Assert.DoesNotContain((await _t.Boards().GetBoardAsync(boardId, user.Id)).Tasks, t => t.Id == a.Id);
        Assert.Equal("A", Assert.Single(await _t.Tasks().GetArchivedAsync(boardId, user.Id)).Title);

        await _t.Tasks().RestoreAsync(a.Id, user);
        Assert.Equal(new[] { "B", "C", "A" }, await _t.TitlesInColumnAsync(todo));
    }

    [Fact]
    public async Task Archived_board_is_hidden_from_members_until_the_owner_restores_it()
    {
        var owner = _t.AddUser("Owner");
        var member = _t.AddUser("Member");
        var (boardId, _) = await _t.CreateBoardAsync(owner);
        await _t.Boards().InviteAsync(boardId, owner, "member@test.dev");

        await _t.Boards().ArchiveAsync(boardId, owner);

        Assert.Empty(await _t.Boards().GetMyBoardsAsync(member.Id));
        Assert.NotNull(Assert.Single(await _t.Boards().GetMyBoardsAsync(owner.Id)).ArchivedAt);
        await Assert.ThrowsAsync<ForbiddenException>(() => _t.Boards().GetBoardAsync(boardId, member.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => _t.Boards().RestoreAsync(boardId, member));

        await _t.Boards().RestoreAsync(boardId, owner);
        Assert.Single(await _t.Boards().GetMyBoardsAsync(member.Id));
    }

    // ---------- My tasks ----------

    [Fact]
    public async Task My_tasks_lists_only_my_active_tasks_across_boards()
    {
        var sara = _t.AddUser("Sara");
        var ali = _t.AddUser("Ali");
        var (_, board1) = await _t.CreateBoardAsync(sara, "One");
        var (_, board2) = await _t.CreateBoardAsync(sara, "Two");
        await _t.AddTaskAsync(sara, board1[0].Id, "Mine on one", assigneeId: sara.Id);
        await _t.AddTaskAsync(sara, board2[1].Id, "Mine on two", assigneeId: sara.Id);
        var archived = await _t.AddTaskAsync(sara, board2[0].Id, "Archived", assigneeId: sara.Id);
        await _t.AddTaskAsync(sara, board1[0].Id, "Unassigned");
        var (_, aliBoard) = await _t.CreateBoardAsync(ali, "Ali's");
        await _t.AddTaskAsync(ali, aliBoard[0].Id, "Ali's task", assigneeId: ali.Id);
        await _t.Tasks().ArchiveAsync(archived.Id, sara);

        var mine = await _t.Tasks().GetMyTasksAsync(sara.Id);

        Assert.Equal(new[] { "Mine on one", "Mine on two" }, mine.Select(t => t.Title).OrderBy(t => t));
        Assert.Contains(mine, t => t.BoardName == "Two" && t.ColumnName == "In Progress");
    }

    [Fact]
    public void Mention_matching_respects_word_boundaries()
    {
        Assert.True(CommentService.IsMentioned("thanks @Sara!", "Sara"));
        Assert.True(CommentService.IsMentioned("ping @demo user please", "Demo User"));
        Assert.False(CommentService.IsMentioned("hi @Sarah", "Sara"));
        Assert.False(CommentService.IsMentioned("email sara@example.com", "Ali"));
    }
}
