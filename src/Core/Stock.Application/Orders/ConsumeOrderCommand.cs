using FluentValidation;
using MediatR;
using Stock.Domain.Entities;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Orders;

public sealed record ShortfallDto(Guid StockItemId, decimal Shortfall);

public sealed record ConsumeResultDto(string Status, IReadOnlyList<ShortfallDto> Shortfalls);

public sealed record ConsumeOrderCommand(Guid OrderId, Guid TenantId, IReadOnlyList<ConsumptionLine> Lines)
    : IRequest<ConsumeResultDto>;

public sealed class ConsumeOrderValidator : AbstractValidator<ConsumeOrderCommand>
{
    public ConsumeOrderValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.Lines).NotEmpty();
        RuleForEach(c => c.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.StockItemId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}

public sealed class ConsumeOrderHandler(
    IStockItemRepository stocks,
    IReplenishmentRequestRepository replenishments,
    IOrderConsumptionRepository consumptions,
    IUnitOfWork uow)
    : IRequestHandler<ConsumeOrderCommand, ConsumeResultDto>
{
    private const string Actor = "order-service";

    public async Task<ConsumeResultDto> Handle(ConsumeOrderCommand cmd, CancellationToken ct)
    {
        var existing = await consumptions.GetByOrderIdAsync(cmd.OrderId, ct);
        if (existing is not null)
        {
            if (existing.TenantId != cmd.TenantId)
            {
                throw DomainException.NotFound("Order consumption not found.");
            }

            return new ConsumeResultDto(
                existing.Status == OrderConsumptionStatus.Cancelled ? "SKIPPED_CANCELLED" : "ALREADY_APPLIED",
                []);
        }

        var required = cmd.Lines
            .GroupBy(l => l.StockItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var items = (await stocks.GetByIdsAsync(required.Keys, ct))
            .Where(i => i.TenantId == cmd.TenantId)
            .ToDictionary(i => i.Id);

        // Validate everything before mutating so a bad line leaves nothing half-applied.
        if (required.Keys.Any(id => !items.ContainsKey(id)))
        {
            throw DomainException.NotFound("Stock item not found.");
        }

        var removedLines = new List<ConsumptionLine>();
        var shortfalls = new List<ShortfallDto>();
        foreach (var (id, quantity) in required)
        {
            var item = items[id];
            var result = item.ConsumeForOrder(quantity, cmd.OrderId, Actor);

            await stocks.AddMovementAsync(result.Movement, ct);
            removedLines.Add(new ConsumptionLine(id, result.Removed));
            if (result.Shortfall > 0)
            {
                shortfalls.Add(new ShortfallDto(id, result.Shortfall));
            }

            // Same duplicate guard as RegisterMovementHandler.
            if (result.AutoReplenishment is not null &&
                !await replenishments.HasPendingForItemAsync(item.Id, ct))
            {
                await replenishments.AddAsync(result.AutoReplenishment, ct);
            }
        }

        await consumptions.AddAsync(OrderConsumption.Applied(cmd.OrderId, cmd.TenantId, removedLines), ct);
        await uow.SaveChangesAsync(ct);

        return new ConsumeResultDto("APPLIED", shortfalls);
    }
}
