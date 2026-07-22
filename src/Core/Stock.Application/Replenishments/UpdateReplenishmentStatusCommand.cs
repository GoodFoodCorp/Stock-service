using FluentValidation;
using MediatR;
using Stock.Application.Common;
using Stock.Domain.Entities;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Replenishments;

public sealed record UpdateReplenishmentStatusCommand(
    CurrentUser User,
    Guid RequestId,
    string Status) : IRequest<ReplenishmentRequestDto>;

public sealed class UpdateReplenishmentStatusValidator : AbstractValidator<UpdateReplenishmentStatusCommand>
{
    public UpdateReplenishmentStatusValidator()
    {
        RuleFor(c => c.RequestId).NotEmpty();
        RuleFor(c => c.Status)
            .Must(s => Enum.TryParse<ReplenishmentStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be PENDING, APPROVED, ORDERED, RECEIVED or CANCELLED.");
    }
}

public sealed class UpdateReplenishmentStatusHandler(
    IReplenishmentRequestRepository replenishments,
    IStockItemRepository stocks,
    IUnitOfWork uow)
    : IRequestHandler<UpdateReplenishmentStatusCommand, ReplenishmentRequestDto>
{
    public async Task<ReplenishmentRequestDto> Handle(UpdateReplenishmentStatusCommand cmd, CancellationToken ct)
    {
        var request = await replenishments.GetByIdAsync(cmd.RequestId, ct)
            ?? throw DomainException.NotFound("Replenishment request not found.");

        var target = Enum.Parse<ReplenishmentStatus>(cmd.Status, ignoreCase: true);
        request.TransitionTo(target);

        // Receiving the goods adds them back into stock (IN movement).
        if (target == ReplenishmentStatus.Received)
        {
            var item = await stocks.GetByIdAsync(request.StockItemId, ct)
                ?? throw DomainException.NotFound("Stock item of this request no longer exists.");
            var movement = item.ReceiveReplenishment(request, cmd.User.UserId);
            await stocks.AddMovementAsync(movement, ct);
        }

        await uow.SaveChangesAsync(ct);
        return request.ToDto();
    }
}
