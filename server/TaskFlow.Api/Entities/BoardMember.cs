namespace TaskFlow.Api.Entities;

public enum BoardRole
{
    Owner = 0,
    Member = 1
}

/// <summary>
/// Join table between users and boards. A user can only see a board
/// if a BoardMember row exists for them. The owner is simply the member with Role = Owner.
/// </summary>
public class BoardMember
{
    public int BoardId { get; set; }
    public Board Board { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    public BoardRole Role { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
