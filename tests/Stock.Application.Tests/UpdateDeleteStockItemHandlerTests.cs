using FluentAssertions;
using Moq;
using Stock.Application.Common;
using Stock.Application.Stocks;
using Stock.Domain.Entities;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Tests;

public class UpdateDeleteStockItemHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly CurrentUser Manager = new("mgr-1", TenantId, ["manager"]);
    private static readonly CurrentUser OtherManager = new("mgr-2", Guid.NewGuid(), ["manager"]);

    private readonly Mock<IStockItemRepository> _stocks = new();
    private readonly Mock<IReplenishmentRequestRepository> _replenishments = new();
    private readonly Mock<IOrderConsumptionRepository> _orders = new();
    private readonly Mock<IRecipeUsageClient> _recipes = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private StockUsageInspector Inspector => new(_replenishments.Object, _orders.Object, _recipes.Object);

    private UpdateStockItemHandler UpdateHandler => new(_stocks.Object, Inspector, _uow.Object);

    private DeleteStockItemHandler DeleteHandler => new(_stocks.Object, Inspector, _uow.Object);

    private StockItem SetupItem(bool withMovement = false, bool inRecipe = false, bool inReplenishment = false, bool inOrder = false)
    {
        var item = new StockItem(TenantId, "Tomatoes", "kg", 20, 5, 50);
        if (withMovement)
        {
            item.ApplyMovement(MovementType.In, 1, "x", "mgr-1");
        }

        _stocks.Setup(s => s.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _recipes.Setup(r => r.GetDishesUsingAsync(TenantId, item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inRecipe ? ["Cheeseburger"] : []);
        _replenishments.Setup(r => r.AnyForItemAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inReplenishment);
        _orders.Setup(o => o.AnyForStockItemAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inOrder);
        return item;
    }

    [Fact]
    public async Task Unused_item_can_be_fully_rewritten()
    {
        var item = SetupItem();

        var dto = await UpdateHandler.Handle(
            new UpdateStockItemCommand(Manager, item.Id, "Cherry tomatoes", "g", 300, 100, 900), default);

        dto.Name.Should().Be("Cherry tomatoes");
        dto.Unit.Should().Be("g");
        dto.QuantityOnHand.Should().Be(300);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public async Task Used_item_rejects_name_change_but_accepts_thresholds(bool movement, bool recipe, bool replenishment, bool order)
    {
        var item = SetupItem(movement, recipe, replenishment, order);

        var rename = () => UpdateHandler.Handle(
            new UpdateStockItemCommand(Manager, item.Id, "Other", item.Unit, item.QuantityOnHand, 5, 50), default);
        (await rename.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.Conflict);

        var dto = await UpdateHandler.Handle(
            new UpdateStockItemCommand(Manager, item.Id, item.Name, item.Unit, item.QuantityOnHand, 8, 80), default);
        dto.ThresholdMin.Should().Be(8);
        dto.ThresholdMax.Should().Be(80);
    }

    [Fact]
    public async Task Threshold_only_change_never_calls_menu_service()
    {
        var item = SetupItem();

        await UpdateHandler.Handle(
            new UpdateStockItemCommand(Manager, item.Id, item.Name, item.Unit, item.QuantityOnHand, 8, 80), default);

        _recipes.Verify(r => r.GetDishesUsingAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rename_to_an_existing_name_is_a_conflict()
    {
        var item = SetupItem();
        _stocks.Setup(s => s.NameExistsAsync(TenantId, "Salads", item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => UpdateHandler.Handle(
            new UpdateStockItemCommand(Manager, item.Id, "Salads", "kg", 20, 5, 50), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.Conflict);
    }

    [Fact]
    public async Task Menu_service_failure_blocks_a_name_change()
    {
        var item = SetupItem();
        _recipes.Setup(r => r.GetDishesUsingAsync(TenantId, item.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(DomainException.Unavailable("down"));

        var act = () => UpdateHandler.Handle(
            new UpdateStockItemCommand(Manager, item.Id, "Other", "kg", 20, 5, 50), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.Unavailable);
    }

    [Fact]
    public async Task Unused_item_is_deleted()
    {
        var item = SetupItem();

        await DeleteHandler.Handle(new DeleteStockItemCommand(Manager, item.Id), default);

        _stocks.Verify(s => s.Remove(item), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public async Task Used_item_cannot_be_deleted(bool movement, bool recipe, bool replenishment, bool order)
    {
        var item = SetupItem(movement, recipe, replenishment, order);

        var act = () => DeleteHandler.Handle(new DeleteStockItemCommand(Manager, item.Id), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.Conflict);
        _stocks.Verify(s => s.Remove(It.IsAny<StockItem>()), Times.Never);
    }

    [Fact]
    public async Task Other_tenant_cannot_delete_or_update()
    {
        var item = SetupItem();

        var del = () => DeleteHandler.Handle(new DeleteStockItemCommand(OtherManager, item.Id), default);
        (await del.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.Forbidden);

        var upd = () => UpdateHandler.Handle(
            new UpdateStockItemCommand(OtherManager, item.Id, "x", "kg", 1, 1, 2), default);
        (await upd.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.Forbidden);
    }
}
