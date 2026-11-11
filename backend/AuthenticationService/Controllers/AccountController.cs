using System;
using System.Threading.Tasks;
using AuthenticationService.DataAccess;
using AuthenticationService.Dtos;
using AuthenticationService.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using UserModel = AuthenticationService.Models.User;

namespace AuthenticationService.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AccountController(
    IUserRepo users,
    IPasswordHasher<UserModel> passwordHasher,
    ITokenService tokenService
) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponseDto>> Login([FromBody] LoginRequestDto request)
    {
        try
        {
            var user = await users.GetUserByNameAsync(request.UserName!);
            if (user == null || !PasswordMatches(user, request.Password!))
            {
                Log.Warning("--> Failed login for user {UserName}.", request.UserName);
                return Unauthorized();
            }

            Log.Information("--> User {UserName} logged in.", user.UserName);
            return Ok(await tokenService.IssueAsync(user));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponseDto>> Refresh([FromBody] RefreshRequestDto request)
    {
        try
        {
            var response = await tokenService.RefreshAsync(request.RefreshToken!);
            if (response == null)
            {
                Log.Warning("--> Refresh rejected: token unknown, expired or revoked.");
                return Unauthorized();
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequestDto request)
    {
        try
        {
            // Always 204: whether the token was active or not, it is unusable afterwards.
            await tokenService.RevokeAsync(request.RefreshToken!);
            return NoContent();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpPut("password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto request)
    {
        try
        {
            var user = await users.GetUserByNameAsync(User.Identity!.Name!);
            if (user == null)
            {
                return Unauthorized();
            }

            if (!PasswordMatches(user, request.CurrentPassword!))
            {
                return Problem(
                    title: "Current password is incorrect",
                    statusCode: StatusCodes.Status400BadRequest
                );
            }

            user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword!);
            await users.UpdateUserAsync(user);

            Log.Information("--> User {UserName} changed their password.", user.UserName);
            return NoContent();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    private bool PasswordMatches(UserModel user, string password) =>
        passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password)
        != PasswordVerificationResult.Failed;
}
