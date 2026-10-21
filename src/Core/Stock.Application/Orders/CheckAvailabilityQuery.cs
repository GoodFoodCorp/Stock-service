using FluentValidation;
using MediatR;
using Stock.Domain.Entities;
using Stock.Domain.Repositories;

namespace Stock.Application.Orders;

public sealed record MissingItemDto(Guid StockItemId, string Name, string Unit, decimal Required, decimal OnHand);

public sealed record AvailabilityDto(bool Available, IReadOnlyList<MissingItemDto> Missing);

public sealed record CheckAvailabilityQuery(Guid TenantId, IReadOnlyList<ConsumptionLine> Lines)
    : IRequest<AvailabilityDto>;

public sealed class CheckAvailabilityValidator : AbstractValidator<CheckAvailabilityQuery>
{
    public CheckAvailabilityValidator()
    {
        RuleFor(q => q.TenantId).NotEmpty();
        RuleFor(q => q.Lines).NotEmpty();
        RuleForEach(q => q.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.StockItemId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}

public sealed class CheckAvailabilityHandler(IStockItemRepository stocks)
    : IRequestHandler<CheckAvailabilityQuery, AvailabilityDto>
{
    public async Task<AvailabilityDto> Handle(CheckAvailabilityQuery query, CancellationToken ct)
    {
        var required = query.Lines
            .GroupBy(l => l.StockItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        // Items of another tenant are treated as unknown so nothing about them leaks.
        var items = (await stocks.GetByIdsAsync(required.Keys, ct))
            .Where(i => i.TenantId == query.TenantId)
            .ToDictionary(i => i.Id);

        var missing = new List<MissingItemDto>();
        foreach (var (id, quantity) in required)
        {
            if (!items.TryGetValue(id, out var item))
            {
                missing.Add(new MissingItemDto(id, string.Empty, string.Empty, quantity, 0));
            }
            else if (item.QuantityOnHand < quantity)
            {
                missing.Add(new MissingItemDto(id, item.Name, item.Unit, quantity, item.QuantityOnHand));
            }
        }

        return new AvailabilityDto(missing.Count == 0, missing);
    }
}
