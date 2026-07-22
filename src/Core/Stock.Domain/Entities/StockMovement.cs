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

    internal StockMovement(Guid stockItemId, MovementType type, decimal quantity, string reason, string createdBy)
    {
        Id = Guid.NewGuid();
        StockItemId = stockItemId;
        Type = type;
        Quantity = quantity;
        Reason = reason;
        CreatedBy = createdBy;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid StockItemId { get; private set; }

    public MovementType Type { get; private set; }

    /// <summary>Moved quantity; for Adjustment this is the new absolute quantity.</summary>
    public decimal Quantity { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;
}
