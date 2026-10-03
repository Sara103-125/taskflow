using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Infrastructure;

namespace TaskFlow.Tests;

public class PermissionTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Users_only_see_boards_they_belong_to()
    {
        var sara = _t.AddUser("Sara");
        var ali = _t.AddUser("Ali");
        await _t.CreateBoardAsync(sara, "Sara's board");
        await _t.CreateBoardAsync(ali, "Ali's board");

        var saraBoards = await _t.Boards().GetMyBoardsAsync(sara.Id);

        Assert.Equal(new[] { "Sara's board" }, saraBoards.Select(b => b.Name));
    }

    [Fact]
    public async Task Non_member_cannot_open_a_board()
    {
        var owner = _t.AddUser("Owner");
        var stranger = _t.AddUser("Stranger");
        var (boardId, _) = await _t.CreateBoardAsync(owner);

        await Assert.ThrowsAsync<ForbiddenException>(() => _t.Boards().GetBoardAsync(boardId, stranger.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => _t.Boards().GetActivityAsync(boardId, stranger.Id, 10));
    }

    [Fact]
    public async Task Unknown_board_is_not_found()
    {
        var user = _t.AddUser("Sara");
        await Assert.ThrowsAsync<NotFoundException>(() => _t.Boards().GetBoardAsync(999, user.Id));
    }

    [Fact]
    public async Task Invited_member_can_see_board_but_cannot_rename_delete_or_invite()
    {
        var owner = _t.AddUser("Owner");
        var member = _t.AddUser("Member");
        _t.AddUser("Third");
        var (boardId, _) = await _t.CreateBoardAsync(owner);

        await _t.Boards().InviteAsync(boardId, owner, "member@test.dev");

        var board = await _t.Boards().GetBoardAsync(boardId, member.Id);
        Assert.Equal(2, board.Members.Count);
        await Assert.ThrowsAsync<ForbiddenException>(() => _t.Boards().RenameAsync(boardId, member, "Mine now"));
        await Assert.ThrowsAsync<ForbiddenException>(() => _t.Boards().DeleteAsync(boardId, member));
        await Assert.ThrowsAsync<ForbiddenException>(() => _t.Boards().InviteAsync(boardId, member, "third@test.dev"));
    }

    [Fact]
    public async Task Inviting_an_unregistered_email_fails()
    {
        var owner = _t.AddUser("Owner");
        var (boardId, _) = await _t.CreateBoardAsync(owner);

        await Assert.ThrowsAsync<NotFoundException>(() => _t.Boards().InviteAsync(boardId, owner, "nobody@test.dev"));
    }

    [Fact]
    public async Task Demo_board_cannot_be_deleted_even_by_its_owner()
    {
        var owner = _t.AddUser("Demo");
        var (boardId, _) = await _t.CreateBoardAsync(owner);
        var board = await _t.Db.Boards.SingleAsync(b => b.Id == boardId);
        board.IsDemo = true;
        await _t.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => _t.Boards().DeleteAsync(boardId, owner));
        Assert.True(await _t.Db.Boards.AnyAsync(b => b.Id == boardId));
    }

    [Fact]
    public async Task Non_member_cannot_create_move_or_delete_tasks()
    {
        var owner = _t.AddUser("Owner");
        var stranger = _t.AddUser("Stranger");
        var (_, columns) = await _t.CreateBoardAsync(owner);
        var task = await _t.AddTaskAsync(owner, columns[0].Id, "Secret task");

        await Assert.ThrowsAsync<ForbiddenException>(() => _t.AddTaskAsync(stranger, columns[0].Id, "Hack"));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _t.Tasks().MoveAsync(task.Id, stranger, new MoveTaskRequest(columns[1].Id, 0)));
        await Assert.ThrowsAsync<ForbiddenException>(() => _t.Tasks().DeleteAsync(task.Id, stranger));
    }

    [Fact]
    public async Task Assignee_must_be_a_board_member()
    {
        var owner = _t.AddUser("Owner");
        var outsider = _t.AddUser("Outsider");
        var (_, columns) = await _t.CreateBoardAsync(owner);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _t.AddTaskAsync(owner, columns[0].Id, "Task", assigneeId: outsider.Id));
    }

    [Fact]
    public async Task Removing_a_member_unassigns_their_tasks_and_drops_their_live_connection()
    {
        var owner = _t.AddUser("Owner");
        var member = _t.AddUser("Member");
        var (boardId, columns) = await _t.CreateBoardAsync(owner);
        await _t.Boards().InviteAsync(boardId, owner, "member@test.dev");
        var task = await _t.AddTaskAsync(owner, columns[0].Id, "Their task", assigneeId: member.Id);

        await _t.Boards().RemoveMemberAsync(boardId, owner, member.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => _t.Boards().GetBoardAsync(boardId, member.Id));
        await using var fresh = _t.NewContext();
        Assert.Null((await fresh.Tasks.SingleAsync(t => t.Id == task.Id)).AssigneeId);
        Assert.Contains((boardId, member.Id), _t.Notifier.Removed);
    }
}
