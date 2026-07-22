using FluentValidation;
using MediatR;
using Stock.Application.Common;
using Stock.Domain.Entities;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Replenishments;

public sealed record CreateReplenishmentRequestCommand(
    CurrentUser User,
    Guid StockItemId,
    decimal QuantityRequested) : IRequest<ReplenishmentRequestDto>;

public sealed class CreateReplenishmentRequestValidator : AbstractValidator<CreateReplenishmentRequestCommand>
{
    public CreateReplenishmentRequestValidator()
    {
        RuleFor(c => c.StockItemId).NotEmpty();
        RuleFor(c => c.QuantityRequested).GreaterThan(0);
    }
}

public sealed class CreateReplenishmentRequestHandler(
    IStockItemRepository stocks,
    IReplenishmentRequestRepository replenishments,
    IUnitOfWork uow)
    : IRequestHandler<CreateReplenishmentRequestCommand, ReplenishmentRequestDto>
{
    public async Task<ReplenishmentRequestDto> Handle(CreateReplenishmentRequestCommand cmd, CancellationToken ct)
    {
        var item = await stocks.GetByIdAsync(cmd.StockItemId, ct)
            ?? throw DomainException.NotFound("Stock item not found.");
        TenantAccess.EnsureCanAccess(cmd.User, item.TenantId);

        var request = new ReplenishmentRequest(item.TenantId, item.Id, cmd.QuantityRequested, cmd.User.UserId);
        await replenishments.AddAsync(request, ct);
        await uow.SaveChangesAsync(ct);
        return request.ToDto();
    }
}
