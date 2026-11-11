using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.Mappings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace InventoryService.Controllers;

[Route("api/[controller]")]
[ApiController]
public class CategoriesController(ICategoryRepo repository) : ControllerBase
{
    // Categories are few, so the list isn't paged.
    [HttpGet]
    [ProducesResponseType<IEnumerable<CategoryReadDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CategoryReadDto>>> GetCategories()
    {
        try
        {
            var categories = await repository.GetAllCategoriesAsync();
            return Ok(categories.Select(c => c.Category.ToReadDto(c.ProductCount)));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpGet("{id}", Name = "GetCategoryById")]
    [ProducesResponseType<CategoryReadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryReadDto>> GetCategoryById(Guid id)
    {
        try
        {
            var row = await repository.GetCategoryAsync(id);
            if (row == null)
            {
                Log.Warning("--> Category with id {Id} not found.", id);
                return NotFound();
            }

            return Ok(row.Value.Category.ToReadDto(row.Value.ProductCount));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpPost]
    [ProducesResponseType<CategoryReadDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryReadDto>> CreateCategory(CategoryCreateDto dto)
    {
        try
        {
            if (await repository.NameExistsAsync(dto.Name))
            {
                return NameTaken(dto.Name);
            }

            var category = dto.ToEntity();
            await repository.CreateCategoryAsync(category);

            Log.Information("--> Category created: {@Category}", category);
            return CreatedAtRoute(
                "GetCategoryById",
                new { id = category.Id },
                category.ToReadDto(0)
            );
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType<CategoryReadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryReadDto>> UpdateCategory(Guid id, CategoryUpdateDto dto)
    {
        try
        {
            if (dto.Name != null && await repository.NameExistsAsync(dto.Name, id))
            {
                return NameTaken(dto.Name);
            }

            var updated = await repository.UpdateCategoryAsync(dto.ToEntity(id));
            if (updated == null)
            {
                Log.Warning("--> Category with id {Id} not found for updating.", id);
                return NotFound();
            }

            var row = await repository.GetCategoryAsync(id);
            Log.Information("--> Category with id {Id} updated.", id);
            return Ok(updated.ToReadDto(row?.ProductCount ?? 0));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCategory(Guid id)
    {
        try
        {
            var status = await repository.DeleteCategoryAsync(id);

            switch (status)
            {
                case DeleteCategoryStatus.NotFound:
                    Log.Warning("--> Category with id {Id} not found for deleting.", id);
                    return NotFound();
                case DeleteCategoryStatus.HasProducts:
                    Log.Warning("--> Category with id {Id} still has products.", id);
                    return Problem(
                        title: "Category has products",
                        detail: $"Category {id} still has products. Move or delete them first.",
                        statusCode: StatusCodes.Status409Conflict
                    );
                default:
                    Log.Information("--> Category with id {Id} deleted.", id);
                    return NoContent();
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    private ObjectResult NameTaken(string name) =>
        Problem(
            title: "Category name already exists",
            detail: $"A category named '{name}' already exists.",
            statusCode: StatusCodes.Status409Conflict
        );
}
