namespace TaskFlow.Api.Entities;

/// <summary>
/// One change to one task, e.g. "Sara changed priority from Low to High".
/// The user's name is copied in as text, like ActivityLog, so history never breaks.
/// </summary>
public class TaskHistory
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;

    public string UserName { get; set; } = string.Empty;
    public string Change { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
