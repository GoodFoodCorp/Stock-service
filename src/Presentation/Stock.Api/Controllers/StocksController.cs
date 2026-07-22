using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stock.Api.Auth;
using Stock.Application.Stocks;

namespace Stock.Api.Controllers;

/// <summary>Thin controllers: build the command/query, send it, return the
/// result. All rules live in Application/Domain.</summary>
[ApiController]
[Route("api/stocks")]
[Authorize(Roles = "manager,admin")]
public sealed class StocksController(IMediator mediator) : ControllerBase
{
    public sealed record CreateStockItemBody(
        string Name, string Unit, decimal QuantityOnHand, decimal ThresholdMin, decimal ThresholdMax, Guid? TenantId);

    public sealed record MovementBody(string Type, decimal Quantity, string Reason);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? tenantId, CancellationToken ct) =>
        Ok(await mediator.Send(new GetStocksQuery(CurrentUserFactory.FromClaims(User), tenantId), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await mediator.Send(new GetStockByIdQuery(CurrentUserFactory.FromClaims(User), id), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStockItemBody body, CancellationToken ct)
    {
        var dto = await mediator.Send(new CreateStockItemCommand(
            CurrentUserFactory.FromClaims(User),
            body.Name, body.Unit, body.QuantityOnHand, body.ThresholdMin, body.ThresholdMax, body.TenantId), ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPost("{id:guid}/movements")]
    public async Task<IActionResult> RegisterMovement(Guid id, [FromBody] MovementBody body, CancellationToken ct)
    {
        var result = await mediator.Send(new RegisterMovementCommand(
            CurrentUserFactory.FromClaims(User), id, body.Type, body.Quantity, body.Reason), ct);
        return Created($"/api/stocks/{id}", result);
    }
}
