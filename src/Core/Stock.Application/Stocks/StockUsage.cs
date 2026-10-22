using Stock.Domain.Entities;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Stocks;

/// <summary>Port to the menu-service: which dishes of a restaurant use a stock item in their recipe.</summary>
public interface IRecipeUsageClient
{
    /// <summary>Names of the dishes using the item; throws <see cref="DomainException"/>
    /// (Unavailable) when menu-service cannot answer, so callers fail closed.</summary>
    Task<IReadOnlyList<string>> GetDishesUsingAsync(Guid tenantId, Guid stockItemId, CancellationToken ct = default);
}

/// <summary>What ties a stock item to the rest of the system. A used item is
/// "locked": it cannot be deleted and its name, unit and quantity cannot be
/// rewritten (recipes copy name/unit; history keeps the audit trail).</summary>
public sealed record StockItemUsageDto(
    IReadOnlyList<string> Dishes,
    bool HasMovements,
    bool HasReplenishments,
    bool HasOrders)
{
    public bool IsLocked => Dishes.Count > 0 || HasMovements || HasReplenishments || HasOrders;
}

public sealed class StockUsageInspector(
    IReplenishmentRequestRepository replenishments,
    IOrderConsumptionRepository orders,
    IRecipeUsageClient recipes)
{
    /// <summary>The item must be loaded with its movements.</summary>
    public async Task<StockItemUsageDto> InspectAsync(StockItem item, CancellationToken ct)
    {
        var hasReplenishments = await replenishments.AnyForItemAsync(item.Id, ct);
        var hasOrders = await orders.AnyForStockItemAsync(item.Id, ct);
        var dishes = await recipes.GetDishesUsingAsync(item.TenantId, item.Id, ct);
        return new StockItemUsageDto(dishes, item.Movements.Count > 0, hasReplenishments, hasOrders);
    }
}
