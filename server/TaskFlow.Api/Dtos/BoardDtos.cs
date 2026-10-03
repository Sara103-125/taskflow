using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Entities;

namespace TaskFlow.Api.Dtos;

/// <summary>The logged-in user, taken from the JWT and passed into services.</summary>
public record CurrentUser(string Id, string DisplayName);

// ---------- Responses ----------

public record BoardSummaryDto(int Id, string Name, BoardRole MyRole, int MemberCount, bool IsDemo);

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
    byte[] RowVersion);

public record BoardDetailDto(
    int Id,
    string Name,
    bool IsDemo,
    BoardRole MyRole,
    List<MemberDto> Members,
    List<ColumnDto> Columns,
    List<TaskDto> Tasks);

public record ActivityDto(int Id, string Message, DateTime CreatedAt);

public record OnlineUserDto(string UserId, string DisplayName);

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
    TaskPriority Priority = TaskPriority.Medium);

public record UpdateTaskRequest(
    [Required, MaxLength(200)] string Title,
    [MaxLength(4000)] string? Description,
    string? AssigneeId,
    DateOnly? DueDate,
    TaskPriority Priority,
    [Required] byte[] RowVersion);

public record MoveTaskRequest(int ColumnId, int Position);
