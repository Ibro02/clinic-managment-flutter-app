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

    /// <summary>
    /// Edits the caller's own profile (review item C7). There is no
    /// <c>{id}</c> in the route on purpose: the user being edited is whoever
    /// the bearer token says, so this endpoint cannot be pointed at somebody
    /// else's account (rulebook Part II §F).
    /// </summary>
    [HttpPut("me")]
    public async Task<ActionResult<UserDto>> UpdateMe(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _userService.UpdateCurrentUserAsync(request, cancellationToken));
    }

    [HttpPost("change-password")]
    [EnableRateLimiting(RateLimiterPolicies.Auth)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await _userService.ChangeCurrentUserPasswordAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Starts a forgotten-password reset. Anonymous by necessity - the caller
    /// cannot sign in - and rate-limited, since it both sends mail and could
    /// otherwise be used to probe which addresses have accounts. Always answers
    /// 204, whether or not the address is registered.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiterPolicies.Auth)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await _userService.RequestPasswordResetAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiterPolicies.Auth)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await _userService.ResetPasswordAsync(request, cancellationToken);
        return NoContent();
    }
}
