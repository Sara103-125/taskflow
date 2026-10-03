using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Infrastructure;

namespace TaskFlow.Api.Services;

public class TaskService
{
    private readonly AppDbContext _db;
    private readonly BoardAccess _access;
    private readonly IBoardNotifier _notifier;

    public TaskService(AppDbContext db, BoardAccess access, IBoardNotifier notifier)
    {
        _db = db;
        _access = access;
        _notifier = notifier;
    }

    public async Task<TaskDto> CreateAsync(CurrentUser user, CreateTaskRequest request)
    {
        var column = await _db.Columns.FirstOrDefaultAsync(c => c.Id == request.ColumnId)
            ?? throw new NotFoundException("Column not found.");
        await _access.RequireMemberAsync(column.BoardId, user.Id);
        await EnsureAssigneeIsMemberAsync(column.BoardId, request.AssigneeId);

        var task = new TaskItem
        {
            ColumnId = column.Id,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            AssigneeId = request.AssigneeId,
            DueDate = request.DueDate,
            Priority = request.Priority,
            Position = await _db.Tasks.CountAsync(t => t.ColumnId == column.Id) // add to the bottom
        };
        _db.Tasks.Add(task);
        var activity = _db.AddActivity(column.BoardId, user, $"{user.DisplayName} added '{task.Title}' to {column.Name}");
        await _db.SaveChangesAsync();

        var dto = await GetDtoAsync(task.Id);
        await _notifier.SendAsync(column.BoardId, BoardEvents.TaskUpserted, dto);
        await _notifier.BroadcastAsync(activity);
        return dto;
    }

    /// <summary>
    /// Optimistic concurrency: we tell EF the RowVersion the client last saw. EF then runs
    /// "UPDATE ... WHERE Id = @id AND RowVersion = @clientVersion". If another user saved in the
    /// meantime the row version is different, no row is updated, and EF throws
    /// DbUpdateConcurrencyException. We turn that into a 409 with the latest data.
    /// </summary>
    public async Task<TaskDto> UpdateAsync(int taskId, CurrentUser user, UpdateTaskRequest request)
    {
        var task = await FindTaskAsync(taskId, user);
        var boardId = task.Column.BoardId;
        await EnsureAssigneeIsMemberAsync(boardId, request.AssigneeId);

        _db.Entry(task).Property(t => t.RowVersion).OriginalValue = request.RowVersion;

        task.Title = request.Title.Trim();
        task.Description = request.Description?.Trim();
        task.AssigneeId = request.AssigneeId;
        task.DueDate = request.DueDate;
        task.Priority = request.Priority;
        task.UpdatedAt = DateTime.UtcNow;
        var activity = _db.AddActivity(boardId, user, $"{user.DisplayName} updated '{task.Title}'");

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            var latest = await _db.Tasks.Where(t => t.Id == taskId).Select(Mappings.ToTaskDto).FirstOrDefaultAsync()
                ?? throw new NotFoundException("This task was deleted by someone else.");
            throw new ConflictException(
                "This task was updated by someone else. Review their changes and try again.", latest);
        }

        var dto = await GetDtoAsync(task.Id);
        await _notifier.SendAsync(boardId, BoardEvents.TaskUpserted, dto);
        await _notifier.BroadcastAsync(activity);
        return dto;
    }

    /// <summary>
    /// Moves a task to (columnId, position). Both the old and the new column are renumbered
    /// 0..n-1 so positions never have gaps or duplicates. Every task whose position changed
    /// is broadcast, so other clients get the new order and new row versions.
    /// </summary>
    public async Task<List<TaskDto>> MoveAsync(int taskId, CurrentUser user, MoveTaskRequest request)
    {
        var task = await FindTaskAsync(taskId, user);
        var sourceColumn = task.Column;

        var targetColumn = await _db.Columns.FirstOrDefaultAsync(c => c.Id == request.ColumnId)
            ?? throw new NotFoundException("Target column not found.");
        if (targetColumn.BoardId != sourceColumn.BoardId)
            throw new BadRequestException("Tasks can only be moved within the same board.");

        var sourceTasks = await _db.Tasks
            .Where(t => t.ColumnId == sourceColumn.Id)
            .OrderBy(t => t.Position).ThenBy(t => t.Id)
            .ToListAsync();
        sourceTasks.Remove(task);

        var targetTasks = targetColumn.Id == sourceColumn.Id
            ? sourceTasks
            : await _db.Tasks
                .Where(t => t.ColumnId == targetColumn.Id)
                .OrderBy(t => t.Position).ThenBy(t => t.Id)
                .ToListAsync();

        var position = Math.Clamp(request.Position, 0, targetTasks.Count);
        targetTasks.Insert(position, task);
        task.ColumnId = targetColumn.Id;
        task.Column = targetColumn;

        Renumber(sourceTasks);
        Renumber(targetTasks);

        ActivityLog? activity = null;
        if (targetColumn.Id != sourceColumn.Id)
        {
            task.UpdatedAt = DateTime.UtcNow;
            activity = _db.AddActivity(targetColumn.BoardId, user, $"{user.DisplayName} moved '{task.Title}' to {targetColumn.Name}");
        }
        await _db.SaveChangesAsync();

        var columnIds = new[] { sourceColumn.Id, targetColumn.Id };
        var changed = await _db.Tasks
            .Where(t => columnIds.Contains(t.ColumnId))
            .OrderBy(t => t.Position)
            .Select(Mappings.ToTaskDto)
            .ToListAsync();

        await _notifier.SendAsync(targetColumn.BoardId, BoardEvents.TasksChanged, changed);
        if (activity is not null) await _notifier.BroadcastAsync(activity);
        return changed;
    }

    public async Task DeleteAsync(int taskId, CurrentUser user)
    {
        var task = await FindTaskAsync(taskId, user);
        var boardId = task.Column.BoardId;

        var remaining = await _db.Tasks
            .Where(t => t.ColumnId == task.ColumnId && t.Id != task.Id)
            .OrderBy(t => t.Position).ThenBy(t => t.Id)
            .ToListAsync();
        Renumber(remaining);

        _db.Tasks.Remove(task);
        var activity = _db.AddActivity(boardId, user, $"{user.DisplayName} deleted '{task.Title}'");
        await _db.SaveChangesAsync();

        await _notifier.SendAsync(boardId, BoardEvents.TaskDeleted, taskId);
        await _notifier.BroadcastAsync(activity);
    }

    private async Task<TaskItem> FindTaskAsync(int taskId, CurrentUser user)
    {
        var task = await _db.Tasks.Include(t => t.Column).FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw new NotFoundException("Task not found. It may have been deleted.");
        await _access.RequireMemberAsync(task.Column.BoardId, user.Id);
        return task;
    }

    private async Task EnsureAssigneeIsMemberAsync(int boardId, string? assigneeId)
    {
        if (assigneeId is null) return;
        if (!await _db.BoardMembers.AnyAsync(m => m.BoardId == boardId && m.UserId == assigneeId))
            throw new BadRequestException("The assignee must be a member of this board.");
    }

    private Task<TaskDto> GetDtoAsync(int taskId) =>
        _db.Tasks.Where(t => t.Id == taskId).Select(Mappings.ToTaskDto).FirstAsync();

    private static void Renumber(List<TaskItem> tasks)
    {
        for (var i = 0; i < tasks.Count; i++)
            if (tasks[i].Position != i) tasks[i].Position = i;
    }
}
