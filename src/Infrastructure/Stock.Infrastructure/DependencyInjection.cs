using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stock.Domain.Repositories;
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
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }
}
