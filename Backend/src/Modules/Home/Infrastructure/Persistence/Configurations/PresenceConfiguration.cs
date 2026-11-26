using Backend.Kernel.Persistence.Configurations;
using Backend.Modules.Home.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistance.Configurations;

internal class PresenceConfiguration : AggregateRootConfiguration<Presence>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Presence> builder)
    {
        builder.HasData(Presence.Create(DateTimeOffset.MinValue));
    }
}
