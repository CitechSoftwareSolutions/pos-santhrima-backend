using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POSSystem.Application.Features.Promotions;
using POSSystem.Domain.Enums;

namespace POSSystem.API.Controllers;

[Authorize]
public class PromotionsController : ApiControllerBase
{
    private readonly IPromotionService _promotionService;

    public PromotionsController(IPromotionService promotionService)
    {
        _promotionService = promotionService;
    }

    [HttpGet]
    public async Task<ActionResult<List<PromotionDto>>> GetAll([FromQuery] Guid? productId)
    {
        return Ok(await _promotionService.GetAllAsync(productId));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PromotionDto>> GetById(Guid id)
    {
        return Ok(await _promotionService.GetByIdAsync(id));
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<PromotionDto>> Create(CreatePromotionRequest request)
    {
        var result = await _promotionService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<PromotionDto>> Update(Guid id, UpdatePromotionRequest request)
    {
        return Ok(await _promotionService.UpdateAsync(id, request));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _promotionService.DeleteAsync(id);
        return NoContent();
    }
}
