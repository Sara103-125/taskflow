using TaskFlow.Api.Data;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Hubs;

namespace TaskFlow.Api.Services;

/// <summary>
/// Helpers that record "what happened" rows. Each row is added to the DbContext and saved
/// together with the change it describes (one SaveChanges = one transaction), then broadcast.
/// </summary>
public static class ActivityExtensions
{
    /// <summary>Board activity feed line, e.g. "Sara moved 'Login page' to Done".</summary>
    public static ActivityLog AddActivity(this AppDbContext db, int boardId, CurrentUser user, string message)
    {
        var entry = new ActivityLog { BoardId = boardId, UserId = user.Id, Message = message };
        db.ActivityLogs.Add(entry);
        return entry;
    }

    /// <summary>Per-task history line, e.g. "changed priority from Low to High".</summary>
    public static void AddHistory(this AppDbContext db, TaskItem task, CurrentUser user, string change) =>
        db.TaskHistory.Add(new TaskHistory { Task = task, UserName = user.DisplayName, Change = change });

    /// <summary>A notification for one user. Nobody gets notified about their own actions.</summary>
    public static Notification? AddNotification(
        this AppDbContext db, string recipientId, CurrentUser actor, int boardId, int? taskId, string message)
    {
        if (recipientId == actor.Id) return null;
        var notification = new Notification { UserId = recipientId, BoardId = boardId, TaskId = taskId, Message = message };
        db.Notifications.Add(notification);
        return notification;
    }

    /// <summary>Call after SaveChanges so the entry has its database Id.</summary>
    public static Task BroadcastAsync(this IBoardNotifier notifier, ActivityLog entry) =>
        notifier.SendAsync(entry.BoardId, BoardEvents.ActivityAdded, entry.ToDto());

    /// <summary>Call after SaveChanges. Pushes each notification to its recipient's open tabs.</summary>
    public static async Task PushAsync(this IBoardNotifier notifier, IEnumerable<Notification?> notifications)
    {
        foreach (var n in notifications.OfType<Notification>())
            await notifier.SendToUserAsync(n.UserId, BoardEvents.NotificationAdded, n.ToDto());
    }
}
