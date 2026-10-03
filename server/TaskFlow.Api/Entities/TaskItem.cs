using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.Entities;

public enum TaskPriority
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>
/// A card on the board. Named TaskItem to avoid clashing with System.Threading.Tasks.Task.
/// </summary>
public class TaskItem
{
    public int Id { get; set; }

    public int ColumnId { get; set; }
    public Column Column { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string? AssigneeId { get; set; }
    public AppUser? Assignee { get; set; }

    public DateOnly? DueDate { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    /// <summary>0-based order of the task inside its column (top to bottom).</summary>
    public int Position { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// SQL Server "rowversion" column. The database changes it automatically on every update.
    /// EF Core adds "WHERE RowVersion = @original" to UPDATEs, so if someone else saved first,
    /// zero rows match and EF throws DbUpdateConcurrencyException instead of silently overwriting.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
