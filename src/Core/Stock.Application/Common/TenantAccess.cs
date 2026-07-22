using Stock.Domain.Errors;

namespace Stock.Application.Common;

public static class TenantAccess
{
    /// <summary>Resolves the tenant the caller may act on: managers are locked
    /// to their own tenant; head office may target any tenant explicitly.</summary>
    public static Guid ResolveTenant(CurrentUser user, Guid? requestedTenantId = null)
    {
        if (user.IsAdmin && requestedTenantId.HasValue)
        {
            return requestedTenantId.Value;
        }

        if (user.TenantId.HasValue && user.TenantId.Value != Guid.Empty)
        {
            return user.TenantId.Value;
        }

        throw DomainException.Forbidden("No tenant associated with this account.");
    }

    /// <summary>Guards direct access to a tenant-owned resource.</summary>
    public static void EnsureCanAccess(CurrentUser user, Guid resourceTenantId)
    {
        if (user.IsAdmin)
        {
            return;
        }

        if (user.TenantId != resourceTenantId)
        {
            throw DomainException.Forbidden("This resource belongs to another tenant.");
        }
    }
}
