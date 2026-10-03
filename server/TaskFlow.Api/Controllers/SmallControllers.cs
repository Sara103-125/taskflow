using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Controllers;

// Small controllers for routes addressed by their own id (/api/labels/5, /api/comments/7, ...).
// Creating these items happens under their parent (POST /api/boards/1/labels, /api/tasks/3/comments).

[Route("api/labels")]
public class LabelsController : ApiControllerBase
{
    private readonly LabelService _labels;
    public LabelsController(LabelService labels) => _labels = labels;

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, LabelRequest request)
    {
        await _labels.UpdateAsync(id, Me, request);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _labels.DeleteAsync(id, Me);
        return NoContent();
    }
}

[Route("api/checklist")]
public class ChecklistController : ApiControllerBase
{
    private readonly ChecklistService _checklist;
    public ChecklistController(ChecklistService checklist) => _checklist = checklist;

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateChecklistItemRequest request)
    {
        await _checklist.UpdateAsync(id, Me, request);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _checklist.DeleteAsync(id, Me);
        return NoContent();
    }
}

[Route("api/comments")]
public class CommentsController : ApiControllerBase
{
    private readonly CommentService _comments;
    public CommentsController(CommentService comments) => _comments = comments;

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _comments.DeleteAsync(id, Me);
        return NoContent();
    }
}

[Route("api/notifications")]
public class NotificationsController : ApiControllerBase
{
    private readonly NotificationService _notifications;
    public NotificationsController(NotificationService notifications) => _notifications = notifications;

    [HttpGet]
    public Task<NotificationsDto> Get([FromQuery] int take = 20) => _notifications.GetAsync(Me.Id, take);

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        await _notifications.MarkReadAsync(id, Me.Id);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await _notifications.MarkAllReadAsync(Me.Id);
        return NoContent();
    }
}
