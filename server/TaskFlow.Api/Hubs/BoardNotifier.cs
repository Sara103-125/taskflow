using Microsoft.AspNetCore.SignalR;

namespace TaskFlow.Api.Hubs;

/// <summary>
/// Services use this to push changes to everyone viewing a board.
/// It is an interface so unit tests can swap in a fake instead of real SignalR.
/// </summary>
public interface IBoardNotifier
{
    Task SendAsync(int boardId, string eventName, object? payload);

    /// <summary>Sends to every open tab of one user (used for notifications).</summary>
    Task SendToUserAsync(string userId, string eventName, object? payload);

    /// <summary>Stops a removed member's open connections from receiving more updates for the board.</summary>
    Task RemoveUserFromBoardAsync(int boardId, string userId);
}

public class SignalRBoardNotifier : IBoardNotifier
{
    private readonly IHubContext<BoardHub> _hub;
    private readonly PresenceTracker _presence;

    public SignalRBoardNotifier(IHubContext<BoardHub> hub, PresenceTracker presence)
    {
        _hub = hub;
        _presence = presence;
    }

    public Task SendAsync(int boardId, string eventName, object? payload) =>
        _hub.Clients.Group(BoardEvents.Group(boardId)).SendAsync(eventName, payload);

    public Task SendToUserAsync(string userId, string eventName, object? payload) =>
        _hub.Clients.User(userId).SendAsync(eventName, payload);

    public async Task RemoveUserFromBoardAsync(int boardId, string userId)
    {
        foreach (var connectionId in _presence.GetConnections(boardId, userId))
        {
            await _hub.Groups.RemoveFromGroupAsync(connectionId, BoardEvents.Group(boardId));
            _presence.Remove(boardId, connectionId);
        }
        await SendAsync(boardId, BoardEvents.OnlineUsers, _presence.GetOnlineUsers(boardId));
    }
}
