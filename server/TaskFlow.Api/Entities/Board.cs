namespace TaskFlow.Api.Entities;

public class Board
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True for the seeded demo board. It cannot be deleted and is reset daily.</summary>
    public bool IsDemo { get; set; }

    public List<BoardMember> Members { get; set; } = new();
    public List<Column> Columns { get; set; } = new();
    public List<ActivityLog> Activity { get; set; } = new();
}
