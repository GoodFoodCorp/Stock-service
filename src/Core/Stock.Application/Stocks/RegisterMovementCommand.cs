using FluentValidation;
using MediatR;
using Stock.Application.Common;
using Stock.Domain.Entities;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Stocks;

public sealed record RegisterMovementCommand(
    CurrentUser User,
    Guid StockItemId,
    string Type,
    decimal Quantity,
    string Reason) : IRequest<MovementResultDto>;

public sealed class RegisterMovementValidator : AbstractValidator<RegisterMovementCommand>
{
    private static readonly string[] AllowedTypes = ["IN", "OUT", "ADJUSTMENT"];

    public RegisterMovementValidator()
    {
        RuleFor(c => c.StockItemId).NotEmpty();
        RuleFor(c => c.Type)
            .Must(t => AllowedTypes.Contains(t?.ToUpperInvariant()))
            .WithMessage("Type must be IN, OUT or ADJUSTMENT.");
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class RegisterMovementHandler(
    IStockItemRepository stocks,
    IReplenishmentRequestRepository replenishments,
    IUnitOfWork uow)
    : IRequestHandler<RegisterMovementCommand, MovementResultDto>
{
    public async Task<MovementResultDto> Handle(RegisterMovementCommand cmd, CancellationToken ct)
    {
        var item = await stocks.GetByIdAsync(cmd.StockItemId, ct)
            ?? throw DomainException.NotFound("Stock item not found.");
        TenantAccess.EnsureCanAccess(cmd.User, item.TenantId);

        var type = Enum.Parse<MovementType>(cmd.Type, ignoreCase: true);
        var result = item.ApplyMovement(type, cmd.Quantity, cmd.Reason, cmd.User.UserId);

        await stocks.AddMovementAsync(result.Movement, ct);

        // Low-stock rule: persist the automatic suggestion unless one is
        // already pending for this item (avoids duplicate requests).
        ReplenishmentRequest? autoRequest = null;
        if (result.AutoReplenishment is not null &&
            !await replenishments.HasPendingForItemAsync(item.Id, ct))
        {
            autoRequest = result.AutoReplenishment;
            await replenishments.AddAsync(autoRequest, ct);
        }

        await uow.SaveChangesAsync(ct);

        return new MovementResultDto(result.Movement.ToDto(), item.ToDto(), autoRequest?.ToDto());
    }
}
