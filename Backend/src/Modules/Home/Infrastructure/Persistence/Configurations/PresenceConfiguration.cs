using Backend.Kernel.Persistence.Configurations;
using Backend.Modules.Home.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistance.Configurations;

internal class PresenceConfiguration : AggregateRootConfiguration<Presence>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Presence> builder)
    {
        // Seed values must be constants: a value that differs per model build makes EF report pending changes.
        builder.HasData(new
        {
            Id = new Guid("01a217cc-f3c5-752e-8438-65a412278e23"),
            Status = PresenceStatus.Present,
            LastSeenAt = DateTimeOffset.MinValue,
            DeskIsBusy = false,
        });
    }
}
