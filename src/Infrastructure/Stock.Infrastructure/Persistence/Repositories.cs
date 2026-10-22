using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stock.Domain.Entities;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Infrastructure.Persistence;

public sealed class StockItemRepository(StockDbContext db) : IStockItemRepository
{
    public Task<StockItem?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.StockItems.Include(i => i.Movements)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<StockItem>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.Distinct().ToList();
        return await db.StockItems.Where(i => idList.Contains(i.Id)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StockItem>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.StockItems
            .Where(i => i.TenantId == tenantId)
            .OrderBy(i => i.Name)
            .ToListAsync(ct);

    public async Task AddAsync(StockItem item, CancellationToken ct = default) =>
        await db.StockItems.AddAsync(item, ct);

    public void Remove(StockItem item) => db.StockItems.Remove(item);

    public Task<bool> NameExistsAsync(Guid tenantId, string name, Guid excludingId, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        return db.StockItems.AnyAsync(i => i.TenantId == tenantId && i.Id != excludingId && i.Name == trimmed, ct);
    }

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

    public Task<bool> AnyForItemAsync(Guid stockItemId, CancellationToken ct = default) =>
        db.ReplenishmentRequests.AnyAsync(r => r.StockItemId == stockItemId, ct);

    public async Task AddAsync(ReplenishmentRequest request, CancellationToken ct = default) =>
        await db.ReplenishmentRequests.AddAsync(request, ct);
}

public sealed class OrderConsumptionRepository(StockDbContext db) : IOrderConsumptionRepository
{
    public Task<OrderConsumption?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default) =>
        db.OrderConsumptions.FirstOrDefaultAsync(c => c.OrderId == orderId, ct);

    public Task<bool> AnyForStockItemAsync(Guid stockItemId, CancellationToken ct = default) =>
        db.OrderConsumptions.AnyAsync(c => c.Lines.Any(l => l.StockItemId == stockItemId), ct);

    public async Task AddAsync(OrderConsumption consumption, CancellationToken ct = default) =>
        await db.OrderConsumptions.AddAsync(consumption, ct);
}

public sealed class UnitOfWork(StockDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainException.Conflict("The stock was modified concurrently, please retry.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw DomainException.Conflict("A concurrent request is already processing this resource, please retry.");
        }
    }
}
