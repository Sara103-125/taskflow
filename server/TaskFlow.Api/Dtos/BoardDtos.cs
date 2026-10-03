using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Entities;

namespace TaskFlow.Api.Dtos;

/// <summary>The logged-in user, taken from the JWT and passed into services.</summary>
public record CurrentUser(string Id, string DisplayName);

// ---------- Responses ----------

public record BoardSummaryDto(int Id, string Name, BoardRole MyRole, int MemberCount, bool IsDemo, DateTime? ArchivedAt);

public record MemberDto(string UserId, string DisplayName, string Email, BoardRole Role);

public record ColumnDto(int Id, string Name, int Position);

/// <summary>
/// RowVersion is sent to the client (as base64) and must be sent back on update,
/// so the server can detect if someone else changed the task in between.
/// </summary>
public record TaskDto(
    int Id,
    int ColumnId,
    string Title,
    string? Description,
    string? AssigneeId,
    string? AssigneeName,
    DateOnly? DueDate,
    TaskPriority Priority,
    int Position,
    byte[] RowVersion,
    List<int> LabelIds,
    int ChecklistDone,
    int ChecklistTotal,
    int CommentCount);

public record BoardDetailDto(
    int Id,
    string Name,
    bool IsDemo,
    BoardRole MyRole,
    List<MemberDto> Members,
    List<ColumnDto> Columns,
    List<TaskDto> Tasks,
    List<LabelDto> Labels);

public record ActivityDto(int Id, string Message, DateTime CreatedAt);

public record OnlineUserDto(string UserId, string DisplayName);

/// <summary>"DisplayName has task TaskId open for editing right now."</summary>
public record EditorDto(int TaskId, string UserId, string DisplayName);

public record LabelDto(int Id, string Name, string Color);

public record ChecklistItemDto(int Id, string Text, bool IsDone, int Position);

public record CommentDto(int Id, int TaskId, string AuthorId, string AuthorName, string Body, DateTime CreatedAt);

public record HistoryDto(int Id, string UserName, string Change, DateTime CreatedAt);

/// <summary>Everything the task dialog shows besides the task itself.</summary>
public record TaskDetailsDto(List<ChecklistItemDto> Checklist, List<CommentDto> Comments, List<HistoryDto> History);

public record ArchivedTaskDto(int Id, string Title, string ColumnName, DateTime ArchivedAt);

/// <summary>A row on the "My tasks" page (tasks assigned to me on all my boards).</summary>
public record MyTaskDto(
    int Id,
    string Title,
    int BoardId,
    string BoardName,
    string ColumnName,
    DateOnly? DueDate,
    TaskPriority Priority,
    int ChecklistDone,
    int ChecklistTotal);

public record NotificationDto(int Id, int BoardId, int? TaskId, string Message, bool IsRead, DateTime CreatedAt);

public record NotificationsDto(int UnreadCount, List<NotificationDto> Items);

// ---------- Requests ----------

public record CreateBoardRequest([Required, MaxLength(100)] string Name);

public record RenameBoardRequest([Required, MaxLength(100)] string Name);

public record InviteMemberRequest([Required, EmailAddress] string Email);

public record CreateColumnRequest([Required, MaxLength(50)] string Name);

public record RenameColumnRequest([Required, MaxLength(50)] string Name);

public record ReorderColumnsRequest([Required] List<int> ColumnIds);

public record CreateTaskRequest(
    int ColumnId,
    [Required, MaxLength(200)] string Title,
    [MaxLength(4000)] string? Description,
    string? AssigneeId,
    DateOnly? DueDate,
    TaskPriority Priority = TaskPriority.Medium,
    List<int>? LabelIds = null);

public record UpdateTaskRequest(
    [Required, MaxLength(200)] string Title,
    [MaxLength(4000)] string? Description,
    string? AssigneeId,
    DateOnly? DueDate,
    TaskPriority Priority,
    [Required] byte[] RowVersion,
    List<int>? LabelIds = null);

public record MoveTaskRequest(int ColumnId, int Position);

public record LabelRequest(
    [Required, MaxLength(30)] string Name,
    [Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Color must look like #2563eb.")] string Color);

public record ChecklistItemRequest([Required, MaxLength(200)] string Text);

public record UpdateChecklistItemRequest([Required, MaxLength(200)] string Text, bool IsDone);

public record CommentRequest([Required, MaxLength(2000)] string Body);
