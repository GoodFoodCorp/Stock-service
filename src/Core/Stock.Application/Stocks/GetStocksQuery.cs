using MediatR;
using Stock.Application.Common;
using Stock.Domain.Repositories;

namespace Stock.Application.Stocks;

public sealed record GetStocksQuery(CurrentUser User, Guid? TenantId = null) : IRequest<IReadOnlyList<StockItemDto>>;

public sealed class GetStocksHandler(IStockItemRepository stocks)
    : IRequestHandler<GetStocksQuery, IReadOnlyList<StockItemDto>>
{
    public async Task<IReadOnlyList<StockItemDto>> Handle(GetStocksQuery query, CancellationToken ct)
    {
        var tenantId = TenantAccess.ResolveTenant(query.User, query.TenantId);
        var items = await stocks.ListByTenantAsync(tenantId, ct);
        return items.Select(i => i.ToDto()).ToList();
    }
}
