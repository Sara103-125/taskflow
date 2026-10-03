using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Infrastructure;

namespace TaskFlow.Api.Services;

public class CommentService
{
    private readonly AppDbContext _db;
    private readonly BoardAccess _access;
    private readonly IBoardNotifier _notifier;

    public CommentService(AppDbContext db, BoardAccess access, IBoardNotifier notifier)
    {
        _db = db;
        _access = access;
        _notifier = notifier;
    }

    /// <summary>
    /// Adds a comment. Board members mentioned as "@Their Name" get a notification;
    /// the task's assignee is told about new comments too.
    /// </summary>
    public async Task<CommentDto> AddAsync(int taskId, CurrentUser user, string body)
    {
        var task = await _db.Tasks.Include(t => t.Column).FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw new NotFoundException("Task not found.");
        var boardId = task.Column.BoardId;
        await _access.RequireMemberAsync(boardId, user.Id);

        var comment = new Comment { TaskId = taskId, AuthorId = user.Id, Body = body.Trim() };
        _db.Comments.Add(comment);

        var members = await _db.BoardMembers
            .Where(m => m.BoardId == boardId)
            .Select(m => new { m.UserId, m.User.DisplayName })
            .ToListAsync();
        var mentioned = members.Where(m => IsMentioned(comment.Body, m.DisplayName)).Select(m => m.UserId).ToHashSet();

        var notifications = mentioned
            .Select(id => _db.AddNotification(id, user, boardId, taskId, $"{user.DisplayName} mentioned you on '{task.Title}'"))
            .ToList();
        if (task.AssigneeId is not null && !mentioned.Contains(task.AssigneeId))
            notifications.Add(_db.AddNotification(task.AssigneeId, user, boardId, taskId,
                $"{user.DisplayName} commented on '{task.Title}'"));

        var activity = _db.AddActivity(boardId, user, $"{user.DisplayName} commented on '{task.Title}'");
        await _db.SaveChangesAsync();

        var dto = new CommentDto(comment.Id, taskId, user.Id, user.DisplayName, comment.Body, comment.CreatedAt);
        await _notifier.SendAsync(boardId, BoardEvents.CommentAdded, dto);
        await BroadcastTaskAsync(boardId, taskId);
        await _notifier.BroadcastAsync(activity);
        await _notifier.PushAsync(notifications);
        return dto;
    }

    /// <summary>Authors can delete their own comments; the board owner can delete any.</summary>
    public async Task DeleteAsync(int commentId, CurrentUser user)
    {
        var comment = await _db.Comments.Include(c => c.Task).ThenInclude(t => t.Column)
            .FirstOrDefaultAsync(c => c.Id == commentId)
            ?? throw new NotFoundException("Comment not found.");
        var boardId = comment.Task.Column.BoardId;
        var me = await _access.RequireMemberAsync(boardId, user.Id);
        if (comment.AuthorId != user.Id && me.Role != BoardRole.Owner)
            throw new ForbiddenException("You can only delete your own comments.");

        _db.Comments.Remove(comment);
        await _db.SaveChangesAsync();

        await _notifier.SendAsync(boardId, BoardEvents.CommentDeleted, new { taskId = comment.TaskId, commentId });
        await BroadcastTaskAsync(boardId, comment.TaskId);
    }

    /// <summary>
    /// "@Sara" mentions Sara, but "@Sarah" does not (the name must not continue with a letter).
    /// </summary>
    public static bool IsMentioned(string body, string displayName) =>
        Regex.IsMatch(body, "@" + Regex.Escape(displayName) + @"(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase);

    private async Task BroadcastTaskAsync(int boardId, int taskId)
    {
        var task = await _db.Tasks.Where(t => t.Id == taskId).Select(Mappings.ToTaskDto).FirstAsync();
        await _notifier.SendAsync(boardId, BoardEvents.TaskUpserted, task);
    }
}
