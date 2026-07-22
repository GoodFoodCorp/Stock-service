namespace Stock.Domain.Repositories;

/// <summary>Commits all pending changes of the current request atomically.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}
