using MediatR;
using Stock.Application.Common;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Stocks;

public sealed record DeleteStockItemCommand(CurrentUser User, Guid Id) : IRequest;

/// <summary>Refuses to delete an item that a recipe, a movement, a
/// replenishment request or an order refers to.</summary>
public sealed class DeleteStockItemHandler(
    IStockItemRepository stocks,
    StockUsageInspector usage,
    IUnitOfWork uow)
    : IRequestHandler<DeleteStockItemCommand>
{
    public async Task Handle(DeleteStockItemCommand cmd, CancellationToken ct)
    {
        var item = await stocks.GetByIdAsync(cmd.Id, ct)
            ?? throw DomainException.NotFound("Stock item not found.");
        TenantAccess.EnsureCanAccess(cmd.User, item.TenantId);

        if ((await usage.InspectAsync(item, ct)).IsLocked)
        {
            throw DomainException.Conflict(
                "This item is used (recipe, stock movement, replenishment or order) and cannot be deleted.");
        }

        stocks.Remove(item);
        await uow.SaveChangesAsync(ct);
    }
}
