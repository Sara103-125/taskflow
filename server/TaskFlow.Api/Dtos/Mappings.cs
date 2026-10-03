using System.Linq.Expressions;
using TaskFlow.Api.Entities;

namespace TaskFlow.Api.Dtos;

public static class Mappings
{
    /// <summary>Used inside EF queries so only the needed columns are selected.</summary>
    public static readonly Expression<Func<TaskItem, TaskDto>> ToTaskDto = t => new TaskDto(
        t.Id,
        t.ColumnId,
        t.Title,
        t.Description,
        t.AssigneeId,
        t.Assignee != null ? t.Assignee.DisplayName : null,
        t.DueDate,
        t.Priority,
        t.Position,
        t.RowVersion,
        t.TaskLabels.Select(tl => tl.LabelId).ToList(),
        t.Checklist.Count(c => c.IsDone),
        t.Checklist.Count,
        t.Comments.Count);

    public static ColumnDto ToDto(this Column c) => new(c.Id, c.Name, c.Position);

    public static ActivityDto ToDto(this ActivityLog a) => new(a.Id, a.Message, a.CreatedAt);

    public static LabelDto ToDto(this Label l) => new(l.Id, l.Name, l.Color);

    public static ChecklistItemDto ToDto(this ChecklistItem c) => new(c.Id, c.Text, c.IsDone, c.Position);

    public static NotificationDto ToDto(this Notification n) =>
        new(n.Id, n.BoardId, n.TaskId, n.Message, n.IsRead, n.CreatedAt);
}
