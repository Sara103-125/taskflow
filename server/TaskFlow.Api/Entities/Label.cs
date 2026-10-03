namespace TaskFlow.Api.Entities;

/// <summary>A coloured tag that belongs to one board, e.g. "Bug" (red) or "Frontend" (blue).</summary>
public class Label
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public Board Board { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    /// <summary>Hex colour like "#2563eb".</summary>
    public string Color { get; set; } = "#2563eb";

    public List<TaskLabel> TaskLabels { get; set; } = new();
}

/// <summary>Join table: which labels are on which task (many-to-many).</summary>
public class TaskLabel
{
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;

    public int LabelId { get; set; }
    public Label Label { get; set; } = null!;
}
