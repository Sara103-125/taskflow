using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Controllers;

/// <summary>Thin controller: HTTP in, call the service, HTTP out. Rules live in the services.</summary>
[Route("api/boards")]
public class BoardsController : ApiControllerBase
{
    private readonly BoardService _boards;
    private readonly ColumnService _columns;

    public BoardsController(BoardService boards, ColumnService columns)
    {
        _boards = boards;
        _columns = columns;
    }

    [HttpGet]
    public Task<List<BoardSummaryDto>> GetMine() => _boards.GetMyBoardsAsync(Me.Id);

    [HttpGet("{id:int}")]
    public Task<BoardDetailDto> Get(int id) => _boards.GetBoardAsync(id, Me.Id);

    [HttpPost]
    public Task<BoardSummaryDto> Create(CreateBoardRequest request) => _boards.CreateAsync(Me, request.Name);

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Rename(int id, RenameBoardRequest request)
    {
        await _boards.RenameAsync(id, Me, request.Name);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _boards.DeleteAsync(id, Me);
        return NoContent();
    }

    [HttpPost("{id:int}/archive")]
    public async Task<IActionResult> Archive(int id)
    {
        await _boards.ArchiveAsync(id, Me);
        return NoContent();
    }

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        await _boards.RestoreAsync(id, Me);
        return NoContent();
    }

    [HttpGet("{id:int}/archived-tasks")]
    public Task<List<ArchivedTaskDto>> ArchivedTasks(int id, [FromServices] TaskService tasks) =>
        tasks.GetArchivedAsync(id, Me.Id);

    [HttpPost("{id:int}/labels")]
    public Task<LabelDto> AddLabel(int id, LabelRequest request, [FromServices] LabelService labels) =>
        labels.CreateAsync(id, Me, request);

    [HttpPost("{id:int}/members")]
    public Task<List<MemberDto>> Invite(int id, InviteMemberRequest request) => _boards.InviteAsync(id, Me, request.Email);

    [HttpDelete("{id:int}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(int id, string userId)
    {
        await _boards.RemoveMemberAsync(id, Me, userId);
        return NoContent();
    }

    [HttpGet("{id:int}/activity")]
    public Task<List<ActivityDto>> Activity(int id, [FromQuery] int take = 30) =>
        _boards.GetActivityAsync(id, Me.Id, take);

    [HttpPost("{id:int}/columns")]
    public Task<ColumnDto> AddColumn(int id, CreateColumnRequest request) => _columns.CreateAsync(id, Me, request.Name);

    [HttpPut("{id:int}/columns/order")]
    public async Task<IActionResult> ReorderColumns(int id, ReorderColumnsRequest request)
    {
        await _columns.ReorderAsync(id, Me, request.ColumnIds);
        return NoContent();
    }
}
