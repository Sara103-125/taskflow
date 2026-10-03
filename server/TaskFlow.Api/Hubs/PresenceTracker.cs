using TaskFlow.Api.Dtos;

namespace TaskFlow.Api.Hubs;

/// <summary>
/// Remembers which SignalR connections are viewing which board, so we can show "who's online".
/// Kept in memory (a singleton), which is fine for one server instance. With several
/// instances you would move this to Redis or use Azure SignalR Service.
/// A user with two tabs open has two connections but is shown once.
/// </summary>
public class PresenceTracker
{
    private readonly object _lock = new();
    // boardId -> (connectionId -> user)
    private readonly Dictionary<int, Dictionary<string, OnlineUserDto>> _boards = new();

    public void Add(int boardId, string connectionId, OnlineUserDto user)
    {
        lock (_lock)
        {
            if (!_boards.TryGetValue(boardId, out var connections))
                _boards[boardId] = connections = new Dictionary<string, OnlineUserDto>();
            connections[connectionId] = user;
        }
    }

    public void Remove(int boardId, string connectionId)
    {
        lock (_lock)
        {
            if (_boards.TryGetValue(boardId, out var connections))
            {
                connections.Remove(connectionId);
                if (connections.Count == 0) _boards.Remove(boardId);
            }
        }
    }

    /// <summary>Removes a closed connection from every board. Returns the boards it was on.</summary>
    public List<int> RemoveConnection(string connectionId)
    {
        lock (_lock)
        {
            var affected = _boards.Where(b => b.Value.ContainsKey(connectionId)).Select(b => b.Key).ToList();
            foreach (var boardId in affected) Remove(boardId, connectionId);
            return affected;
        }
    }

    public List<string> GetConnections(int boardId, string userId)
    {
        lock (_lock)
        {
            return _boards.TryGetValue(boardId, out var connections)
                ? connections.Where(c => c.Value.UserId == userId).Select(c => c.Key).ToList()
                : new List<string>();
        }
    }

    public List<OnlineUserDto> GetOnlineUsers(int boardId)
    {
        lock (_lock)
        {
            return _boards.TryGetValue(boardId, out var connections)
                ? connections.Values.DistinctBy(u => u.UserId).OrderBy(u => u.DisplayName).ToList()
                : new List<OnlineUserDto>();
        }
    }
}
