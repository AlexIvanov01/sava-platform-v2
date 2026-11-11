using InventoryService.Auditing;
using Microsoft.AspNetCore.Http;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

public class HttpContextCurrentUserTests
{
    [Fact]
    public void Name_NoHttpContext_IsSystem()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);

        new HttpContextCurrentUser(accessor).Name.ShouldBe(HttpContextCurrentUser.SystemName);
    }

    [Fact]
    public void Name_BearerTokenWithNameClaim_IsTheClaim()
    {
        var user = TestData.Faker.Internet.UserName();

        new HttpContextCurrentUser(Accessor(TestTokens.Bearer(user))).Name.ShouldBe(user);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic abc")]
    [InlineData("Bearer not-a-jwt")]
    public void Name_MissingOrUnreadableToken_IsSystem(string? authorization)
    {
        new HttpContextCurrentUser(Accessor(authorization)).Name.ShouldBe(
            HttpContextCurrentUser.SystemName
        );
    }

    private static IHttpContextAccessor Accessor(string? authorization)
    {
        var context = new DefaultHttpContext();
        if (authorization != null)
        {
            context.Request.Headers.Authorization = authorization;
        }
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        return accessor;
    }
}
