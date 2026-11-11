using Microsoft.Extensions.Configuration;
using Ocelot.Configuration.File;

namespace OcelotApiGateway.Tests;

// Guards the authorization policy encoded in ocelot.json: reads need a valid token, writes need
// Role=Administrator, and only the login/refresh/logout routes are anonymous. It also catches the
// trap this file fell into before: a JSON array under RouteClaimsRequirement binds to nothing,
// which silently drops the role check.
public class RoutePolicyTests
{
    private static readonly string[] AnonymousRoutes =
    [
        "/api/Account",
        "/api/Account/refresh",
        "/api/Account/logout",
    ];

    private static readonly List<FileRoute> Routes = new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "ocelot.json"))
        .Build()
        .Get<FileConfiguration>()!
        .Routes;

    [Fact]
    public void OnlyLoginRefreshAndLogoutAreAnonymous()
    {
        var anonymous = Routes
            .Where(r => r.AuthenticationOptions?.AuthenticationProviderKeys is not { Length: > 0 })
            .Select(r => r.UpstreamPathTemplate)
            .ToList();

        anonymous.ShouldBe(AnonymousRoutes, ignoreOrder: true);
        Routes
            .Where(r => AnonymousRoutes.Contains(r.UpstreamPathTemplate))
            .SelectMany(r => r.UpstreamHttpMethod)
            .ShouldAllBe(m => m == "Post");
    }

    [Fact]
    public void AuthenticatedRoutesUseTheBearerScheme()
    {
        foreach (var route in Routes.Where(r => !AnonymousRoutes.Contains(r.UpstreamPathTemplate)))
        {
            route.AuthenticationOptions.AuthenticationProviderKeys.ShouldBe(
                ["Bearer"],
                $"{route.UpstreamPathTemplate} must require a bearer token"
            );
        }
    }

    [Fact]
    public void RoutesThatChangeDataRequireAdministrator()
    {
        foreach (var route in Routes.Where(r => !AnonymousRoutes.Contains(r.UpstreamPathTemplate)))
        {
            var writes = route.UpstreamHttpMethod.Any(m => m != "Get");
            var reads = route.UpstreamHttpMethod.Contains("Get");
            var description =
                $"{route.UpstreamPathTemplate} [{string.Join(",", route.UpstreamHttpMethod)}]";

            // Self-service password change and the admin-only user API are the two exceptions.
            if (route.UpstreamPathTemplate == "/api/Account/password")
            {
                route.RouteClaimsRequirement.ShouldBeEmpty(description);
                continue;
            }
            if (route.UpstreamPathTemplate.StartsWith("/api/Users"))
            {
                route.RouteClaimsRequirement.ShouldBe(
                    new Dictionary<string, string> { ["Role"] = "Administrator" },
                    description
                );
                continue;
            }

            (reads && writes).ShouldBeFalse($"{description} mixes read and write methods");
            if (writes)
            {
                route.RouteClaimsRequirement.ShouldBe(
                    new Dictionary<string, string> { ["Role"] = "Administrator" },
                    description
                );
            }
            else
            {
                route.RouteClaimsRequirement.ShouldBeEmpty(
                    $"{description} is a read and must allow every authenticated role"
                );
            }
        }
    }

    [Fact]
    public void RouteClaimsRequirementsBindToSingleValues()
    {
        // An array value would have been dropped by configuration binding; make the intent explicit.
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ocelot.json"));

        raw.ShouldNotContain("\"Role\": [");
    }

    [Fact]
    public void DownstreamPathsMirrorUpstreamPaths()
    {
        foreach (var route in Routes)
        {
            route.DownstreamPathTemplate.ShouldBe(route.UpstreamPathTemplate);
            route.DownstreamHostAndPorts.ShouldHaveSingleItem().Port.ShouldBe(80);
        }
    }
}
