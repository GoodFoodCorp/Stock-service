using MediatR;
using Stock.Application.Common;
using Stock.Domain.Repositories;

namespace Stock.Application.Replenishments;

public sealed record GetReplenishmentRequestsQuery(CurrentUser User, Guid? TenantId = null)
    : IRequest<IReadOnlyList<ReplenishmentRequestDto>>;

public sealed class GetReplenishmentRequestsHandler(IReplenishmentRequestRepository replenishments)
    : IRequestHandler<GetReplenishmentRequestsQuery, IReadOnlyList<ReplenishmentRequestDto>>
{
    public async Task<IReadOnlyList<ReplenishmentRequestDto>> Handle(GetReplenishmentRequestsQuery query, CancellationToken ct)
    {
        // Head office without an explicit tenant filter sees every request.
        if (query.User.IsAdmin && query.TenantId is null)
        {
            var all = await replenishments.ListAllAsync(ct);
            return all.Select(r => r.ToDto()).ToList();
        }

        var tenantId = TenantAccess.ResolveTenant(query.User, query.TenantId);
        var list = await replenishments.ListByTenantAsync(tenantId, ct);
        return list.Select(r => r.ToDto()).ToList();
    }
}
