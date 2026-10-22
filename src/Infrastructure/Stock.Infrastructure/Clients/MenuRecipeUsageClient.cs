using System.Net.Http.Json;
using Stock.Application.Stocks;
using Stock.Domain.Errors;

namespace Stock.Infrastructure.Clients;

/// <summary>Calls menu-service's internal <c>/internal/recipes/usage</c> route,
/// authenticated by the shared service key (never logged nor put in errors).
/// Any failure becomes <c>Unavailable</c>, so deletes and edits fail closed.</summary>
public sealed class MenuRecipeUsageClient(HttpClient http) : IRecipeUsageClient
{
    public const string ServiceKeyHeader = "X-Service-Key";

    private sealed record UsageResponse(Dictionary<string, List<string>>? Usage);

    public async Task<IReadOnlyList<string>> GetDishesUsingAsync(Guid tenantId, Guid stockItemId, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                "/internal/recipes/usage",
                new { restaurant_id = tenantId, stock_item_ids = new[] { stockItemId } },
                ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<UsageResponse>(
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web), ct);
            return body?.Usage is { } usage && usage.TryGetValue(stockItemId.ToString(), out var dishes)
                ? dishes
                : [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw DomainException.Unavailable("Cannot check recipe usage right now (menu-service unreachable), please retry.");
        }
    }
}
