using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Infrastructure;

namespace TaskFlow.Api.Services;

/// <summary>Reading and marking notifications. Creating them happens where the action happens.</summary>
public class NotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db) => _db = db;

    public async Task<NotificationsDto> GetAsync(string userId, int take)
    {
        var unread = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
        var items = await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(Math.Clamp(take, 1, 50))
            .Select(n => new NotificationDto(n.Id, n.BoardId, n.TaskId, n.Message, n.IsRead, n.CreatedAt))
            .ToListAsync();
        return new NotificationsDto(unread, items);
    }

    public async Task MarkReadAsync(int id, string userId)
    {
        // Filtering by UserId means you can never touch someone else's notification.
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId)
            ?? throw new NotFoundException("Notification not found.");
        notification.IsRead = true;
        await _db.SaveChangesAsync();
    }

    /// <summary>One UPDATE statement, no need to load the rows.</summary>
    public Task MarkAllReadAsync(string userId) =>
        _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
}
