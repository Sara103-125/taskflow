using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Infrastructure;

namespace TaskFlow.Api.Services;

/// <summary>
/// Checklist items live in their own table, so changing them never touches the task row
/// (no RowVersion change, no false edit conflicts). After each change we broadcast the full
/// checklist for the task, plus the task itself so cards can update their "2/5" counter.
/// </summary>
public class ChecklistService
{
    private readonly AppDbContext _db;
    private readonly BoardAccess _access;
    private readonly IBoardNotifier _notifier;

    public ChecklistService(AppDbContext db, BoardAccess access, IBoardNotifier notifier)
    {
        _db = db;
        _access = access;
        _notifier = notifier;
    }

    public async Task<ChecklistItemDto> AddAsync(int taskId, CurrentUser user, string text)
    {
        var task = await _db.Tasks.Include(t => t.Column).FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw new NotFoundException("Task not found.");
        await _access.RequireMemberAsync(task.Column.BoardId, user.Id);

        var item = new ChecklistItem
        {
            TaskId = taskId,
            Text = text.Trim(),
            Position = await _db.ChecklistItems.CountAsync(c => c.TaskId == taskId)
        };
        _db.ChecklistItems.Add(item);
        await _db.SaveChangesAsync();

        await BroadcastAsync(task.Column.BoardId, taskId);
        return item.ToDto();
    }

    public async Task UpdateAsync(int itemId, CurrentUser user, UpdateChecklistItemRequest request)
    {
        var item = await FindItemAsync(itemId, user);
        if (request.IsDone != item.IsDone)
            _db.AddHistory(item.Task, user, $"{(request.IsDone ? "completed" : "reopened")} checklist item '{request.Text.Trim()}'");

        item.Text = request.Text.Trim();
        item.IsDone = request.IsDone;
        await _db.SaveChangesAsync();

        await BroadcastAsync(item.Task.Column.BoardId, item.TaskId);
    }

    public async Task DeleteAsync(int itemId, CurrentUser user)
    {
        var item = await FindItemAsync(itemId, user);
        var rest = await _db.ChecklistItems
            .Where(c => c.TaskId == item.TaskId && c.Id != itemId)
            .OrderBy(c => c.Position)
            .ToListAsync();
        for (var i = 0; i < rest.Count; i++) rest[i].Position = i;

        _db.ChecklistItems.Remove(item);
        await _db.SaveChangesAsync();

        await BroadcastAsync(item.Task.Column.BoardId, item.TaskId);
    }

    private async Task<ChecklistItem> FindItemAsync(int itemId, CurrentUser user)
    {
        // The task's archive filter applies here too: archived tasks are read-only.
        var item = await _db.ChecklistItems.Include(c => c.Task).ThenInclude(t => t.Column)
            .FirstOrDefaultAsync(c => c.Id == itemId)
            ?? throw new NotFoundException("Checklist item not found.");
        await _access.RequireMemberAsync(item.Task.Column.BoardId, user.Id);
        return item;
    }

    private async Task BroadcastAsync(int boardId, int taskId)
    {
        var items = await _db.ChecklistItems
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.Position)
            .Select(c => new ChecklistItemDto(c.Id, c.Text, c.IsDone, c.Position))
            .ToListAsync();
        var task = await _db.Tasks.Where(t => t.Id == taskId).Select(Mappings.ToTaskDto).FirstAsync();

        await _notifier.SendAsync(boardId, BoardEvents.ChecklistChanged, new { taskId, items });
        await _notifier.SendAsync(boardId, BoardEvents.TaskUpserted, task);
    }
}
