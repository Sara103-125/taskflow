namespace TaskFlow.Api.Entities;

/// <summary>A message for one user, e.g. "Sara assigned you 'Login page'". Shown under the bell icon.</summary>
public class Notification
{
    public int Id { get; set; }

    /// <summary>Who receives it.</summary>
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    public int BoardId { get; set; }
    public Board Board { get; set; } = null!;

    /// <summary>The task it is about (no foreign key: the task may be deleted later).</summary>
    public int? TaskId { get; set; }

    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
