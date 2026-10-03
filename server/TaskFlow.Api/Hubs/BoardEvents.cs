namespace TaskFlow.Api.Hubs;

/// <summary>Names of the server -> client SignalR messages. The Angular client listens for the same names.</summary>
public static class BoardEvents
{
    public const string TaskUpserted = "TaskUpserted";     // TaskDto (created or edited)
    public const string TaskDeleted = "TaskDeleted";       // int taskId (deleted or archived)
    public const string TasksChanged = "TasksChanged";     // TaskDto[] (several tasks at once, e.g. after a move)
    public const string ColumnsChanged = "ColumnsChanged"; // ColumnDto[] (full list, in order)
    public const string BoardRenamed = "BoardRenamed";     // string name
    public const string BoardDeleted = "BoardDeleted";     // int boardId
    public const string BoardArchived = "BoardArchived";   // int boardId
    public const string MembersChanged = "MembersChanged"; // MemberDto[]
    public const string ActivityAdded = "ActivityAdded";   // ActivityDto
    public const string OnlineUsers = "OnlineUsers";       // OnlineUserDto[]
    public const string EditorsChanged = "EditorsChanged"; // EditorDto[] (who has which task open)
    public const string LabelsChanged = "LabelsChanged";   // LabelDto[]
    public const string ChecklistChanged = "ChecklistChanged"; // { taskId, items: ChecklistItemDto[] }
    public const string CommentAdded = "CommentAdded";     // CommentDto
    public const string CommentDeleted = "CommentDeleted"; // { taskId, commentId }

    // Sent to one user (all their tabs), not to a board group.
    public const string NotificationAdded = "NotificationAdded"; // NotificationDto

    public static string Group(int boardId) => $"board-{boardId}";
}
