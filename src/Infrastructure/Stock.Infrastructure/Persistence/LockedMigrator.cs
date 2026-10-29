using Microsoft.EntityFrameworkCore;

namespace Stock.Infrastructure.Persistence;

/// <summary>Applies EF Core migrations under a PostgreSQL advisory lock, so
/// replicas starting together never run the same migration concurrently
/// (EF Core 8 takes no lock of its own).</summary>
public static class LockedMigrator
{
    /// <summary>Lock id shared by every replica of this service.</summary>
    private const long LockId = 0x73746f636b; // "stock"

    public static async Task MigrateAsync(DbContext db, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_lock({LockId})", ct);
            try
            {
                await db.Database.MigrateAsync(ct);
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({LockId})", CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
