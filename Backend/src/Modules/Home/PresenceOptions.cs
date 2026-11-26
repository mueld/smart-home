using Backend.Modules.Home.Domain;

namespace Backend.Modules.Home;

/// <summary>Configurable values of the presence rules.</summary>
public sealed class PresenceOptions
{
    public const string SectionName = "Presence";

    public double DeskBusyAboveWatts { get; set; } = 50;

    public DeskBusyThreshold DeskBusyThreshold => new(DeskBusyAboveWatts);
}
