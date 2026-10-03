using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskFlow.Api.Auth;
using TaskFlow.Api.Dtos;
using TaskFlow.Api.Entities;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Controllers;

/// <summary>
/// Register / login return a JWT. "Logout" is done on the client by forgetting the token
/// (JWTs are stateless, so the server has nothing to delete).
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;
    private readonly TokenService _tokens;

    public AuthController(UserManager<AppUser> users, SignInManager<AppUser> signIn, TokenService tokens)
    {
        _users = users;
        _signIn = signIn;
        _tokens = tokens;
    }

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var user = new AppUser
        {
            Email = request.Email.Trim(),
            UserName = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim()
        };

        var result = await _users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(error.Code, error.Description);
            return ValidationProblem(ModelState);
        }

        return Ok(ToResponse(user));
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await _users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return Unauthorized(new { message = "Invalid email or password." });

        // lockoutOnFailure: after 5 wrong passwords the account is locked for a few minutes.
        var result = await _signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return Unauthorized(new { message = "Too many failed attempts. Try again in a few minutes." });
        if (!result.Succeeded)
            return Unauthorized(new { message = "Invalid email or password." });

        return Ok(ToResponse(user));
    }

    /// <summary>
    /// "Try the demo": logs straight into the shared demo account (no password typing needed).
    /// Also resets the demo board if it is more than a day old.
    /// </summary>
    [HttpPost("demo")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthResponse>> Demo([FromServices] DemoSeeder seeder)
    {
        var demo = await seeder.EnsureDemoAsync();
        return Ok(ToResponse(demo));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var user = await _users.FindByIdAsync(User.GetUserId());
        if (user is null) return Unauthorized();
        return new UserDto(user.Id, user.Email!, user.DisplayName);
    }

    private AuthResponse ToResponse(AppUser user) =>
        new(_tokens.CreateToken(user), new UserDto(user.Id, user.Email!, user.DisplayName));
}
