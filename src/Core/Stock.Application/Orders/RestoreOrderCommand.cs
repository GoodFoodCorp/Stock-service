using FluentValidation;
using MediatR;
using Stock.Domain.Entities;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Orders;

public sealed record RestoreResultDto(string Status);

public sealed record RestoreOrderCommand(Guid OrderId, Guid TenantId) : IRequest<RestoreResultDto>;

public sealed class RestoreOrderValidator : AbstractValidator<RestoreOrderCommand>
{
    public RestoreOrderValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.TenantId).NotEmpty();
    }
}

public sealed class RestoreOrderHandler(
    IStockItemRepository stocks,
    IOrderConsumptionRepository consumptions,
    IUnitOfWork uow)
    : IRequestHandler<RestoreOrderCommand, RestoreResultDto>
{
    private const string Actor = "order-service";

    public async Task<RestoreResultDto> Handle(RestoreOrderCommand cmd, CancellationToken ct)
    {
        var record = await consumptions.GetByOrderIdAsync(cmd.OrderId, ct);

        if (record is null)
        {
            // Cancellation arrived before the (delayed) consumption: block it.
            await consumptions.AddAsync(OrderConsumption.CancelledBeforeConsume(cmd.OrderId, cmd.TenantId), ct);
            await uow.SaveChangesAsync(ct);
            return new RestoreResultDto("CANCELLED_BEFORE_CONSUME");
        }

        if (record.TenantId != cmd.TenantId)
        {
            throw DomainException.NotFound("Order consumption not found.");
        }

        if (record.Status == OrderConsumptionStatus.Cancelled)
        {
            return new RestoreResultDto("ALREADY_RESTORED");
        }

        // Lines hold what was actually removed. Zero-quantity lines (item was empty)
        // have nothing to give back and RestoreFromOrder rejects them.
        var lines = record.Lines.Where(l => l.Quantity > 0).ToList();

        var items = (await stocks.GetByIdsAsync(lines.Select(l => l.StockItemId), ct))
            .Where(i => i.TenantId == cmd.TenantId)
            .ToDictionary(i => i.Id);

        // Resolve every item before mutating anything.
        if (lines.Any(l => !items.ContainsKey(l.StockItemId)))
        {
            throw DomainException.NotFound("Stock item not found.");
        }

        record.Cancel();
        foreach (var line in lines)
        {
            var item = items[line.StockItemId];
            await stocks.AddMovementAsync(item.RestoreFromOrder(line.Quantity, cmd.OrderId, Actor), ct);
        }

        await uow.SaveChangesAsync(ct);
        return new RestoreResultDto("RESTORED");
    }
}
