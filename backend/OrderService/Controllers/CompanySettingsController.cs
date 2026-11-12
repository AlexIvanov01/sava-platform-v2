using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Mappings;
using Serilog;

namespace OrderService.Controllers;

// The company's own details (the supplier on invoices). A single resource, so no id.
[Route("api/[controller]")]
[ApiController]
public class CompanySettingsController(ICompanySettingsRepo repository) : ControllerBase
{
    // 404 until an administrator has saved the settings.
    [HttpGet]
    [ProducesResponseType<CompanySettingsReadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompanySettingsReadDto>> GetSettings()
    {
        try
        {
            var settings = await repository.GetAsync();
            if (settings == null)
            {
                Log.Warning("--> Company settings have not been saved yet.");
                return NotFound();
            }
            return Ok(settings.ToReadDto());
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    // Partial update; creates the settings the first time (Name and UIC are required then).
    [HttpPut]
    [ProducesResponseType<CompanySettingsReadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CompanySettingsReadDto>> SaveSettings(
        CompanySettingsUpdateDto dto
    )
    {
        try
        {
            if (
                (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.UIC))
                && await repository.GetAsync() == null
            )
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Company settings incomplete",
                    detail: "Name and UIC are required when saving the company settings for the first time."
                );
            }

            var saved = await repository.SaveAsync(dto.ToEntity());
            Log.Information("--> Company settings saved.");
            return Ok(saved.ToReadDto());
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }
}
