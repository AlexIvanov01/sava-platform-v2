using AuthenticationService.Controllers;
using JwtAuthenticationManager;
using JwtAuthenticationManager.Models;
using Microsoft.AspNetCore.Mvc;

namespace AuthenticationService.Tests;

public class AccountControllerTests
{
    private readonly AccountController _controller = new(new JwtTokenHandler());

    [Fact]
    public void Authenticate_ValidCredentials_ReturnsResponse()
    {
        var result = _controller.Authenticate(
            new AuthenticationRequest { UserName = "user", Password = "user123" }
        );

        result.Value.ShouldNotBeNull().UserName.ShouldBe("user");
    }

    [Fact]
    public void Authenticate_InvalidCredentials_ReturnsUnauthorized()
    {
        var result = _controller.Authenticate(
            new AuthenticationRequest { UserName = "user", Password = "nope" }
        );

        result.Result.ShouldBeOfType<UnauthorizedResult>();
    }
}
