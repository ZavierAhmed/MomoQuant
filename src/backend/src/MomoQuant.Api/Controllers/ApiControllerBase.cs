using Microsoft.AspNetCore.Mvc;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult OkResponse<T>(T data, string message = "Request completed successfully.")
    {
        return Ok(ApiResponse<T>.Ok(data, message));
    }

    protected IActionResult FailResponse(
        string message,
        int statusCode = StatusCodes.Status400BadRequest,
        IReadOnlyList<ApiError>? errors = null)
    {
        return StatusCode(statusCode, ApiResponse<object>.Fail(message, errors));
    }

    protected IActionResult ValidationFailResponse()
    {
        var errors = ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .SelectMany(entry => entry.Value!.Errors.Select(error => new ApiError
            {
                Field = entry.Key,
                Message = string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Invalid value." : error.ErrorMessage
            }))
            .ToList();

        return FailResponse("Validation failed.", StatusCodes.Status400BadRequest, errors);
    }

    protected string? GetClientIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();

    protected string? GetUserAgent() => Request.Headers.UserAgent.ToString();
}
