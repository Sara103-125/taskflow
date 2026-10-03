namespace TaskFlow.Api.Entities;

/// <summary>
/// One line in a board's history, e.g. "Sara moved 'Login page' to Done".
/// The message is stored as plain text so it still reads correctly after
/// a task or column is renamed or deleted.
/// </summary>
public class ActivityLog
{
    public int Id { get; set; }

    public int BoardId { get; set; }
    public Board Board { get; set; } = null!;

    public string? UserId { get; set; }
    public AppUser? User { get; set; }

    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
