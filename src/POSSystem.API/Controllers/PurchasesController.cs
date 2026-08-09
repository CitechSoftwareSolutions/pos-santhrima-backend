using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POSSystem.Application.Common.Models;
using POSSystem.Application.Features.Purchases;
using POSSystem.Domain.Enums;

namespace POSSystem.API.Controllers;

[Authorize]
public class PurchasesController : ApiControllerBase
{
    private readonly IPurchaseService _purchaseService;

    public PurchasesController(IPurchaseService purchaseService)
    {
        _purchaseService = purchaseService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<PurchaseDto>>> GetAll([FromQuery] PurchaseQueryParams query)
    {
        return Ok(await _purchaseService.GetAllAsync(query));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PurchaseDto>> GetById(Guid id)
    {
        return Ok(await _purchaseService.GetByIdAsync(id));
    }

    [HttpPost]
    public async Task<ActionResult<PurchaseDto>> Create(CreatePurchaseRequest request)
    {
        var result = await _purchaseService.CreateAsync(request, CurrentUserId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<PurchaseDto>> Approve(Guid id)
    {
        return Ok(await _purchaseService.ApproveAsync(id, CurrentUserId));
    }

    [HttpPost("{id:guid}/add-to-stock")]
    public async Task<ActionResult<PurchaseDto>> AddToStock(Guid id)
    {
        return Ok(await _purchaseService.AddToStockAsync(id, CurrentUserId));
    }

    [HttpPost("{id:guid}/mark-paid")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<PurchaseDto>> MarkAsPaid(Guid id)
    {
        return Ok(await _purchaseService.MarkAsPaidAsync(id, CurrentUserId));
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<PurchaseDto>> Cancel(Guid id)
    {
        return Ok(await _purchaseService.CancelAsync(id));
    }
}
