using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace AuthenticationService.Auditing;

// Unlike the other services, this one validates bearer tokens itself, so the name
// comes from the authenticated principal.
public class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public const string SystemName = "system";

    public string Name
    {
        get
        {
            var name = httpContextAccessor.HttpContext?.User.Identity?.Name;
            return string.IsNullOrWhiteSpace(name) ? SystemName : name;
        }
    }
}
