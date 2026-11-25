using Backend.Kernel.Domain;

namespace Backend.Modules.Home.Domain;

/// <summary>The desk socket switched on or off; <paramref name="ChangedAt"/> is the time Home Assistant gives (AD-8).</summary>
public sealed record DeskSwitched(bool IsOn, DateTimeOffset ChangedAt) : DomainEvent;

/// <summary>The desk socket was switched off; <paramref name="ChangedAt"/> is the time Home Assistant gives (AD-8).</summary>
public sealed record DeskHasBeenSwitchedOffDomainEvent(DateTimeOffset ChangedAt) : DomainEvent;

/// <summary>
/// The power of the desk socket changed. <paramref name="Watts"/> is null when Home Assistant reports no number
/// (for example <c>unavailable</c>); <paramref name="ChangedAt"/> is the time Home Assistant gives (AD-8).
/// </summary>
public sealed record DeskPowerChanged(double? Watts, DateTimeOffset ChangedAt) : DomainEvent;
