using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POSSystem.Application.Common.Models;
using POSSystem.Application.Features.Cheques;
using POSSystem.Domain.Enums;

namespace POSSystem.API.Controllers;

[Authorize(Roles = Roles.Admin)]
public class ChequesController : ApiControllerBase
{
    private readonly IChequeService _chequeService;

    public ChequesController(IChequeService chequeService)
    {
        _chequeService = chequeService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ChequeDto>>> GetAll([FromQuery] ChequeQueryParams query, CancellationToken cancellationToken)
    {
        var result = await _chequeService.GetAllAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ChequeSummaryDto>> GetSummary(CancellationToken cancellationToken)
    {
        var summary = await _chequeService.GetSummaryAsync(cancellationToken);
        return Ok(summary);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChequeDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var cheque = await _chequeService.GetByIdAsync(id, cancellationToken);
        return Ok(cheque);
    }

    [HttpPost]
    public async Task<ActionResult<ChequeDto>> Create([FromBody] CreateChequeRequest request, CancellationToken cancellationToken)
    {
        var cheque = await _chequeService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = cheque.Id }, cheque);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ChequeDto>> Update(Guid id, [FromBody] UpdateChequeRequest request, CancellationToken cancellationToken)
    {
        var cheque = await _chequeService.UpdateAsync(id, request, cancellationToken);
        return Ok(cheque);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ChequeDto>> UpdateStatus(Guid id, [FromBody] UpdateChequeStatusRequest request, CancellationToken cancellationToken)
    {
        var cheque = await _chequeService.UpdateStatusAsync(id, request, cancellationToken);
        return Ok(cheque);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _chequeService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    // Recipients
    [HttpGet("recipients")]
    public async Task<ActionResult<List<ChequeNotificationRecipientDto>>> GetRecipients(CancellationToken cancellationToken)
    {
        var recipients = await _chequeService.GetRecipientsAsync(cancellationToken);
        return Ok(recipients);
    }

    [HttpPost("recipients")]
    public async Task<ActionResult<ChequeNotificationRecipientDto>> CreateRecipient([FromBody] CreateRecipientRequest request, CancellationToken cancellationToken)
    {
        var recipient = await _chequeService.CreateRecipientAsync(request, cancellationToken);
        return Ok(recipient);
    }

    [HttpPut("recipients/{id:guid}")]
    public async Task<ActionResult<ChequeNotificationRecipientDto>> UpdateRecipient(Guid id, [FromBody] UpdateRecipientRequest request, CancellationToken cancellationToken)
    {
        var recipient = await _chequeService.UpdateRecipientAsync(id, request, cancellationToken);
        return Ok(recipient);
    }

    [HttpDelete("recipients/{id:guid}")]
    public async Task<IActionResult> DeleteRecipient(Guid id, CancellationToken cancellationToken)
    {
        await _chequeService.DeleteRecipientAsync(id, cancellationToken);
        return NoContent();
    }

    // WhatsApp Settings & Triggers
    [HttpGet("settings")]
    public async Task<ActionResult<WhatsAppSettingDto>> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await _chequeService.GetWhatsAppSettingsAsync(cancellationToken);
        return Ok(settings);
    }

    [HttpPut("settings")]
    public async Task<ActionResult<WhatsAppSettingDto>> UpdateSettings([FromBody] UpdateWhatsAppSettingRequest request, CancellationToken cancellationToken)
    {
        var settings = await _chequeService.UpdateWhatsAppSettingsAsync(request, cancellationToken);
        return Ok(settings);
    }

    [HttpPost("send-due-reminders")]
    public async Task<ActionResult<WhatsAppSendResultDto>> SendDueReminders([FromQuery] bool forceAll = false, CancellationToken cancellationToken = default)
    {
        var result = await _chequeService.SendDueChequeRemindersAsync(forceAll, cancellationToken);
        return Ok(result);
    }

    [HttpPost("test-whatsapp")]
    public async Task<ActionResult<WhatsAppSendResultDto>> TestWhatsApp([FromBody] TestWhatsAppRequest request, CancellationToken cancellationToken)
    {
        var result = await _chequeService.SendTestMessageAsync(request, cancellationToken);
        return Ok(result);
    }
}
