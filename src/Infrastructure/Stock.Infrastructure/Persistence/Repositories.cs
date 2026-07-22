using Microsoft.EntityFrameworkCore;
using Stock.Domain.Entities;
using Stock.Domain.Repositories;

namespace Stock.Infrastructure.Persistence;

public sealed class StockItemRepository(StockDbContext db) : IStockItemRepository
{
    public Task<StockItem?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.StockItems.Include(i => i.Movements)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<StockItem>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.StockItems
            .Where(i => i.TenantId == tenantId)
            .OrderBy(i => i.Name)
            .ToListAsync(ct);

    public async Task AddAsync(StockItem item, CancellationToken ct = default) =>
        await db.StockItems.AddAsync(item, ct);

    public Task AddMovementAsync(StockMovement movement, CancellationToken ct = default)
    {
        // The movement instance is also reachable through the aggregate's
        // navigation with its key pre-set, which EF would treat as an existing
        // row. Adding it explicitly marks this single instance as Added.
        db.StockMovements.Add(movement);
        return Task.CompletedTask;
    }
}

public sealed class ReplenishmentRequestRepository(StockDbContext db) : IReplenishmentRequestRepository
{
    public Task<ReplenishmentRequest?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.ReplenishmentRequests.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<ReplenishmentRequest>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.ReplenishmentRequests
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ReplenishmentRequest>> ListAllAsync(CancellationToken ct = default) =>
        await db.ReplenishmentRequests
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(ct);

    public Task<bool> HasPendingForItemAsync(Guid stockItemId, CancellationToken ct = default) =>
        db.ReplenishmentRequests.AnyAsync(
            r => r.StockItemId == stockItemId && r.Status == ReplenishmentStatus.Pending, ct);

    public async Task AddAsync(ReplenishmentRequest request, CancellationToken ct = default) =>
        await db.ReplenishmentRequests.AddAsync(request, ct);
}

public sealed class UnitOfWork(StockDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
