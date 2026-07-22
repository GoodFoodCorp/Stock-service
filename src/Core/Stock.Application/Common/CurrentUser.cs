namespace Stock.Application.Common;

/// <summary>
/// Authenticated caller, built by the API layer from JWT claims and passed
/// inside each command/query so handlers stay HTTP-agnostic and testable.
/// Role slugs come from auth-service: admin (head office), manager (franchisee).
/// </summary>
public sealed record CurrentUser(string UserId, Guid? TenantId, IReadOnlyList<string> Roles)
{
    public bool IsAdmin => Roles.Contains("admin");

    public bool IsManager => Roles.Contains("manager");
}
