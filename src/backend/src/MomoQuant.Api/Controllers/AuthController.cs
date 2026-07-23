using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application.Auth;
using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _authService.LoginAsync(
            request,
            GetClientIpAddress(),
            GetUserAgent(),
            cancellationToken);

        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(
                result.ErrorMessage ?? "Invalid email or password.",
                StatusCodes.Status401Unauthorized);
        }

        return OkResponse(result.Data, "Login successful.");
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var result = await _authService.GetCurrentUserAsync(cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "User is not authenticated.", StatusCodes.Status401Unauthorized);
        }

        return OkResponse(result.Data);
    }

    [Authorize]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        return OkResponse(new { loggedOut = true }, "Logout successful.");
    }
}
