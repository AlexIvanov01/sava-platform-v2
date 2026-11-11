using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuthenticationService.DataAccess;
using AuthenticationService.Dtos;
using AuthenticationService.Mappings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using UserModel = AuthenticationService.Models.User;

namespace AuthenticationService.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = UserModel.AdministratorRole)]
public class UsersController(IUserRepo repository, IPasswordHasher<UserModel> passwordHasher)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<UserReadDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<UserReadDto>>> GetUsers()
    {
        try
        {
            var users = await repository.GetAllUsersAsync();
            return Ok(users.Select(u => u.ToReadDto()));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpGet("{id}", Name = "GetUserById")]
    [ProducesResponseType<UserReadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserReadDto>> GetUserById(Guid id)
    {
        try
        {
            var user = await repository.GetUserAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            return Ok(user.ToReadDto());
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpPost]
    [ProducesResponseType<UserReadDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserReadDto>> CreateUser([FromBody] UserCreateDto dto)
    {
        try
        {
            if (await repository.GetUserByNameAsync(dto.UserName!) != null)
            {
                return Problem(
                    title: "User name already taken",
                    detail: $"A user named '{dto.UserName}' already exists.",
                    statusCode: StatusCodes.Status409Conflict
                );
            }

            var user = dto.ToEntity(string.Empty);
            user.Id = Guid.NewGuid();
            user.PasswordHash = passwordHasher.HashPassword(user, dto.Password!);
            await repository.CreateUserAsync(user);

            Log.Information(
                "--> Created user {UserName} with role {Role}.",
                user.UserName,
                user.Role
            );
            return CreatedAtRoute("GetUserById", new { id = user.Id }, user.ToReadDto());
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType<UserReadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserReadDto>> UpdateUser(Guid id, [FromBody] UserUpdateDto dto)
    {
        try
        {
            var existing = await repository.GetUserAsync(id);
            if (existing == null)
            {
                return NotFound();
            }

            if (
                dto.Role != null
                && dto.Role != UserModel.AdministratorRole
                && existing.UserName == User.Identity!.Name
            )
            {
                return Problem(
                    title: "Cannot demote yourself",
                    detail: "Ask another administrator to change your role.",
                    statusCode: StatusCodes.Status409Conflict
                );
            }

            var passwordHash =
                dto.Password == null ? null : passwordHasher.HashPassword(existing, dto.Password);
            var updated = await repository.UpdateUserAsync(dto.ToEntity(id, passwordHash));
            if (updated == null)
            {
                return NotFound();
            }

            Log.Information("--> Updated user {UserName}.", updated.UserName);
            return Ok(updated.ToReadDto());
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
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        try
        {
            var existing = await repository.GetUserAsync(id);
            if (existing == null)
            {
                return NotFound();
            }

            if (existing.UserName == User.Identity!.Name)
            {
                return Problem(
                    title: "Cannot delete yourself",
                    detail: "Ask another administrator to delete your account.",
                    statusCode: StatusCodes.Status409Conflict
                );
            }

            await repository.DeleteUserAsync(id);

            Log.Information("--> Deleted user {UserName}.", existing.UserName);
            return NoContent();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }
}
