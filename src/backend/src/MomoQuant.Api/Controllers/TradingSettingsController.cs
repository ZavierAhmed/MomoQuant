using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Settings;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/settings/trading")]
public sealed class TradingSettingsController : ApiControllerBase
{
    private readonly ITradingSettingsService _service;

    public TradingSettingsController(ITradingSettingsService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Trading settings were not found.");
        }

        return OkResponse(result.Data);
    }

    [HttpPut]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Put([FromBody] UpdateTradingSettingsRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Unable to update trading settings.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data, "Trading settings updated.");
    }

    [HttpPost("reset-defaults")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> ResetDefaults(CancellationToken cancellationToken)
    {
        var result = await _service.ResetDefaultsAsync(cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Unable to reset trading settings.");
        }

        return OkResponse(result.Data, "Trading settings reset to defaults.");
    }
}
