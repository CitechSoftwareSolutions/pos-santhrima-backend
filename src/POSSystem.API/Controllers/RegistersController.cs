using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POSSystem.Application.Features.Registers;

namespace POSSystem.API.Controllers;

[Authorize]
public class RegistersController : ApiControllerBase
{
    private readonly IRegisterService _registerService;

    public RegistersController(IRegisterService registerService)
    {
        _registerService = registerService;
    }

    [HttpGet("current")]
    public async Task<ActionResult<RegisterSessionDto?>> GetCurrent([FromQuery] Guid terminalId)
    {
        var session = await _registerService.GetCurrentSessionAsync(terminalId);
        return Ok(session);
    }

    [HttpPost("open")]
    public async Task<ActionResult<RegisterSessionDto>> Open([FromBody] OpenRegisterRequest request)
    {
        var result = await _registerService.OpenSessionAsync(request, CurrentUserId);
        return Ok(result);
    }

    [HttpPost("close")]
    public async Task<ActionResult<CounterDailySummaryDto>> Close([FromBody] CloseRegisterApiRequest request)
    {
        var result = await _registerService.CloseSessionAsync(request.TerminalId, new CloseRegisterRequest(request.Notes), CurrentUserId);
        return Ok(result);
    }

    [HttpGet("today-summary")]
    public async Task<ActionResult<CounterDailySummaryDto>> GetTodaySummary([FromQuery] Guid terminalId)
    {
        var summary = await _registerService.GetTodayCounterSummaryAsync(terminalId);
        return Ok(summary);
    }
}

public record CloseRegisterApiRequest(Guid TerminalId, string? Notes);
