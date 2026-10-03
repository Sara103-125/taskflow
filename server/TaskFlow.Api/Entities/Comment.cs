namespace TaskFlow.Api.Entities;

public class Comment
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;

    public string AuthorId { get; set; } = string.Empty;
    public AppUser Author { get; set; } = null!;

    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
