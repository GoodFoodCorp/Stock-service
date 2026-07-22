using Stock.Domain.Entities;

namespace Stock.Domain.Repositories;

/// <summary>Persistence port for stock items (implemented in Infrastructure).</summary>
public interface IStockItemRepository
{
    Task<StockItem?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<StockItem>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(StockItem item, CancellationToken ct = default);

    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);
}
