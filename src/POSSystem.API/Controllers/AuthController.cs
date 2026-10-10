using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POSSystem.Application.Features.Auth;
using POSSystem.Domain.Enums;

namespace POSSystem.API.Controllers;

public class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);
        return Ok(result);
    }

    [HttpPost("register")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);
        return Ok(result);
    }

    [HttpGet("users")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<List<UserDto>>> GetUsers()
    {
        var result = await _authService.GetAllUsersAsync();
        return Ok(result);
    }

    [HttpPatch("users/{id:guid}/status")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SetUserStatus(Guid id, [FromBody] bool isActive)
    {
        await _authService.SetUserActiveStatusAsync(id, isActive);
        return NoContent();
    }

    [HttpPut("users/{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<UserDto>> UpdateUser(Guid id, UpdateUserRequest request)
    {
        var result = await _authService.UpdateUserAsync(id, request);
        return Ok(result);
    }

    [HttpDelete("users/{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        await _authService.DeleteUserAsync(id, CurrentUserId);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        await _authService.ChangePasswordAsync(CurrentUserId, request);
        return NoContent();
    }
}
