using System.Security.Claims;
using Stock.Application.Common;

namespace Stock.Api.Auth;

public static class CurrentUserFactory
{
    /// <summary>Builds the application-layer CurrentUser from JWT claims
    /// (sub, tenant_id, role_slugs) issued by auth-service.</summary>
    public static CurrentUser FromClaims(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? string.Empty;

        Guid? tenantId = null;
        var rawTenant = principal.FindFirstValue("tenant_id");
        if (Guid.TryParse(rawTenant, out var parsed))
        {
            tenantId = parsed;
        }

        var roles = principal.FindAll("role_slugs")
            .Select(c => c.Value.ToLowerInvariant())
            .Distinct()
            .ToList();

        return new CurrentUser(userId, tenantId, roles);
    }
}
