using Stock.Domain.Entities;

namespace Stock.Domain.Repositories;

/// <summary>Persistence port for replenishment requests.</summary>
public interface IReplenishmentRequestRepository
{
    Task<ReplenishmentRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ReplenishmentRequest>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<ReplenishmentRequest>> ListAllAsync(CancellationToken ct = default);

    Task<bool> HasPendingForItemAsync(Guid stockItemId, CancellationToken ct = default);

    Task AddAsync(ReplenishmentRequest request, CancellationToken ct = default);
}
