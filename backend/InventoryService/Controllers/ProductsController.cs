using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using InventoryService.AsyncDataServices;
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
    private readonly IMessageBusClient _messageBusClient;

    public ProductsController(IProductRepo repository, IMessageBusClient messageBusClient)
    {
        _repository = repository;
        _messageBusClient = messageBusClient;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductPageReadDto>> GetProducts(
        Guid? cursor,
        [Range(1, 100)] int pageSize = 10
    )
    {
        try
        {
            Log.Information(
                "--> Getting {PageSize} amount of products after cursor {Cursor}",
                pageSize,
                cursor
            );

            var productItems = await _repository.GetAllProductsAsync(cursor, pageSize);

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

            var dtos = productItems.Select(p => p.ToReadDto()).ToList();

            var pageCount = Math.Ceiling(
                await _repository.GetAllProductsCountAsync() / (float)pageSize
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
            return StatusCode(500, "An internal server error occured.");
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

            return Ok(productItem.ToReadDto());
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occured.");
        }
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ProductReadDto>> CreateProduct(ProductCreateDto productCreateDto)
    {
        try
        {
            Log.Information("--> Creating a product.............");
            var productModel = productCreateDto.ToEntity();
            await _repository.CreateProductAsync(productModel);

            Log.Information("Product created: {@ProductModel}", productModel);

            var productReadDto = productModel.ToReadDto();

            return CreatedAtRoute(
                nameof(GetProductById),
                new { Id = productReadDto.Id },
                productReadDto
            );
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occured.");
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductReadDto>> UpdateProduct(
        Guid id,
        ProductUpdateDto productUpdateDto
    )
    {
        Product savedProduct;

        try
        {
            Log.Information("--> Updating a product with id {Id}....................", id);

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
            return StatusCode(500, "An internal server error occured.");
        }

        if (productUpdateDto.Name != null)
        {
            //Send Async Message
            try
            {
                ItemPublishedDto itemPublishedDto = new()
                {
                    ExternalProductId = id,
                    Name = savedProduct.Name,
                    Event = "ProductName_Updated",
                };
                await _messageBusClient.PublishNewItemAsync(itemPublishedDto);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error with publishing item message: {Message}", ex.Message);
            }
        }

        return Ok(savedProduct.ToReadDto());
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProduct(Guid id)
    {
        try
        {
            Log.Information("--> Deleting a product...........");

            var nullCheck = await _repository.DeleteProductAsync(id);

            if (nullCheck == null)
            {
                Log.Warning("Product with id {Id} not found for deleting.", id);
                return NotFound();
            }

            Log.Information("--> Product with id {Id} deleted", id);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occured.");
        }

        //Send Async Message
        try
        {
            ItemPublishedDto itemPublishedDto = new()
            {
                ExternalProductId = id,
                Event = "Product_Deleted",
            };

            await _messageBusClient.PublishNewItemAsync(itemPublishedDto);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error with publishing item message: {Message}", ex.Message);
        }

        return NoContent();
    }
}
