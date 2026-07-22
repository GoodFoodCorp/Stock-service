using FluentAssertions;
using Moq;
using Stock.Application.Common;
using Stock.Application.Stocks;
using Stock.Domain.Entities;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Tests;

public class RegisterMovementHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly CurrentUser Manager = new("mgr-1", TenantId, ["manager"]);
    private static readonly CurrentUser OtherManager = new("mgr-2", Guid.NewGuid(), ["manager"]);

    private readonly Mock<IStockItemRepository> _stocks = new();
    private readonly Mock<IReplenishmentRequestRepository> _replenishments = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private RegisterMovementHandler Handler => new(_stocks.Object, _replenishments.Object, _uow.Object);

    private StockItem SetupItem(decimal onHand = 20, decimal min = 10, decimal max = 50)
    {
        var item = new StockItem(TenantId, "Tomatoes", "kg", onHand, min, max);
        _stocks.Setup(s => s.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        return item;
    }

    [Fact]
    public async Task Out_movement_persists_and_returns_updated_stock()
    {
        var item = SetupItem(onHand: 30);

        var result = await Handler.Handle(
            new RegisterMovementCommand(Manager, item.Id, "OUT", 5, "service"), default);

        result.Item.QuantityOnHand.Should().Be(25);
        result.Movement.Type.Should().Be("OUT");
        result.AutoReplenishment.Should().BeNull();
        _stocks.Verify(s => s.AddMovementAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Low_stock_persists_automatic_replenishment()
    {
        var item = SetupItem(onHand: 12, min: 10, max: 50);
        _replenishments.Setup(r => r.HasPendingForItemAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Handler.Handle(
            new RegisterMovementCommand(Manager, item.Id, "OUT", 5, "rush"), default);

        result.AutoReplenishment.Should().NotBeNull();
        result.AutoReplenishment!.QuantityRequested.Should().Be(43);
        _replenishments.Verify(r => r.AddAsync(
            It.Is<ReplenishmentRequest>(x => x.IsAutomatic && x.StockItemId == item.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Low_stock_with_existing_pending_request_does_not_duplicate()
    {
        var item = SetupItem(onHand: 12, min: 10, max: 50);
        _replenishments.Setup(r => r.HasPendingForItemAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Handler.Handle(
            new RegisterMovementCommand(Manager, item.Id, "OUT", 5, "rush"), default);

        result.AutoReplenishment.Should().BeNull();
        _replenishments.Verify(r => r.AddAsync(It.IsAny<ReplenishmentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Manager_of_another_tenant_is_forbidden()
    {
        var item = SetupItem();

        var act = () => Handler.Handle(
            new RegisterMovementCommand(OtherManager, item.Id, "OUT", 1, "sneaky"), default);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be(DomainErrorCode.Forbidden);
    }

    [Fact]
    public async Task Unknown_item_returns_not_found()
    {
        _stocks.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockItem?)null);

        var act = () => Handler.Handle(
            new RegisterMovementCommand(Manager, Guid.NewGuid(), "IN", 1, "x"), default);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be(DomainErrorCode.NotFound);
    }
}
