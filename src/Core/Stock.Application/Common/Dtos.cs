using Stock.Domain.Entities;

namespace Stock.Application.Common;

public sealed record StockItemDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string Unit,
    decimal QuantityOnHand,
    decimal ThresholdMin,
    decimal ThresholdMax,
    bool IsBelowMinimum,
    DateTimeOffset UpdatedAt);

public sealed record StockMovementDto(
    Guid Id,
    Guid StockItemId,
    string Type,
    decimal Quantity,
    string Reason,
    DateTimeOffset CreatedAt,
    string CreatedBy);

public sealed record ReplenishmentRequestDto(
    Guid Id,
    Guid TenantId,
    Guid StockItemId,
    decimal QuantityRequested,
    string Status,
    string RequestedBy,
    bool IsAutomatic,
    DateTimeOffset RequestedAt,
    DateTimeOffset UpdatedAt);

public sealed record MovementResultDto(
    StockMovementDto Movement,
    StockItemDto Item,
    ReplenishmentRequestDto? AutoReplenishment);

public static class DtoMapping
{
    public static StockItemDto ToDto(this StockItem i) => new(
        i.Id, i.TenantId, i.Name, i.Unit, i.QuantityOnHand,
        i.ThresholdMin, i.ThresholdMax, i.IsBelowMinimum, i.UpdatedAt);

    public static StockMovementDto ToDto(this StockMovement m) => new(
        m.Id, m.StockItemId, m.Type.ToString().ToUpperInvariant(), m.Quantity,
        m.Reason, m.CreatedAt, m.CreatedBy);

    public static ReplenishmentRequestDto ToDto(this ReplenishmentRequest r) => new(
        r.Id, r.TenantId, r.StockItemId, r.QuantityRequested,
        r.Status.ToString().ToUpperInvariant(), r.RequestedBy, r.IsAutomatic,
        r.RequestedAt, r.UpdatedAt);
}
