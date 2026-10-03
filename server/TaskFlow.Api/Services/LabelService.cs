using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Infrastructure;

namespace TaskFlow.Api.Services;

/// <summary>Any board member can manage the board's labels. The full list is broadcast after each change.</summary>
public class LabelService
{
    private readonly AppDbContext _db;
    private readonly BoardAccess _access;
    private readonly IBoardNotifier _notifier;

    public LabelService(AppDbContext db, BoardAccess access, IBoardNotifier notifier)
    {
        _db = db;
        _access = access;
        _notifier = notifier;
    }

    public async Task<LabelDto> CreateAsync(int boardId, CurrentUser user, LabelRequest request)
    {
        await _access.RequireMemberAsync(boardId, user.Id);
        var name = request.Name.Trim();
        if (await _db.Labels.AnyAsync(l => l.BoardId == boardId && l.Name == name))
            throw new BadRequestException($"There is already a label called '{name}'.");

        var label = new Label { BoardId = boardId, Name = name, Color = request.Color.ToLowerInvariant() };
        _db.Labels.Add(label);
        await _db.SaveChangesAsync();

        await BroadcastLabelsAsync(boardId);
        return label.ToDto();
    }

    public async Task UpdateAsync(int labelId, CurrentUser user, LabelRequest request)
    {
        var label = await FindLabelAsync(labelId, user);
        label.Name = request.Name.Trim();
        label.Color = request.Color.ToLowerInvariant();
        await _db.SaveChangesAsync();

        await BroadcastLabelsAsync(label.BoardId);
    }

    /// <summary>Removes the label from every task, then deletes it.</summary>
    public async Task DeleteAsync(int labelId, CurrentUser user)
    {
        var label = await FindLabelAsync(labelId, user);
        var links = await _db.TaskLabels.Where(tl => tl.LabelId == labelId).ToListAsync();
        var taskIds = links.Select(tl => tl.TaskId).ToList();

        _db.TaskLabels.RemoveRange(links);
        _db.Labels.Remove(label);
        await _db.SaveChangesAsync();

        await BroadcastLabelsAsync(label.BoardId);
        if (taskIds.Count > 0)
        {
            var tasks = await _db.Tasks.Where(t => taskIds.Contains(t.Id)).Select(Mappings.ToTaskDto).ToListAsync();
            await _notifier.SendAsync(label.BoardId, BoardEvents.TasksChanged, tasks);
        }
    }

    private async Task<Label> FindLabelAsync(int labelId, CurrentUser user)
    {
        var label = await _db.Labels.FirstOrDefaultAsync(l => l.Id == labelId)
            ?? throw new NotFoundException("Label not found.");
        await _access.RequireMemberAsync(label.BoardId, user.Id);
        return label;
    }

    private async Task BroadcastLabelsAsync(int boardId) =>
        await _notifier.SendAsync(boardId, BoardEvents.LabelsChanged, await BoardService.GetLabelsAsync(_db, boardId));
}
