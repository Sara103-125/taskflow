using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Infrastructure;

namespace TaskFlow.Api.Services;

public class BoardService
{
    public static readonly string[] DefaultColumns = { "To Do", "In Progress", "Done" };

    private readonly AppDbContext _db;
    private readonly BoardAccess _access;
    private readonly IBoardNotifier _notifier;

    public BoardService(AppDbContext db, BoardAccess access, IBoardNotifier notifier)
    {
        _db = db;
        _access = access;
        _notifier = notifier;
    }

    /// <summary>Only boards the user is a member of. Archived boards are only listed for their owner.</summary>
    public Task<List<BoardSummaryDto>> GetMyBoardsAsync(string userId) =>
        _db.BoardMembers
            .Where(m => m.UserId == userId && (m.Board.ArchivedAt == null || m.Role == BoardRole.Owner))
            .OrderBy(m => m.Board.Name)
            .Select(m => new BoardSummaryDto(
                m.BoardId, m.Board.Name, m.Role, m.Board.Members.Count, m.Board.IsDemo, m.Board.ArchivedAt))
            .ToListAsync();

    public async Task<BoardDetailDto> GetBoardAsync(int boardId, string userId)
    {
        var me = await _access.RequireMemberAsync(boardId, userId);

        var members = await GetMembersAsync(boardId);
        var columns = await _db.Columns
            .Where(c => c.BoardId == boardId)
            .OrderBy(c => c.Position)
            .Select(c => new ColumnDto(c.Id, c.Name, c.Position))
            .ToListAsync();
        var tasks = await _db.Tasks
            .Where(t => t.Column.BoardId == boardId)
            .OrderBy(t => t.Position)
            .Select(Mappings.ToTaskDto)
            .ToListAsync();
        var labels = await GetLabelsAsync(_db, boardId);

        return new BoardDetailDto(boardId, me.Board.Name, me.Board.IsDemo, me.Role, members, columns, tasks, labels);
    }

    public async Task<BoardSummaryDto> CreateAsync(CurrentUser user, string name)
    {
        var board = new Board { Name = name.Trim() };
        board.Members.Add(new BoardMember { UserId = user.Id, Role = BoardRole.Owner });
        for (var i = 0; i < DefaultColumns.Length; i++)
            board.Columns.Add(new Column { Name = DefaultColumns[i], Position = i });
        board.Activity.Add(new ActivityLog { UserId = user.Id, Message = $"{user.DisplayName} created the board" });

        _db.Boards.Add(board);
        await _db.SaveChangesAsync();

        return new BoardSummaryDto(board.Id, board.Name, BoardRole.Owner, 1, false, null);
    }

    public async Task RenameAsync(int boardId, CurrentUser user, string name)
    {
        var owner = await _access.RequireOwnerAsync(boardId, user.Id);
        owner.Board.Name = name.Trim();
        var activity = _db.AddActivity(boardId, user, $"{user.DisplayName} renamed the board to '{owner.Board.Name}'");
        await _db.SaveChangesAsync();

        await _notifier.SendAsync(boardId, BoardEvents.BoardRenamed, owner.Board.Name);
        await _notifier.BroadcastAsync(activity);
    }

    /// <summary>Permanent delete. Works on archived boards too.</summary>
    public async Task DeleteAsync(int boardId, CurrentUser user)
    {
        var owner = await _access.RequireOwnerAsync(boardId, user.Id, allowArchived: true);
        if (owner.Board.IsDemo)
            throw new ForbiddenException("The demo board can't be deleted.");

        await RemoveBoardAsync(_db, owner.Board);
        await _db.SaveChangesAsync();

        await _notifier.SendAsync(boardId, BoardEvents.BoardDeleted, boardId);
    }

    /// <summary>
    /// Marks a board for deletion. Columns, tasks, members, labels and activity cascade in the
    /// database, but task-label links must go first (see the TaskLabel comment in AppDbContext).
    /// </summary>
    public static async Task RemoveBoardAsync(AppDbContext db, Board board)
    {
        db.TaskLabels.RemoveRange(await db.TaskLabels.Where(tl => tl.Label.BoardId == board.Id).ToListAsync());
        db.Boards.Remove(board);
    }

    /// <summary>Soft delete: hides the board from everyone but the owner, who can restore it.</summary>
    public async Task ArchiveAsync(int boardId, CurrentUser user)
    {
        var owner = await _access.RequireOwnerAsync(boardId, user.Id);
        if (owner.Board.IsDemo)
            throw new ForbiddenException("The demo board can't be archived.");

        owner.Board.ArchivedAt = DateTime.UtcNow;
        _db.AddActivity(boardId, user, $"{user.DisplayName} archived the board");
        await _db.SaveChangesAsync();

        await _notifier.SendAsync(boardId, BoardEvents.BoardArchived, boardId);
    }

    public async Task RestoreAsync(int boardId, CurrentUser user)
    {
        var owner = await _access.RequireOwnerAsync(boardId, user.Id, allowArchived: true);
        owner.Board.ArchivedAt = null;
        _db.AddActivity(boardId, user, $"{user.DisplayName} restored the board");
        await _db.SaveChangesAsync();
    }

    public async Task<List<MemberDto>> InviteAsync(int boardId, CurrentUser user, string email)
    {
        var owner = await _access.RequireOwnerAsync(boardId, user.Id);
        if (owner.Board.IsDemo)
            throw new ForbiddenException("Members can't be changed on the demo board.");

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var invitee = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail)
            ?? throw new NotFoundException("No TaskFlow user with that email. Ask them to register first.");

        if (await _db.BoardMembers.AnyAsync(m => m.BoardId == boardId && m.UserId == invitee.Id))
            throw new BadRequestException($"{invitee.DisplayName} is already a member.");

        _db.BoardMembers.Add(new BoardMember { BoardId = boardId, UserId = invitee.Id, Role = BoardRole.Member });
        var activity = _db.AddActivity(boardId, user, $"{user.DisplayName} added {invitee.DisplayName} to the board");
        var notification = _db.AddNotification(invitee.Id, user, boardId, null,
            $"{user.DisplayName} added you to the board '{owner.Board.Name}'");
        await _db.SaveChangesAsync();

        var members = await GetMembersAsync(boardId);
        await _notifier.SendAsync(boardId, BoardEvents.MembersChanged, members);
        await _notifier.BroadcastAsync(activity);
        await _notifier.PushAsync(new[] { notification });
        return members;
    }

    /// <summary>The owner can remove any member; a member can remove themselves (leave the board).</summary>
    public async Task RemoveMemberAsync(int boardId, CurrentUser user, string memberUserId)
    {
        var me = await _access.RequireMemberAsync(boardId, user.Id);
        var leavingSelf = memberUserId == user.Id;

        if (!leavingSelf && me.Role != BoardRole.Owner)
            throw new ForbiddenException("Only the board owner can remove members.");
        if (me.Board.IsDemo)
            throw new ForbiddenException("Members can't be changed on the demo board.");

        var member = await _db.BoardMembers.Include(m => m.User)
            .FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == memberUserId)
            ?? throw new NotFoundException("That user is not a member of this board.");
        if (member.Role == BoardRole.Owner)
            throw new BadRequestException("The owner can't be removed. Delete the board instead.");

        // Unassign their tasks on this board, archived ones too (the FK has no cascade on purpose).
        var theirTasks = await _db.Tasks.IgnoreQueryFilters()
            .Where(t => t.Column.BoardId == boardId && t.AssigneeId == memberUserId)
            .ToListAsync();
        foreach (var task in theirTasks) task.AssigneeId = null;

        _db.BoardMembers.Remove(member);
        var message = leavingSelf
            ? $"{user.DisplayName} left the board"
            : $"{user.DisplayName} removed {member.User.DisplayName} from the board";
        var activity = _db.AddActivity(boardId, user, message);
        await _db.SaveChangesAsync();

        // Tell the group (including the removed user, so their screen can react) and only then
        // cut the removed user's connections off from the group.
        await _notifier.SendAsync(boardId, BoardEvents.MembersChanged, await GetMembersAsync(boardId));
        await _notifier.RemoveUserFromBoardAsync(boardId, memberUserId);
        if (theirTasks.Count > 0)
        {
            var ids = theirTasks.Select(t => t.Id).ToList();
            var updated = await _db.Tasks.Where(t => ids.Contains(t.Id)).Select(Mappings.ToTaskDto).ToListAsync();
            await _notifier.SendAsync(boardId, BoardEvents.TasksChanged, updated);
        }
        await _notifier.BroadcastAsync(activity);
    }

    public async Task<List<ActivityDto>> GetActivityAsync(int boardId, string userId, int take)
    {
        await _access.RequireMemberAsync(boardId, userId);
        return await _db.ActivityLogs
            .Where(a => a.BoardId == boardId)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(Math.Clamp(take, 1, 100))
            .Select(a => new ActivityDto(a.Id, a.Message, a.CreatedAt))
            .ToListAsync();
    }

    public static Task<List<LabelDto>> GetLabelsAsync(AppDbContext db, int boardId) =>
        db.Labels
            .Where(l => l.BoardId == boardId)
            .OrderBy(l => l.Name)
            .Select(l => new LabelDto(l.Id, l.Name, l.Color))
            .ToListAsync();

    private Task<List<MemberDto>> GetMembersAsync(int boardId) =>
        _db.BoardMembers
            .Where(m => m.BoardId == boardId)
            .OrderBy(m => m.Role).ThenBy(m => m.User.DisplayName)
            .Select(m => new MemberDto(m.UserId, m.User.DisplayName, m.User.Email!, m.Role))
            .ToListAsync();
}
