using TaskFlow.Api.Data;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Hubs;

namespace TaskFlow.Api.Services;

public static class ActivityExtensions
{
    /// <summary>Adds an activity row. It is saved together with the change it describes.</summary>
    public static ActivityLog AddActivity(this AppDbContext db, int boardId, CurrentUser user, string message)
    {
        var entry = new ActivityLog { BoardId = boardId, UserId = user.Id, Message = message };
        db.ActivityLogs.Add(entry);
        return entry;
    }

    /// <summary>Call after SaveChanges so the entry has its database Id.</summary>
    public static Task BroadcastAsync(this IBoardNotifier notifier, ActivityLog entry) =>
        notifier.SendAsync(entry.BoardId, BoardEvents.ActivityAdded, entry.ToDto());
}
