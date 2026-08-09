using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POSSystem.Application.Common.Models;
using POSSystem.Application.Features.Sales;
using POSSystem.Domain.Enums;

namespace POSSystem.API.Controllers;

[Authorize]
public class SalesController : ApiControllerBase
{
    private readonly ISaleService _saleService;

    public SalesController(ISaleService saleService)
    {
        _saleService = saleService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<SaleListItemDto>>> GetAll([FromQuery] SaleQueryParams query)
    {
        return Ok(await _saleService.GetAllAsync(query));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleDto>> GetById(Guid id)
    {
        return Ok(await _saleService.GetByIdAsync(id));
    }

    [HttpPost]
    public async Task<ActionResult<SaleDto>> Create(CreateSaleRequest request)
    {
        var result = await _saleService.CreateAsync(request, CurrentUserId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:guid}/void")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<SaleDto>> Void(Guid id, [FromBody] string reason)
    {
        return Ok(await _saleService.VoidAsync(id, reason));
    }

    [HttpPost("{id:guid}/refund")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<SaleDto>> Refund(Guid id, [FromBody] RefundRequest request)
    {
        return Ok(await _saleService.RefundAsync(id, request.SaleItemIds, request.Reason));
    }
}

public record RefundRequest(List<Guid>? SaleItemIds, string Reason);
