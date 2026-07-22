using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stock.Api.Auth;
using Stock.Application.Replenishments;

namespace Stock.Api.Controllers;

[ApiController]
[Route("api/replenishment-requests")]
public sealed class ReplenishmentRequestsController(IMediator mediator) : ControllerBase
{
    public sealed record CreateBody(Guid StockItemId, decimal QuantityRequested);

    public sealed record StatusBody(string Status);

    [HttpGet]
    [Authorize(Roles = "manager,admin")]
    public async Task<IActionResult> List([FromQuery] Guid? tenantId, CancellationToken ct) =>
        Ok(await mediator.Send(new GetReplenishmentRequestsQuery(CurrentUserFactory.FromClaims(User), tenantId), ct));

    [HttpPost]
    [Authorize(Roles = "manager,admin")]
    public async Task<IActionResult> Create([FromBody] CreateBody body, CancellationToken ct)
    {
        var dto = await mediator.Send(new CreateReplenishmentRequestCommand(
            CurrentUserFactory.FromClaims(User), body.StockItemId, body.QuantityRequested), ct);
        return Created($"/api/replenishment-requests/{dto.Id}", dto);
    }

    // Status changes are a head-office decision (spec: SIEGE_ADMIN → admin slug).
    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] StatusBody body, CancellationToken ct) =>
        Ok(await mediator.Send(new UpdateReplenishmentStatusCommand(
            CurrentUserFactory.FromClaims(User), id, body.Status), ct));
}
