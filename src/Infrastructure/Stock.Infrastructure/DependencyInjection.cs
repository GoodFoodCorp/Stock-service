using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stock.Application.Stocks;
using Stock.Domain.Repositories;
using Stock.Infrastructure.Clients;
using Stock.Infrastructure.Persistence;

namespace Stock.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("StockDb")
            ?? throw new InvalidOperationException("ConnectionStrings__StockDb is required.");

        services.AddDbContext<StockDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IStockItemRepository, StockItemRepository>();
        services.AddScoped<IReplenishmentRequestRepository, ReplenishmentRequestRepository>();
        services.AddScoped<IOrderConsumptionRepository, OrderConsumptionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        var menuUrl = configuration["MENU_SERVICE_URL"] ?? "http://menu-service:8085";
        var serviceKey = configuration["SERVICE_API_KEY"] ?? string.Empty;
        services.AddHttpClient<IRecipeUsageClient, MenuRecipeUsageClient>(client =>
        {
            client.BaseAddress = new Uri(menuUrl);
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.Add(MenuRecipeUsageClient.ServiceKeyHeader, serviceKey);
        });
        return services;
    }
}
