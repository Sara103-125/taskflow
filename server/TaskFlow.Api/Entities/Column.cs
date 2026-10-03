namespace TaskFlow.Api.Entities;

public class Column
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public Board Board { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    /// <summary>0-based order of the column on the board (left to right).</summary>
    public int Position { get; set; }

    public List<TaskItem> Tasks { get; set; } = new();
}
