using MediatR;
using Stock.Application.Common;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Stocks;

public sealed record GetStockByIdQuery(CurrentUser User, Guid Id) : IRequest<StockItemDetailDto>;

public sealed record StockItemDetailDto(StockItemDto Item, IReadOnlyList<StockMovementDto> Movements);

public sealed class GetStockByIdHandler(IStockItemRepository stocks)
    : IRequestHandler<GetStockByIdQuery, StockItemDetailDto>
{
    public async Task<StockItemDetailDto> Handle(GetStockByIdQuery query, CancellationToken ct)
    {
        var item = await stocks.GetByIdAsync(query.Id, ct)
            ?? throw DomainException.NotFound("Stock item not found.");
        TenantAccess.EnsureCanAccess(query.User, item.TenantId);

        var movements = item.Movements
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.ToDto())
            .ToList();
        return new StockItemDetailDto(item.ToDto(), movements);
    }
}
