using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Infrastructure;

namespace TaskFlow.Api.Services;

/// <summary>Any board member can manage columns. After each change the full column list is broadcast.</summary>
public class ColumnService
{
    private readonly AppDbContext _db;
    private readonly BoardAccess _access;
    private readonly IBoardNotifier _notifier;

    public ColumnService(AppDbContext db, BoardAccess access, IBoardNotifier notifier)
    {
        _db = db;
        _access = access;
        _notifier = notifier;
    }

    public async Task<ColumnDto> CreateAsync(int boardId, CurrentUser user, string name)
    {
        await _access.RequireMemberAsync(boardId, user.Id);

        var position = await _db.Columns.CountAsync(c => c.BoardId == boardId);
        var column = new Column { BoardId = boardId, Name = name.Trim(), Position = position };
        _db.Columns.Add(column);
        var activity = _db.AddActivity(boardId, user, $"{user.DisplayName} added column '{column.Name}'");
        await _db.SaveChangesAsync();

        await BroadcastColumnsAsync(boardId, activity);
        return column.ToDto();
    }

    public async Task RenameAsync(int columnId, CurrentUser user, string name)
    {
        var column = await FindColumnAsync(columnId, user);
        var oldName = column.Name;
        column.Name = name.Trim();
        var activity = _db.AddActivity(column.BoardId, user, $"{user.DisplayName} renamed column '{oldName}' to '{column.Name}'");
        await _db.SaveChangesAsync();

        await BroadcastColumnsAsync(column.BoardId, activity);
    }

    /// <summary>The client sends every column id of the board in the new order.</summary>
    public async Task ReorderAsync(int boardId, CurrentUser user, List<int> columnIds)
    {
        await _access.RequireMemberAsync(boardId, user.Id);

        var columns = await _db.Columns.Where(c => c.BoardId == boardId).ToListAsync();
        var sameSet = columnIds.Count == columns.Count
                      && columnIds.Distinct().Count() == columns.Count
                      && columns.All(c => columnIds.Contains(c.Id));
        if (!sameSet)
            throw new BadRequestException("The column list doesn't match this board's columns.");

        foreach (var column in columns)
            column.Position = columnIds.IndexOf(column.Id);
        var activity = _db.AddActivity(boardId, user, $"{user.DisplayName} reordered the columns");
        await _db.SaveChangesAsync();

        await BroadcastColumnsAsync(boardId, activity);
    }

    /// <summary>Deletes the column and its tasks. A board always keeps at least one column.</summary>
    public async Task DeleteAsync(int columnId, CurrentUser user)
    {
        var column = await FindColumnAsync(columnId, user);
        var boardColumns = await _db.Columns
            .Where(c => c.BoardId == column.BoardId)
            .OrderBy(c => c.Position)
            .ToListAsync();
        if (boardColumns.Count == 1)
            throw new BadRequestException("A board needs at least one column.");

        _db.Columns.Remove(column); // its tasks cascade
        boardColumns.Remove(column);
        for (var i = 0; i < boardColumns.Count; i++)
            boardColumns[i].Position = i;
        var activity = _db.AddActivity(column.BoardId, user, $"{user.DisplayName} deleted column '{column.Name}'");
        await _db.SaveChangesAsync();

        await BroadcastColumnsAsync(column.BoardId, activity);
    }

    private async Task<Column> FindColumnAsync(int columnId, CurrentUser user)
    {
        var column = await _db.Columns.FirstOrDefaultAsync(c => c.Id == columnId)
            ?? throw new NotFoundException("Column not found.");
        await _access.RequireMemberAsync(column.BoardId, user.Id);
        return column;
    }

    private async Task BroadcastColumnsAsync(int boardId, ActivityLog activity)
    {
        var columns = await _db.Columns
            .Where(c => c.BoardId == boardId)
            .OrderBy(c => c.Position)
            .Select(c => new ColumnDto(c.Id, c.Name, c.Position))
            .ToListAsync();
        await _notifier.SendAsync(boardId, BoardEvents.ColumnsChanged, columns);
        await _notifier.BroadcastAsync(activity);
    }
}
