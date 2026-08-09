using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POSSystem.Application.Common.Models;
using POSSystem.Application.Features.Products;
using POSSystem.Domain.Enums;

namespace POSSystem.API.Controllers;

[Authorize]
public class ProductsController : ApiControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductDto>>> GetAll([FromQuery] ProductQueryParams query)
    {
        return Ok(await _productService.GetAllAsync(query));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> GetById(Guid id)
    {
        return Ok(await _productService.GetByIdAsync(id));
    }

    [HttpGet("lookup/{code}")]
    public async Task<ActionResult<ProductDto>> LookupByCode(string code)
    {
        var product = await _productService.GetBySkuOrBarcodeAsync(code);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<List<ProductDto>>> GetLowStock()
    {
        return Ok(await _productService.GetLowStockAsync());
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<ProductDto>> Create(CreateProductRequest request)
    {
        var result = await _productService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<ProductDto>> Update(Guid id, UpdateProductRequest request)
    {
        return Ok(await _productService.UpdateAsync(id, request));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _productService.DeleteAsync(id);
        return NoContent();
    }

    [HttpPost("{id:guid}/adjust-stock")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<ProductDto>> AdjustStock(Guid id, AdjustStockRequest request)
    {
        return Ok(await _productService.AdjustStockAsync(id, request, CurrentUserId));
    }
}
