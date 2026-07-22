using FluentValidation;
using MediatR;
using Stock.Application.Common;
using Stock.Domain.Entities;
using Stock.Domain.Repositories;

namespace Stock.Application.Stocks;

public sealed record CreateStockItemCommand(
    CurrentUser User,
    string Name,
    string Unit,
    decimal QuantityOnHand,
    decimal ThresholdMin,
    decimal ThresholdMax,
    Guid? TenantId = null) : IRequest<StockItemDto>;

public sealed class CreateStockItemValidator : AbstractValidator<CreateStockItemCommand>
{
    public CreateStockItemValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Unit).NotEmpty().MaximumLength(20);
        RuleFor(c => c.QuantityOnHand).GreaterThanOrEqualTo(0);
        RuleFor(c => c.ThresholdMin).GreaterThanOrEqualTo(0);
        RuleFor(c => c.ThresholdMax).GreaterThan(c => c.ThresholdMin)
            .WithMessage("ThresholdMax must be greater than ThresholdMin.");
    }
}

public sealed class CreateStockItemHandler(IStockItemRepository stocks, IUnitOfWork uow)
    : IRequestHandler<CreateStockItemCommand, StockItemDto>
{
    public async Task<StockItemDto> Handle(CreateStockItemCommand cmd, CancellationToken ct)
    {
        var tenantId = TenantAccess.ResolveTenant(cmd.User, cmd.TenantId);
        var item = new StockItem(tenantId, cmd.Name, cmd.Unit, cmd.QuantityOnHand, cmd.ThresholdMin, cmd.ThresholdMax);

        await stocks.AddAsync(item, ct);
        await uow.SaveChangesAsync(ct);
        return item.ToDto();
    }
}
