using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.Mappings;
using InventoryService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace InventoryService.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ProductsController : ControllerBase
{
    private readonly IProductRepo _repository;
    private readonly ICategoryRepo _categories;
    private readonly TimeProvider _timeProvider;

    public ProductsController(
        IProductRepo repository,
        ICategoryRepo categories,
        TimeProvider timeProvider
    )
    {
        _repository = repository;
        _categories = categories;
        _timeProvider = timeProvider;
    }

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    // Filters combine with AND; Name and SKU match substrings, Barcode matches exactly.
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductPageReadDto>> GetProducts(
        Guid? cursor,
        [Range(1, 100)] int pageSize = 10,
        string? name = null,
        string? sku = null,
        string? barcode = null,
        Guid? categoryId = null,
        bool? isPublished = null
    )
    {
        try
        {
            var filter = new ProductFilter(name, sku, barcode, categoryId, isPublished);
            Log.Information(
                "--> Getting {PageSize} amount of products after cursor {Cursor} with {@Filter}",
                pageSize,
                cursor,
                filter
            );

            var productItems = await _repository.GetAllProductsAsync(filter, cursor, pageSize);

            if (!productItems.Any())
            {
                Log.Warning(
                    "--> No products found in database after cursor {Cursor} or cursor not found.",
                    cursor
                );
                return NotFound();
            }

            Log.Information(
                "--> Fetched {PageSize} amount of products from database after cursor {Cursor}.",
                pageSize,
                cursor
            );

            var dtos = productItems.Select(p => p.ToReadDto(Today)).ToList();

            var pageCount = Math.Ceiling(
                await _repository.GetAllProductsCountAsync(filter) / (float)pageSize
            );

            ProductPageReadDto productPageReadDto = new()
            {
                ProductReadDtos = dtos,
                Cursor = dtos[^1].Id,
                Pages = (int)pageCount,
            };

            return Ok(productPageReadDto);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpGet("{id}", Name = "GetProductById")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductReadDto>> GetProductById(Guid id)
    {
        try
        {
            Log.Information("--> Getting a product with id {Id}........", id);

            var productItem = await _repository.GetProductAsync(id);

            if (productItem == null)
            {
                Log.Warning("Product with id {Id} not found.", id);
                return NotFound();
            }

            Log.Information("--> Fetched a product with id {Id}.", id);

            return Ok(productItem.ToReadDto(Today));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductReadDto>> CreateProduct(ProductCreateDto productCreateDto)
    {
        try
        {
            Log.Information("--> Creating a product.............");

            var invalid = await ValidateReferencesAsync(
                productCreateDto.SKU,
                productCreateDto.CategoryId,
                null
            );
            if (invalid != null)
            {
                return invalid;
            }

            var productModel = productCreateDto.ToEntity();
            await _repository.CreateProductAsync(productModel);

            Log.Information("Product created: {@ProductModel}", productModel);

            var productReadDto = productModel.ToReadDto(Today);

            return CreatedAtRoute(
                nameof(GetProductById),
                new { Id = productReadDto.Id },
                productReadDto
            );
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductReadDto>> UpdateProduct(
        Guid id,
        ProductUpdateDto productUpdateDto
    )
    {
        Product savedProduct;

        try
        {
            Log.Information("--> Updating a product with id {Id}....................", id);

            var invalid = await ValidateReferencesAsync(
                productUpdateDto.SKU,
                productUpdateDto.CategoryId,
                id
            );
            if (invalid != null)
            {
                return invalid;
            }

            var updated = await _repository.UpdateProductAsync(productUpdateDto.ToEntity(id));

            if (updated == null)
            {
                Log.Warning("Product with id {Id} not found for updating.", id);
                return NotFound();
            }

            savedProduct = updated;

            Log.Information("--> Product with id {Id} updated", id);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }

        return Ok(savedProduct.ToReadDto(Today));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteProduct(Guid id)
    {
        try
        {
            Log.Information("--> Deleting a product...........");

            var (status, _) = await _repository.DeleteProductAsync(id);

            if (status == DeleteStatus.NotFound)
            {
                Log.Warning("Product with id {Id} not found for deleting.", id);
                return NotFound();
            }

            if (status == DeleteStatus.HasOrders)
            {
                Log.Warning("--> Product with id {Id} has orders; not deleted.", id);
                return Problem(
                    title: "Product has orders",
                    detail: $"Orders hold stock from product {id}, so it can't be deleted.",
                    statusCode: StatusCodes.Status409Conflict
                );
            }

            Log.Information("--> Product with id {Id} deleted", id);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }

        return NoContent();
    }

    // SKUs are unique, and a referenced category must exist.
    private async Task<ActionResult?> ValidateReferencesAsync(
        string? sku,
        Guid? categoryId,
        Guid? productId
    )
    {
        if (sku != null && await _repository.SkuExistsAsync(sku, productId))
        {
            Log.Warning("--> SKU {SKU} is already used by another product.", sku);
            return Problem(
                title: "SKU already exists",
                detail: $"Another product already uses SKU '{sku}'.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        if (categoryId != null && !await _categories.CategoryExistsAsync(categoryId.Value))
        {
            Log.Warning("--> Category {CategoryId} not found.", categoryId);
            return Problem(
                title: "Unknown category",
                detail: $"Category {categoryId} does not exist.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        return null;
    }
}
