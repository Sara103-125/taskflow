namespace TaskFlow.Api.Entities;

/// <summary>
/// A sub-step inside a task. Stored in its own table, so ticking an item does not change the
/// task row (and its RowVersion) - no false "updated by someone else" conflicts.
/// </summary>
public class ChecklistItem
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;

    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public int Position { get; set; }
}
