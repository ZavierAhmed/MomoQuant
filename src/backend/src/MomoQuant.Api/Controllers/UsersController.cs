using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Users;
using MomoQuant.Application.Users.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("api/v1/users")]
public sealed class UsersController : ApiControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers([FromQuery] PagedRequest request, CancellationToken cancellationToken)
    {
        var result = await _userService.GetUsersAsync(request, cancellationToken);
        return OkResponse(result.Data!);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetUser(long id, CancellationToken cancellationToken)
    {
        var result = await _userService.GetUserByIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "User was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _userService.CreateUserAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to create user.", StatusCodes.Status400BadRequest, errors);
        }

        return StatusCode(StatusCodes.Status201Created, ApiResponse<UserDto>.Ok(result.Data, "User created successfully."));
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> UpdateUser(long id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _userService.UpdateUserAsync(id, request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "User was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            var errors = result.ErrorField is null
                ? null
                : new List<ApiError> { new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." } };

            return FailResponse(result.ErrorMessage ?? "Unable to update user.", statusCode, errors);
        }

        return OkResponse(result.Data, "User updated successfully.");
    }

    [HttpPost("{id:long}/disable")]
    public async Task<IActionResult> DisableUser(long id, CancellationToken cancellationToken)
    {
        var result = await _userService.DisableUserAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorMessage == "User was not found."
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            return FailResponse(result.ErrorMessage ?? "Unable to disable user.", statusCode);
        }

        return OkResponse(result.Data, "User disabled successfully.");
    }
}
