using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Auth;
using TaskFlow.Api.Data;
using TaskFlow.Api.Dtos;

namespace TaskFlow.Api.Hubs;

/// <summary>
/// The SignalR hub. Clients only call JoinBoard / LeaveBoard here.
/// All changes (create, move, edit...) go through the normal REST API, which validates them,
/// saves to the database and then broadcasts to the board's group via IBoardNotifier.
/// </summary>
[Authorize]
public class BoardHub : Hub
{
    private readonly AppDbContext _db;
    private readonly PresenceTracker _presence;

    public BoardHub(AppDbContext db, PresenceTracker presence)
    {
        _db = db;
        _presence = presence;
    }

    public async Task JoinBoard(int boardId)
    {
        var userId = Context.User!.GetUserId();

        // Security: only members may join a board's group (and so receive its updates).
        var isMember = await _db.BoardMembers.AnyAsync(
            m => m.BoardId == boardId && m.UserId == userId && m.Board.ArchivedAt == null);
        if (!isMember)
            throw new HubException("You are not a member of this board.");

        await Groups.AddToGroupAsync(Context.ConnectionId, BoardEvents.Group(boardId));
        _presence.Add(boardId, Context.ConnectionId, new OnlineUserDto(userId, Context.User!.GetDisplayName()));
        await BroadcastOnlineUsers(boardId);
        await Clients.Caller.SendAsync(BoardEvents.EditorsChanged, _presence.GetEditors(boardId));
    }

    /// <summary>
    /// Called when a user opens (taskId) or closes (null) a task dialog, so teammates see
    /// "Sara is editing". Only allowed after JoinBoard, which already checked membership.
    /// </summary>
    public async Task SetEditing(int boardId, int? taskId)
    {
        if (!_presence.IsOnBoard(boardId, Context.ConnectionId))
            throw new HubException("Join the board first.");

        _presence.SetEditing(boardId, Context.ConnectionId, taskId);
        await BroadcastEditors(boardId);
    }

    public async Task LeaveBoard(int boardId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, BoardEvents.Group(boardId));
        _presence.Remove(boardId, Context.ConnectionId);
        await BroadcastOnlineUsers(boardId);
        await BroadcastEditors(boardId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // SignalR removes closed connections from groups automatically; we only update presence.
        foreach (var boardId in _presence.RemoveConnection(Context.ConnectionId))
        {
            await BroadcastOnlineUsers(boardId);
            await BroadcastEditors(boardId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Task BroadcastEditors(int boardId) =>
        Clients.Group(BoardEvents.Group(boardId)).SendAsync(BoardEvents.EditorsChanged, _presence.GetEditors(boardId));

    private Task BroadcastOnlineUsers(int boardId) =>
        Clients.Group(BoardEvents.Group(boardId))
            .SendAsync(BoardEvents.OnlineUsers, _presence.GetOnlineUsers(boardId));
}
