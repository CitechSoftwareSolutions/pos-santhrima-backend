using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POSSystem.Application.Features.Reports;
using POSSystem.Domain.Enums;

namespace POSSystem.API.Controllers;

[Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
public class ReportsController : ApiControllerBase
{
    private readonly IReportService _reportService;

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet("sales-summary")]
    public async Task<ActionResult<SalesSummaryDto>> GetSalesSummary([FromQuery] DateTime dateFrom, [FromQuery] DateTime dateTo)
    {
        return Ok(await _reportService.GetSalesSummaryAsync(dateFrom, dateTo));
    }

    [HttpGet("daily-sales")]
    public async Task<ActionResult<List<DailySalesDto>>> GetDailySales([FromQuery] DateTime dateFrom, [FromQuery] DateTime dateTo)
    {
        return Ok(await _reportService.GetDailySalesAsync(dateFrom, dateTo));
    }

    [HttpGet("top-products")]
    public async Task<ActionResult<List<TopProductDto>>> GetTopProducts([FromQuery] DateTime dateFrom, [FromQuery] DateTime dateTo, [FromQuery] int take = 10)
    {
        return Ok(await _reportService.GetTopProductsAsync(dateFrom, dateTo, take));
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<List<LowStockReportItemDto>>> GetLowStock()
    {
        return Ok(await _reportService.GetLowStockReportAsync());
    }

    [HttpGet("inventory-valuation")]
    public async Task<ActionResult<InventoryValuationDto>> GetInventoryValuation()
    {
        return Ok(await _reportService.GetInventoryValuationAsync());
    }

    [HttpGet("payment-methods")]
    public async Task<ActionResult<List<PaymentMethodBreakdownDto>>> GetPaymentMethodBreakdown([FromQuery] DateTime dateFrom, [FromQuery] DateTime dateTo)
    {
        return Ok(await _reportService.GetPaymentMethodBreakdownAsync(dateFrom, dateTo));
    }
}
