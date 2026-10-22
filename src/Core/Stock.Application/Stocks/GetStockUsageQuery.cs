using MediatR;
using Stock.Application.Common;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Stocks;

public sealed record GetStockUsageQuery(CurrentUser User, Guid Id) : IRequest<StockItemUsageResponse>;

public sealed record StockItemUsageResponse(
    IReadOnlyList<string> Dishes,
    bool HasMovements,
    bool HasReplenishments,
    bool HasOrders,
    bool IsLocked);

public sealed class GetStockUsageHandler(IStockItemRepository stocks, StockUsageInspector usage)
    : IRequestHandler<GetStockUsageQuery, StockItemUsageResponse>
{
    public async Task<StockItemUsageResponse> Handle(GetStockUsageQuery query, CancellationToken ct)
    {
        var item = await stocks.GetByIdAsync(query.Id, ct)
            ?? throw DomainException.NotFound("Stock item not found.");
        TenantAccess.EnsureCanAccess(query.User, item.TenantId);

        var u = await usage.InspectAsync(item, ct);
        return new StockItemUsageResponse(u.Dishes, u.HasMovements, u.HasReplenishments, u.HasOrders, u.IsLocked);
    }
}
