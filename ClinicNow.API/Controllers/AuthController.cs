using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Services.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Registration, login, logout, and "who am I". Contains no business logic itself -
/// everything is delegated to <see cref="IUserService"/> (rulebook Part II §D).
///
/// The controller is <c>[Authorize]</c> by default; only <c>register</c>/<c>login</c>
/// are explicitly <c>[AllowAnonymous]</c> - every other action (including
/// <c>logout</c>) requires a valid bearer token, per rulebook §5.
/// </summary>
[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;

    public AuthController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiterPolicies.Auth)]
    public async Task<ActionResult<UserDto>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var created = await _userService.RegisterAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiterPolicies.Auth)]
    public async Task<ActionResult<LoginResponseDto>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _userService.LoginAsync(request, cancellationToken));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _userService.LogoutAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
    {
        return Ok(await _userService.GetCurrentUserAsync(cancellationToken));
    }
}
