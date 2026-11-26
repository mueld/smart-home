using Backend.Kernel.Abstractions;
using Backend.Modules.Home.Domain;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Home.Features;

public interface IHomeDbContext: IDbContext
{
    DbSet<Presence> Presences { get; }
}
