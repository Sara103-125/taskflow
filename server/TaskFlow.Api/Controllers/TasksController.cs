using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Controllers;

[Route("api/tasks")]
public class TasksController : ApiControllerBase
{
    private readonly TaskService _tasks;

    public TasksController(TaskService tasks) => _tasks = tasks;

    [HttpPost]
    public Task<TaskDto> Create(CreateTaskRequest request) => _tasks.CreateAsync(Me, request);

    /// <summary>Returns 409 Conflict (with the latest task) if someone else saved it first.</summary>
    [HttpPut("{id:int}")]
    public Task<TaskDto> Update(int id, UpdateTaskRequest request) => _tasks.UpdateAsync(id, Me, request);

    [HttpPut("{id:int}/move")]
    public Task<List<TaskDto>> Move(int id, MoveTaskRequest request) => _tasks.MoveAsync(id, Me, request);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _tasks.DeleteAsync(id, Me);
        return NoContent();
    }
}
