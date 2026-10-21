namespace Stock.Domain.Entities;

public enum OrderConsumptionStatus
{
    Applied,
    Cancelled,
}

/// <summary>Quantity of one stock item actually removed for an order.</summary>
public sealed record ConsumptionLine(Guid StockItemId, decimal Quantity);

/// <summary>
/// Idempotency and cancellation record for one order. Lines hold the quantities
/// actually removed (not the requested ones), so a cancellation restores exactly
/// what was taken. A record created as cancelled blocks a late consumption.
/// </summary>
public sealed class OrderConsumption
{
    private readonly List<ConsumptionLine> _lines = [];

    private OrderConsumption() { } // EF Core

    private OrderConsumption(Guid orderId, Guid tenantId, OrderConsumptionStatus status, IEnumerable<ConsumptionLine> lines)
    {
        OrderId = orderId;
        TenantId = tenantId;
        Status = status;
        _lines.AddRange(lines);
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid OrderId { get; private set; }

    public Guid TenantId { get; private set; }

    public OrderConsumptionStatus Status { get; private set; }

    public IReadOnlyCollection<ConsumptionLine> Lines => _lines.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }

    public static OrderConsumption Applied(Guid orderId, Guid tenantId, IEnumerable<ConsumptionLine> lines) =>
        new(orderId, tenantId, OrderConsumptionStatus.Applied, lines);

    public static OrderConsumption CancelledBeforeConsume(Guid orderId, Guid tenantId) =>
        new(orderId, tenantId, OrderConsumptionStatus.Cancelled, []);

    /// <summary>Marks the order cancelled and returns the lines to restore; empty if already cancelled.</summary>
    public IReadOnlyList<ConsumptionLine> Cancel()
    {
        if (Status == OrderConsumptionStatus.Cancelled)
        {
            return [];
        }

        Status = OrderConsumptionStatus.Cancelled;
        return [.. _lines];
    }
}
