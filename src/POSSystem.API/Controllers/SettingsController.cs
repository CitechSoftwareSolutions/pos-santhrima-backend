using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POSSystem.Domain.Enums;
using POSSystem.Application.Features.Settings;

namespace POSSystem.API.Controllers;

[Authorize]
public class SettingsController : ApiControllerBase
{
    private readonly ISettingsService _settingsService;

    public SettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [HttpGet]
    public async Task<ActionResult<StoreSettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetSettingsAsync(cancellationToken);
        return Ok(settings);
    }

    [HttpPut]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<StoreSettingsDto>> UpdateSettings(
        [FromBody] UpdateStoreSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _settingsService.UpdateSettingsAsync(request, cancellationToken);
        return Ok(updated);
    }
}
