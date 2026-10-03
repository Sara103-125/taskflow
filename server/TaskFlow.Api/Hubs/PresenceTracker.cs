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
    // connectionId -> (boardId, taskId) of the task dialog that connection has open
    private readonly Dictionary<string, (int BoardId, int TaskId)> _editing = new();

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
            if (_editing.TryGetValue(connectionId, out var editing) && editing.BoardId == boardId)
                _editing.Remove(connectionId);
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

    public bool IsOnBoard(int boardId, string connectionId)
    {
        lock (_lock)
        {
            return _boards.TryGetValue(boardId, out var connections) && connections.ContainsKey(connectionId);
        }
    }

    /// <summary>Remembers that this connection has a task open (null = closed the dialog).</summary>
    public void SetEditing(int boardId, string connectionId, int? taskId)
    {
        lock (_lock)
        {
            if (taskId is null) _editing.Remove(connectionId);
            else _editing[connectionId] = (boardId, taskId.Value);
        }
    }

    /// <summary>Who has which task open on this board (one row per user and task).</summary>
    public List<EditorDto> GetEditors(int boardId)
    {
        lock (_lock)
        {
            if (!_boards.TryGetValue(boardId, out var connections)) return new List<EditorDto>();
            return _editing
                .Where(e => e.Value.BoardId == boardId && connections.ContainsKey(e.Key))
                .Select(e => new EditorDto(e.Value.TaskId, connections[e.Key].UserId, connections[e.Key].DisplayName))
                .Distinct()
                .ToList();
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
