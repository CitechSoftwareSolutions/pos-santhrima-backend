using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POSSystem.Domain.Enums;
using POSSystem.Application.Features.Terminals;

namespace POSSystem.API.Controllers;

[Authorize]
public class TerminalsController : ApiControllerBase
{
    private readonly ITerminalService _terminalService;

    public TerminalsController(ITerminalService terminalService)
    {
        _terminalService = terminalService;
    }

    [HttpGet]
    public async Task<ActionResult<List<TerminalDto>>> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var terminals = await _terminalService.GetAllAsync(includeInactive, cancellationToken);
        return Ok(terminals);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TerminalDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var terminal = await _terminalService.GetByIdAsync(id, cancellationToken);
        return Ok(terminal);
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<TerminalDto>> Create(
        [FromBody] CreateTerminalRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _terminalService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<TerminalDto>> Update(
        Guid id,
        [FromBody] UpdateTerminalRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _terminalService.UpdateAsync(id, request, cancellationToken);
        return Ok(updated);
    }

    [HttpPost("set-count")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<List<TerminalDto>>> SetCount(
        [FromBody] SetTerminalCountRequest request,
        CancellationToken cancellationToken)
    {
        var terminals = await _terminalService.SetCountAsync(request.TargetCount, cancellationToken);
        return Ok(terminals);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _terminalService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("authorize-switch")]
    public async Task<ActionResult<bool>> AuthorizeSwitch(
        [FromBody] AuthorizeSwitchRequest request,
        CancellationToken cancellationToken)
    {
        var authorized = await _terminalService.AuthorizeSwitchAsync(request, cancellationToken);
        return Ok(authorized);
    }
}
