using FluentAssertions;
using Moq;
using Stock.Application.Common;
using Stock.Application.Replenishments;
using Stock.Domain.Entities;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Tests;

public class UpdateReplenishmentStatusHandlerTests
{
    private static readonly CurrentUser Admin = new("adm-1", null, ["admin"]);

    private readonly Mock<IReplenishmentRequestRepository> _replenishments = new();
    private readonly Mock<IStockItemRepository> _stocks = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private UpdateReplenishmentStatusHandler Handler => new(_replenishments.Object, _stocks.Object, _uow.Object);

    [Fact]
    public async Task Approves_pending_request()
    {
        var request = new ReplenishmentRequest(Guid.NewGuid(), Guid.NewGuid(), 20, "mgr-1");
        _replenishments.Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var dto = await Handler.Handle(
            new UpdateReplenishmentStatusCommand(Admin, request.Id, "APPROVED"), default);

        dto.Status.Should().Be("APPROVED");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Received_adds_stock_back()
    {
        var item = new StockItem(Guid.NewGuid(), "Flour", "kg", 2, 10, 40);
        var request = new ReplenishmentRequest(item.TenantId, item.Id, 38, "mgr-1");
        request.TransitionTo(ReplenishmentStatus.Approved);
        request.TransitionTo(ReplenishmentStatus.Ordered);

        _replenishments.Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _stocks.Setup(s => s.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var dto = await Handler.Handle(
            new UpdateReplenishmentStatusCommand(Admin, request.Id, "RECEIVED"), default);

        dto.Status.Should().Be("RECEIVED");
        item.QuantityOnHand.Should().Be(40, "received goods must be added to stock");
        _stocks.Verify(s => s.AddMovementAsync(
            It.Is<StockMovement>(m => m.Type == MovementType.In && m.Quantity == 38),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Invalid_transition_is_rejected()
    {
        var request = new ReplenishmentRequest(Guid.NewGuid(), Guid.NewGuid(), 20, "mgr-1");
        _replenishments.Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var act = () => Handler.Handle(
            new UpdateReplenishmentStatusCommand(Admin, request.Id, "RECEIVED"), default);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be(DomainErrorCode.InvalidTransition);
    }

    [Fact]
    public async Task Unknown_request_returns_not_found()
    {
        _replenishments.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReplenishmentRequest?)null);

        var act = () => Handler.Handle(
            new UpdateReplenishmentStatusCommand(Admin, Guid.NewGuid(), "APPROVED"), default);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be(DomainErrorCode.NotFound);
    }
}
