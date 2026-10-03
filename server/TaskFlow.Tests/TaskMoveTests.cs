using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Infrastructure;

namespace TaskFlow.Tests;

public class TaskMoveTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Moving_within_a_column_reorders_it()
    {
        var user = _t.AddUser("Sara");
        var (_, columns) = await _t.CreateBoardAsync(user);
        var todo = columns[0].Id;
        await _t.AddTaskAsync(user, todo, "A");
        await _t.AddTaskAsync(user, todo, "B");
        var c = await _t.AddTaskAsync(user, todo, "C");

        await _t.Tasks().MoveAsync(c.Id, user, new MoveTaskRequest(todo, 0));

        Assert.Equal(new[] { "C", "A", "B" }, await _t.TitlesInColumnAsync(todo));
    }

    [Fact]
    public async Task Moving_to_another_column_renumbers_both_columns_and_broadcasts()
    {
        var user = _t.AddUser("Sara");
        var (boardId, columns) = await _t.CreateBoardAsync(user);
        var (todo, done) = (columns[0].Id, columns[2].Id);
        await _t.AddTaskAsync(user, todo, "A");
        var b = await _t.AddTaskAsync(user, todo, "B");
        await _t.AddTaskAsync(user, todo, "C");
        await _t.AddTaskAsync(user, done, "X");

        var changed = await _t.Tasks().MoveAsync(b.Id, user, new MoveTaskRequest(done, 0));

        Assert.Equal(new[] { "A", "C" }, await _t.TitlesInColumnAsync(todo));
        Assert.Equal(new[] { "B", "X" }, await _t.TitlesInColumnAsync(done));

        // Positions have no gaps in either column.
        await using var db = _t.NewContext();
        foreach (var columnId in new[] { todo, done })
        {
            var positions = await db.Tasks.Where(t => t.ColumnId == columnId).OrderBy(t => t.Position)
                .Select(t => t.Position).ToListAsync();
            Assert.Equal(Enumerable.Range(0, positions.Count), positions);
        }

        // Everyone on the board is told about the new order, and the activity log says what happened.
        Assert.Equal(4, changed.Count);
        Assert.Contains(_t.Notifier.Sent, s => s.BoardId == boardId && s.Event == BoardEvents.TasksChanged);
        var activity = await _t.Boards().GetActivityAsync(boardId, user.Id, 1);
        Assert.Equal("Sara moved 'B' to Done", activity[0].Message);
    }

    [Fact]
    public async Task Position_past_the_end_puts_task_at_the_bottom()
    {
        var user = _t.AddUser("Sara");
        var (_, columns) = await _t.CreateBoardAsync(user);
        var a = await _t.AddTaskAsync(user, columns[0].Id, "A");
        await _t.AddTaskAsync(user, columns[1].Id, "Y");

        await _t.Tasks().MoveAsync(a.Id, user, new MoveTaskRequest(columns[1].Id, 99));

        Assert.Equal(new[] { "Y", "A" }, await _t.TitlesInColumnAsync(columns[1].Id));
    }

    [Fact]
    public async Task Cannot_move_a_task_to_a_column_on_another_board()
    {
        var user = _t.AddUser("Sara");
        var (_, board1) = await _t.CreateBoardAsync(user, "One");
        var (_, board2) = await _t.CreateBoardAsync(user, "Two");
        var task = await _t.AddTaskAsync(user, board1[0].Id, "A");

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _t.Tasks().MoveAsync(task.Id, user, new MoveTaskRequest(board2[0].Id, 0)));
    }

    [Fact]
    public async Task Deleting_a_task_closes_the_gap()
    {
        var user = _t.AddUser("Sara");
        var (_, columns) = await _t.CreateBoardAsync(user);
        await _t.AddTaskAsync(user, columns[0].Id, "A");
        var b = await _t.AddTaskAsync(user, columns[0].Id, "B");
        var c = await _t.AddTaskAsync(user, columns[0].Id, "C");

        await _t.Tasks().DeleteAsync(b.Id, user);

        await using var db = _t.NewContext();
        Assert.Equal(1, (await db.Tasks.SingleAsync(t => t.Id == c.Id)).Position);
    }
}
