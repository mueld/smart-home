using Microsoft.EntityFrameworkCore;

namespace Backend.Kernel.Abstractions;

public interface IDbContext
{
    DbContext DbContext { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
