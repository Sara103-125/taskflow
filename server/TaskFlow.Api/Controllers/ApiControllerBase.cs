using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Auth;
using TaskFlow.Api.Dtos;

namespace TaskFlow.Api.Controllers;

/// <summary>Base for the board/column/task controllers: all require a valid JWT.</summary>
[ApiController]
[Authorize]
public abstract class ApiControllerBase : ControllerBase
{
    protected CurrentUser Me => new(User.GetUserId(), User.GetDisplayName());
}
