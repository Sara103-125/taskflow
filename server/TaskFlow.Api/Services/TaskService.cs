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
        var labelIds = await ValidLabelIdsAsync(column.BoardId, request.LabelIds);

        var task = new TaskItem
        {
            ColumnId = column.Id,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            AssigneeId = request.AssigneeId,
            DueDate = request.DueDate,
            Priority = request.Priority,
            Position = await _db.Tasks.CountAsync(t => t.ColumnId == column.Id), // add to the bottom
            TaskLabels = labelIds.Select(id => new TaskLabel { LabelId = id }).ToList()
        };
        _db.Tasks.Add(task);
        _db.AddHistory(task, user, $"created this task in {column.Name}");
        var activity = _db.AddActivity(column.BoardId, user, $"{user.DisplayName} added '{task.Title}' to {column.Name}");
        await _db.SaveChangesAsync();

        // The notification needs the new task's id, which only exists after the first save.
        var notification = task.AssigneeId is null ? null
            : _db.AddNotification(task.AssigneeId, user, column.BoardId, task.Id, $"{user.DisplayName} assigned you '{task.Title}'");
        if (notification is not null) await _db.SaveChangesAsync();

        var dto = await GetDtoAsync(task.Id);
        await _notifier.SendAsync(column.BoardId, BoardEvents.TaskUpserted, dto);
        await _notifier.BroadcastAsync(activity);
        await _notifier.PushAsync(new[] { notification });
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
        var task = await FindTaskAsync(taskId, user.Id, q => q.Include(t => t.TaskLabels).ThenInclude(tl => tl.Label));
        var boardId = task.Column.BoardId;
        await EnsureAssigneeIsMemberAsync(boardId, request.AssigneeId);
        var newLabelIds = await ValidLabelIdsAsync(boardId, request.LabelIds);

        _db.Entry(task).Property(t => t.RowVersion).OriginalValue = request.RowVersion;

        var changes = await DescribeChangesAsync(task, request, newLabelIds);
        var assigneeChanged = task.AssigneeId != request.AssigneeId;

        task.Title = request.Title.Trim();
        task.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        task.AssigneeId = request.AssigneeId;
        task.DueDate = request.DueDate;
        task.Priority = request.Priority;
        task.UpdatedAt = DateTime.UtcNow;
        task.TaskLabels.RemoveAll(tl => !newLabelIds.Contains(tl.LabelId));
        foreach (var id in newLabelIds.Where(id => task.TaskLabels.All(tl => tl.LabelId != id)))
            task.TaskLabels.Add(new TaskLabel { LabelId = id });

        foreach (var change in changes) _db.AddHistory(task, user, change);
        var activity = changes.Count > 0 ? _db.AddActivity(boardId, user, $"{user.DisplayName} updated '{task.Title}'") : null;
        var notification = assigneeChanged && task.AssigneeId is not null
            ? _db.AddNotification(task.AssigneeId, user, boardId, task.Id, $"{user.DisplayName} assigned you '{task.Title}'")
            : null;

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
        if (activity is not null) await _notifier.BroadcastAsync(activity);
        await _notifier.PushAsync(new[] { notification });
        return dto;
    }

    /// <summary>
    /// Moves a task to (columnId, position). Both the old and the new column are renumbered
    /// 0..n-1 so positions never have gaps or duplicates. Every task whose position changed
    /// is broadcast, so other clients get the new order and new row versions.
    /// </summary>
    public async Task<List<TaskDto>> MoveAsync(int taskId, CurrentUser user, MoveTaskRequest request)
    {
        var task = await FindTaskAsync(taskId, user.Id);
        var sourceColumn = task.Column;

        var targetColumn = await _db.Columns.FirstOrDefaultAsync(c => c.Id == request.ColumnId)
            ?? throw new NotFoundException("Target column not found.");
        if (targetColumn.BoardId != sourceColumn.BoardId)
            throw new BadRequestException("Tasks can only be moved within the same board.");

        var sourceTasks = await TasksInColumnAsync(sourceColumn.Id);
        sourceTasks.Remove(task);

        var targetTasks = targetColumn.Id == sourceColumn.Id ? sourceTasks : await TasksInColumnAsync(targetColumn.Id);

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
            _db.AddHistory(task, user, $"moved this task from {sourceColumn.Name} to {targetColumn.Name}");
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

    /// <summary>
    /// Soft delete. The global query filter hides the task from now on; the rest of the
    /// column is renumbered so positions stay 0..n-1.
    /// </summary>
    public async Task ArchiveAsync(int taskId, CurrentUser user)
    {
        var task = await FindTaskAsync(taskId, user.Id);
        var boardId = task.Column.BoardId;

        var remaining = await TasksInColumnAsync(task.ColumnId);
        remaining.Remove(task);
        Renumber(remaining);

        task.ArchivedAt = DateTime.UtcNow;
        _db.AddHistory(task, user, "archived this task");
        var activity = _db.AddActivity(boardId, user, $"{user.DisplayName} archived '{task.Title}'");
        await _db.SaveChangesAsync();

        await _notifier.SendAsync(boardId, BoardEvents.TaskDeleted, taskId);
        await BroadcastColumnAsync(boardId, task.ColumnId);
        await _notifier.BroadcastAsync(activity);
    }

    /// <summary>Brings an archived task back, at the bottom of its column.</summary>
    public async Task<TaskDto> RestoreAsync(int taskId, CurrentUser user)
    {
        var task = await FindTaskAsync(taskId, user.Id, includeArchived: true);
        if (task.ArchivedAt is null)
            throw new BadRequestException("This task is not archived.");

        task.Position = await _db.Tasks.CountAsync(t => t.ColumnId == task.ColumnId);
        task.ArchivedAt = null;
        _db.AddHistory(task, user, "restored this task");
        var activity = _db.AddActivity(task.Column.BoardId, user, $"{user.DisplayName} restored '{task.Title}'");
        await _db.SaveChangesAsync();

        var dto = await GetDtoAsync(task.Id);
        await _notifier.SendAsync(task.Column.BoardId, BoardEvents.TaskUpserted, dto);
        await _notifier.BroadcastAsync(activity);
        return dto;
    }

    /// <summary>Permanent delete (the UI offers it from the archive).</summary>
    public async Task DeleteAsync(int taskId, CurrentUser user)
    {
        var task = await FindTaskAsync(taskId, user.Id, includeArchived: true);
        var boardId = task.Column.BoardId;
        var wasActive = task.ArchivedAt is null;

        if (wasActive)
        {
            var remaining = await TasksInColumnAsync(task.ColumnId);
            remaining.Remove(task);
            Renumber(remaining);
        }

        _db.Tasks.Remove(task); // checklist, comments, history and label links cascade
        var activity = _db.AddActivity(boardId, user, $"{user.DisplayName} deleted '{task.Title}'");
        await _db.SaveChangesAsync();

        if (wasActive)
        {
            await _notifier.SendAsync(boardId, BoardEvents.TaskDeleted, taskId);
            await BroadcastColumnAsync(boardId, task.ColumnId);
        }
        await _notifier.BroadcastAsync(activity);
    }

    public async Task<List<ArchivedTaskDto>> GetArchivedAsync(int boardId, string userId)
    {
        await _access.RequireMemberAsync(boardId, userId);
        return await _db.Tasks.IgnoreQueryFilters()
            .Where(t => t.Column.BoardId == boardId && t.ArchivedAt != null)
            .OrderByDescending(t => t.ArchivedAt)
            .Select(t => new ArchivedTaskDto(t.Id, t.Title, t.Column.Name, t.ArchivedAt!.Value))
            .ToListAsync();
    }

    /// <summary>Checklist, comments and history for the task dialog.</summary>
    public async Task<TaskDetailsDto> GetDetailsAsync(int taskId, string userId)
    {
        var task = await FindTaskAsync(taskId, userId);

        var checklist = await _db.ChecklistItems
            .Where(c => c.TaskId == task.Id)
            .OrderBy(c => c.Position)
            .Select(c => new ChecklistItemDto(c.Id, c.Text, c.IsDone, c.Position))
            .ToListAsync();
        var comments = await _db.Comments
            .Where(c => c.TaskId == task.Id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new CommentDto(c.Id, c.TaskId, c.AuthorId, c.Author.DisplayName, c.Body, c.CreatedAt))
            .ToListAsync();
        var history = await _db.TaskHistory
            .Where(h => h.TaskId == task.Id)
            .OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id)
            .Take(50)
            .Select(h => new HistoryDto(h.Id, h.UserName, h.Change, h.CreatedAt))
            .ToListAsync();

        return new TaskDetailsDto(checklist, comments, history);
    }

    /// <summary>Every task assigned to me on boards I'm still a member of, soonest due date first.</summary>
    public Task<List<MyTaskDto>> GetMyTasksAsync(string userId) =>
        _db.Tasks
            .Where(t => t.AssigneeId == userId
                        && t.Column.Board.ArchivedAt == null
                        && t.Column.Board.Members.Any(m => m.UserId == userId))
            .OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenBy(t => t.Title)
            .Select(t => new MyTaskDto(
                t.Id, t.Title, t.Column.BoardId, t.Column.Board.Name, t.Column.Name, t.DueDate, t.Priority,
                t.Checklist.Count(c => c.IsDone), t.Checklist.Count))
            .ToListAsync();

    // ---------- Helpers ----------

    private async Task<TaskItem> FindTaskAsync(
        int taskId, string userId,
        Func<IQueryable<TaskItem>, IQueryable<TaskItem>>? include = null,
        bool includeArchived = false)
    {
        IQueryable<TaskItem> query = _db.Tasks.Include(t => t.Column);
        if (includeArchived) query = query.IgnoreQueryFilters();
        if (include is not null) query = include(query);

        var task = await query.FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw new NotFoundException("Task not found. It may have been deleted or archived.");
        await _access.RequireMemberAsync(task.Column.BoardId, userId);
        return task;
    }

    private Task<List<TaskItem>> TasksInColumnAsync(int columnId) =>
        _db.Tasks.Where(t => t.ColumnId == columnId).OrderBy(t => t.Position).ThenBy(t => t.Id).ToListAsync();

    private async Task BroadcastColumnAsync(int boardId, int columnId)
    {
        var tasks = await _db.Tasks.Where(t => t.ColumnId == columnId).Select(Mappings.ToTaskDto).ToListAsync();
        if (tasks.Count > 0) await _notifier.SendAsync(boardId, BoardEvents.TasksChanged, tasks);
    }

    private async Task EnsureAssigneeIsMemberAsync(int boardId, string? assigneeId)
    {
        if (assigneeId is null) return;
        if (!await _db.BoardMembers.AnyAsync(m => m.BoardId == boardId && m.UserId == assigneeId))
            throw new BadRequestException("The assignee must be a member of this board.");
    }

    private async Task<List<int>> ValidLabelIdsAsync(int boardId, List<int>? labelIds)
    {
        var ids = labelIds?.Distinct().ToList() ?? new List<int>();
        if (ids.Count == 0) return ids;
        var valid = await _db.Labels.CountAsync(l => l.BoardId == boardId && ids.Contains(l.Id));
        if (valid != ids.Count)
            throw new BadRequestException("Labels must belong to this board.");
        return ids;
    }

    /// <summary>Human-readable list of what an edit changes, for the task history.</summary>
    private async Task<List<string>> DescribeChangesAsync(TaskItem task, UpdateTaskRequest request, List<int> newLabelIds)
    {
        var changes = new List<string>();
        var title = request.Title.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        if (task.Title != title)
            changes.Add($"renamed the task from '{task.Title}' to '{title}'");
        if (task.Description != description)
            changes.Add("edited the description");
        if (task.Priority != request.Priority)
            changes.Add($"changed priority from {task.Priority} to {request.Priority}");
        if (task.DueDate != request.DueDate)
            changes.Add(request.DueDate is null ? "removed the due date" : $"set the due date to {request.DueDate:yyyy-MM-dd}");
        if (task.AssigneeId != request.AssigneeId)
        {
            var name = request.AssigneeId is null ? null
                : await _db.Users.Where(u => u.Id == request.AssigneeId).Select(u => u.DisplayName).FirstAsync();
            changes.Add(name is null ? "unassigned the task" : $"assigned the task to {name}");
        }

        var oldLabelIds = task.TaskLabels.Select(tl => tl.LabelId).ToHashSet();
        if (!oldLabelIds.SetEquals(newLabelIds))
        {
            var names = await _db.Labels.Where(l => newLabelIds.Contains(l.Id)).Select(l => l.Name).ToListAsync();
            changes.Add(names.Count == 0 ? "removed all labels" : $"set labels to {string.Join(", ", names.OrderBy(n => n))}");
        }
        return changes;
    }

    private Task<TaskDto> GetDtoAsync(int taskId) =>
        _db.Tasks.IgnoreQueryFilters().Where(t => t.Id == taskId).Select(Mappings.ToTaskDto).FirstAsync();

    private static void Renumber(List<TaskItem> tasks)
    {
        for (var i = 0; i < tasks.Count; i++)
            if (tasks[i].Position != i) tasks[i].Position = i;
    }
}
