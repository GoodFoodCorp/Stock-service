using Stock.Domain.Entities;

namespace Stock.Domain.Repositories;

/// <summary>Persistence port for the per-order consumption (idempotency) records.</summary>
public interface IOrderConsumptionRepository
{
    Task<OrderConsumption?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default);

    Task<bool> AnyForStockItemAsync(Guid stockItemId, CancellationToken ct = default);

    Task AddAsync(OrderConsumption consumption, CancellationToken ct = default);
}
