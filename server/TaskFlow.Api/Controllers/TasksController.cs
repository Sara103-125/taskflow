using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Controllers;

[Route("api/tasks")]
public class TasksController : ApiControllerBase
{
    private readonly TaskService _tasks;

    public TasksController(TaskService tasks) => _tasks = tasks;

    /// <summary>Tasks assigned to me on all my boards.</summary>
    [HttpGet("mine")]
    public Task<List<MyTaskDto>> Mine() => _tasks.GetMyTasksAsync(Me.Id);

    /// <summary>Checklist, comments and history for the task dialog.</summary>
    [HttpGet("{id:int}/details")]
    public Task<TaskDetailsDto> Details(int id) => _tasks.GetDetailsAsync(id, Me.Id);

    [HttpPost]
    public Task<TaskDto> Create(CreateTaskRequest request) => _tasks.CreateAsync(Me, request);

    /// <summary>Returns 409 Conflict (with the latest task) if someone else saved it first.</summary>
    [HttpPut("{id:int}")]
    public Task<TaskDto> Update(int id, UpdateTaskRequest request) => _tasks.UpdateAsync(id, Me, request);

    [HttpPut("{id:int}/move")]
    public Task<List<TaskDto>> Move(int id, MoveTaskRequest request) => _tasks.MoveAsync(id, Me, request);

    [HttpPost("{id:int}/archive")]
    public async Task<IActionResult> Archive(int id)
    {
        await _tasks.ArchiveAsync(id, Me);
        return NoContent();
    }

    [HttpPost("{id:int}/restore")]
    public Task<TaskDto> Restore(int id) => _tasks.RestoreAsync(id, Me);

    /// <summary>Permanent delete.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _tasks.DeleteAsync(id, Me);
        return NoContent();
    }

    [HttpPost("{id:int}/checklist")]
    public Task<ChecklistItemDto> AddChecklistItem(int id, ChecklistItemRequest request, [FromServices] ChecklistService checklist) =>
        checklist.AddAsync(id, Me, request.Text);

    [HttpPost("{id:int}/comments")]
    public Task<CommentDto> AddComment(int id, CommentRequest request, [FromServices] CommentService comments) =>
        comments.AddAsync(id, Me, request.Body);
}
