using Backend.Kernel.Abstractions;
using Backend.Modules.Home.Domain;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Home.Features.Shared;

public interface IHomeDbContext: IDbContext
{
    DbSet<Presence> Presences { get; }
}
