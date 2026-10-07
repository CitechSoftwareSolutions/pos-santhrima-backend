using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POSSystem.Application.Common.Models;
using POSSystem.Application.Features.Customers;
using POSSystem.Domain.Enums;

namespace POSSystem.API.Controllers;

[Authorize]
public class CustomersController : ApiControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerDto>>> GetAll([FromQuery] PaginationParams query)
    {
        return Ok(await _customerService.GetAllAsync(query));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> GetById(Guid id)
    {
        return Ok(await _customerService.GetByIdAsync(id));
    }

    [HttpPost]
    public async Task<ActionResult<CustomerDto>> Create(CreateCustomerRequest request)
    {
        var result = await _customerService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> Update(Guid id, UpdateCustomerRequest request)
    {
        return Ok(await _customerService.UpdateAsync(id, request));
    }

    [HttpPost("{id:guid}/claim-gift")]
    public async Task<ActionResult<CustomerDto>> ClaimGift(Guid id)
    {
        return Ok(await _customerService.ClaimMilestoneGiftAsync(id));
    }

    [HttpGet("{id:guid}/loyalty-history")]
    public async Task<ActionResult<List<LoyaltyTransactionDto>>> GetLoyaltyHistory(Guid id)
    {
        return Ok(await _customerService.GetLoyaltyHistoryAsync(id));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _customerService.DeleteAsync(id);
        return NoContent();
    }
}
