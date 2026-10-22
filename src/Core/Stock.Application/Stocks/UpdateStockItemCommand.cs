using FluentValidation;
using MediatR;
using Stock.Application.Common;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Stocks;

public sealed record UpdateStockItemCommand(
    CurrentUser User,
    Guid Id,
    string Name,
    string Unit,
    decimal QuantityOnHand,
    decimal ThresholdMin,
    decimal ThresholdMax) : IRequest<StockItemDto>;

public sealed class UpdateStockItemValidator : AbstractValidator<UpdateStockItemCommand>
{
    public UpdateStockItemValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Unit).NotEmpty().MaximumLength(20);
        RuleFor(c => c.QuantityOnHand).GreaterThanOrEqualTo(0);
        RuleFor(c => c.ThresholdMin).GreaterThanOrEqualTo(0);
        RuleFor(c => c.ThresholdMax).GreaterThan(c => c.ThresholdMin)
            .WithMessage("ThresholdMax must be greater than ThresholdMin.");
    }
}

/// <summary>Thresholds can always change. Name, unit and quantity only while
/// nothing references the item (no recipe, no movement, request or order).</summary>
public sealed class UpdateStockItemHandler(
    IStockItemRepository stocks,
    StockUsageInspector usage,
    IUnitOfWork uow)
    : IRequestHandler<UpdateStockItemCommand, StockItemDto>
{
    public async Task<StockItemDto> Handle(UpdateStockItemCommand cmd, CancellationToken ct)
    {
        var item = await stocks.GetByIdAsync(cmd.Id, ct)
            ?? throw DomainException.NotFound("Stock item not found.");
        TenantAccess.EnsureCanAccess(cmd.User, item.TenantId);

        var name = cmd.Name.Trim();
        var unit = cmd.Unit.Trim();
        var nameChanged = name != item.Name;
        var identityChanged = nameChanged || unit != item.Unit || cmd.QuantityOnHand != item.QuantityOnHand;

        if (identityChanged && (await usage.InspectAsync(item, ct)).IsLocked)
        {
            throw DomainException.Conflict(
                "This item is used (recipe, stock movement, replenishment or order): only its thresholds can be changed.");
        }

        if (nameChanged && await stocks.NameExistsAsync(item.TenantId, name, item.Id, ct))
        {
            throw DomainException.Conflict($"A stock item named '{name}' already exists.");
        }

        item.Update(name, unit, cmd.QuantityOnHand, cmd.ThresholdMin, cmd.ThresholdMax);
        await uow.SaveChangesAsync(ct);
        return item.ToDto();
    }
}
