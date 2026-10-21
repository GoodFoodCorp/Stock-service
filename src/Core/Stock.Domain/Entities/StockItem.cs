using Stock.Domain.Errors;

namespace Stock.Domain.Entities;

/// <summary>
/// Aggregate root: a stocked product for one tenant (restaurant).
/// All quantity changes go through <see cref="ApplyMovement"/> so the audit
/// trail and the low-stock replenishment rule can never be bypassed.
/// </summary>
public sealed class StockItem
{
    private readonly List<StockMovement> _movements = [];

    private StockItem() { } // EF Core

    public StockItem(Guid tenantId, string name, string unit, decimal quantityOnHand, decimal thresholdMin, decimal thresholdMax)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw DomainException.Validation("Stock item name is required.");
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            throw DomainException.Validation("Stock item unit is required (e.g. kg, L, piece).");
        }

        if (quantityOnHand < 0 || thresholdMin < 0)
        {
            throw DomainException.Validation("Quantities and thresholds cannot be negative.");
        }

        if (thresholdMax <= thresholdMin)
        {
            throw DomainException.Validation("ThresholdMax must be greater than ThresholdMin.");
        }

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = name.Trim();
        Unit = unit.Trim();
        QuantityOnHand = quantityOnHand;
        ThresholdMin = thresholdMin;
        ThresholdMax = thresholdMax;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Unit { get; private set; } = string.Empty;

    public decimal QuantityOnHand { get; private set; }

    public decimal ThresholdMin { get; private set; }

    public decimal ThresholdMax { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<StockMovement> Movements => _movements.AsReadOnly();

    public bool IsBelowMinimum => QuantityOnHand < ThresholdMin;

    /// <summary>
    /// Applies a stock movement and returns it, along with an automatic
    /// replenishment suggestion when an OUT movement drops the quantity
    /// below the minimum threshold (business rule from the spec).
    /// </summary>
    public MovementResult ApplyMovement(MovementType type, decimal quantity, string reason, string createdBy)
    {
        switch (type)
        {
            case MovementType.In:
                if (quantity <= 0)
                {
                    throw DomainException.Validation("IN quantity must be greater than zero.");
                }

                QuantityOnHand += quantity;
                break;

            case MovementType.Out:
                if (quantity <= 0)
                {
                    throw DomainException.Validation("OUT quantity must be greater than zero.");
                }

                if (quantity > QuantityOnHand)
                {
                    throw DomainException.Conflict($"Cannot remove {quantity} {Unit}: only {QuantityOnHand} on hand.");
                }

                QuantityOnHand -= quantity;
                break;

            case MovementType.Adjustment:
                if (quantity < 0)
                {
                    throw DomainException.Validation("Adjustment quantity (new absolute value) cannot be negative.");
                }

                QuantityOnHand = quantity;
                break;

            default:
                throw DomainException.Validation($"Unknown movement type: {type}.");
        }

        UpdatedAt = DateTimeOffset.UtcNow;
        var movement = new StockMovement(Id, type, quantity, reason, createdBy);
        _movements.Add(movement);

        ReplenishmentRequest? suggestion = null;
        if (type == MovementType.Out && IsBelowMinimum)
        {
            // Suggest refilling back up to the max threshold.
            suggestion = new ReplenishmentRequest(
                TenantId, Id, ThresholdMax - QuantityOnHand, createdBy, isAutomatic: true);
        }

        return new MovementResult(movement, suggestion);
    }

    /// <summary>Receiving a replenishment adds the delivered quantity to stock.</summary>
    public StockMovement ReceiveReplenishment(ReplenishmentRequest request, string receivedBy)
    {
        if (request.StockItemId != Id)
        {
            throw DomainException.Conflict("Replenishment request does not target this stock item.");
        }

        return ApplyMovement(MovementType.In, request.QuantityRequested, "Replenishment received", receivedBy).Movement;
    }

    /// <summary>
    /// Consumes stock for a confirmed order. Unlike an OUT movement it never fails
    /// on insufficient stock: it removes what is available (clamped at zero) and
    /// records the unserved quantity as a shortfall on the movement.
    /// </summary>
    public OrderConsumeResult ConsumeForOrder(decimal quantity, Guid orderId, string createdBy)
    {
        if (quantity <= 0)
        {
            throw DomainException.Validation("Consumed quantity must be greater than zero.");
        }

        var removed = Math.Min(quantity, QuantityOnHand);
        var shortfall = quantity - removed;

        QuantityOnHand -= removed;
        UpdatedAt = DateTimeOffset.UtcNow;

        var movement = new StockMovement(Id, MovementType.Out, removed, $"Order {orderId}", createdBy, shortfall);
        _movements.Add(movement);

        ReplenishmentRequest? suggestion = null;
        if (IsBelowMinimum)
        {
            suggestion = new ReplenishmentRequest(
                TenantId, Id, ThresholdMax - QuantityOnHand, createdBy, isAutomatic: true);
        }

        return new OrderConsumeResult(movement, suggestion, removed, shortfall);
    }

    /// <summary>Puts back the quantity actually consumed by a cancelled order.</summary>
    public StockMovement RestoreFromOrder(decimal quantity, Guid orderId, string createdBy) =>
        ApplyMovement(MovementType.In, quantity, $"Order {orderId} cancelled", createdBy).Movement;
}

public sealed record MovementResult(StockMovement Movement, ReplenishmentRequest? AutoReplenishment);

public sealed record OrderConsumeResult(StockMovement Movement, ReplenishmentRequest? AutoReplenishment, decimal Removed, decimal Shortfall);
