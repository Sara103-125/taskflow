using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Infrastructure;

namespace TaskFlow.Api.Services;

/// <summary>
/// The single place that answers "is this user allowed to touch this board?".
/// Every service method calls one of these before reading or changing board data.
/// </summary>
public class BoardAccess
{
    private readonly AppDbContext _db;

    public BoardAccess(AppDbContext db) => _db = db;

    public async Task<BoardMember> RequireMemberAsync(int boardId, string userId)
    {
        var member = await _db.BoardMembers
            .Include(m => m.Board)
            .FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == userId);

        if (member is not null)
            return member;

        if (!await _db.Boards.AnyAsync(b => b.Id == boardId))
            throw new NotFoundException("Board not found.");

        throw new ForbiddenException("You are not a member of this board.");
    }

    public async Task<BoardMember> RequireOwnerAsync(int boardId, string userId)
    {
        var member = await RequireMemberAsync(boardId, userId);
        if (member.Role != BoardRole.Owner)
            throw new ForbiddenException("Only the board owner can do that.");
        return member;
    }
}
