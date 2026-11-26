using Backend.Modules.Home.Domain;
using Backend.Modules.Home.Features;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Home.Infrastructure.Persistence;

/// <summary>The data of the Home module, in its own schema (AD-3).</summary>
public sealed class HomeDbContext(DbContextOptions<HomeDbContext> options) : DbContext(options), IHomeDbContext
{
    public static readonly string Schema = "home";

    public DbSet<Presence> Presences => Set<Presence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HomeDbContext).Assembly);
    }
}
