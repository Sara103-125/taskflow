namespace TaskFlow.Api.Hubs;

/// <summary>Names of the server -> client SignalR messages. The Angular client listens for the same names.</summary>
public static class BoardEvents
{
    public const string TaskUpserted = "TaskUpserted";     // TaskDto (created or edited)
    public const string TaskDeleted = "TaskDeleted";       // int taskId
    public const string TasksChanged = "TasksChanged";     // TaskDto[] (several tasks at once, e.g. after a move)
    public const string ColumnsChanged = "ColumnsChanged"; // ColumnDto[] (full list, in order)
    public const string BoardRenamed = "BoardRenamed";     // string name
    public const string BoardDeleted = "BoardDeleted";     // int boardId
    public const string MembersChanged = "MembersChanged"; // MemberDto[]
    public const string ActivityAdded = "ActivityAdded";   // ActivityDto
    public const string OnlineUsers = "OnlineUsers";       // OnlineUserDto[]

    public static string Group(int boardId) => $"board-{boardId}";
}
