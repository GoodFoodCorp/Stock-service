namespace Stock.Domain.Entities;

public enum MovementType
{
    In,
    Out,
    Adjustment,
}

/// <summary>Immutable audit-trail record of every stock change.</summary>
public sealed class StockMovement
{
    private StockMovement() { } // EF Core

    internal StockMovement(Guid stockItemId, MovementType type, decimal quantity, string reason, string createdBy, decimal shortfall = 0)
    {
        Id = Guid.NewGuid();
        StockItemId = stockItemId;
        Type = type;
        Quantity = quantity;
        Shortfall = shortfall;
        Reason = reason;
        CreatedBy = createdBy;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid StockItemId { get; private set; }

    public MovementType Type { get; private set; }

    /// <summary>Moved quantity; for Adjustment this is the new absolute quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Requested quantity that could not be served (order consumption on insufficient stock); 0 otherwise.</summary>
    public decimal Shortfall { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;
}
