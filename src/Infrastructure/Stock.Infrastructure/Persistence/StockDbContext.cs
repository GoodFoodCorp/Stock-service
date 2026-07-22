using Microsoft.EntityFrameworkCore;
using Stock.Domain.Entities;

namespace Stock.Infrastructure.Persistence;

public sealed class StockDbContext(DbContextOptions<StockDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<ReplenishmentRequest> ReplenishmentRequests => Set<ReplenishmentRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockItem>(e =>
        {
            e.ToTable("stock_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(20).IsRequired();
            e.Property(x => x.QuantityOnHand).HasPrecision(12, 3);
            e.Property(x => x.ThresholdMin).HasPrecision(12, 3);
            e.Property(x => x.ThresholdMax).HasPrecision(12, 3);
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasMany(x => x.Movements)
                .WithOne()
                .HasForeignKey(m => m.StockItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Ignore(x => x.IsBelowMinimum);
        });

        modelBuilder.Entity<StockMovement>(e =>
        {
            e.ToTable("stock_movements");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Quantity).HasPrecision(12, 3);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.StockItemId);
        });

        modelBuilder.Entity<ReplenishmentRequest>(e =>
        {
            e.ToTable("replenishment_requests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.QuantityRequested).HasPrecision(12, 3);
            e.Property(x => x.RequestedBy).HasMaxLength(100);
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => new { x.StockItemId, x.Status });
        });
    }
}
