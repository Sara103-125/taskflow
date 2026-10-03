using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Controllers;

[Route("api/columns")]
public class ColumnsController : ApiControllerBase
{
    private readonly ColumnService _columns;

    public ColumnsController(ColumnService columns) => _columns = columns;

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Rename(int id, RenameColumnRequest request)
    {
        await _columns.RenameAsync(id, Me, request.Name);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _columns.DeleteAsync(id, Me);
        return NoContent();
    }
}
