using Backend.Kernel.Domain;

namespace Backend.Modules.Home.Domain;

public enum PresenceStatus
{
    Present,
    Away,
}

/// <summary>
/// Whether the resident is at home. Away is entered only from the IP checks of the phone; it is left only by
/// motion at the entrance (homecoming). The IP is irrelevant while away.
/// </summary>
public sealed class Presence : AggregateRoot
{
    public static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(2);

    private Presence()
    {
    }

    public PresenceStatus Status { get; private set; }

    /// <summary>
    /// Start of the current away deadline: the last time the IP answered, or the homecoming. Without the reset at
    /// homecoming, the first IP check after a long absence would see a stale value and turn the home off again.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; private set; }

    public static Presence Create(DateTimeOffset at) => new() { Status = PresenceStatus.Present, LastSeenAt = at };

    public void RecordIpPresence(bool reachable, DateTimeOffset checkedAt)
    {
        if (Status == PresenceStatus.Away)
        {
            return;
        }

        if (reachable)
        {
            if (checkedAt > LastSeenAt)
            {
                LastSeenAt = checkedAt;
            }

            return;
        }

        if (checkedAt - LastSeenAt >= AwayAfter)
        {
            Status = PresenceStatus.Away;
            Raise(new BecameAway(Id, LastSeenAt + AwayAfter));
        }
    }

    public void RecordMotion(DateTimeOffset detectedAt)
    {
        if (Status == PresenceStatus.Present)
        {
            return;
        }

        Status = PresenceStatus.Present;
        LastSeenAt = detectedAt;
        Raise(new HomecomingDetected(Id, detectedAt));
    }
}

/// <summary>The resident became away; <paramref name="BecameAwayAt"/> is the end of the deadline, not the time of the check.</summary>
public sealed record BecameAway(Guid PresenceId, DateTimeOffset BecameAwayAt) : DomainEvent;

/// <summary>The first motion at the entrance while away; <paramref name="DetectedAt"/> is the time of that motion.</summary>
public sealed record HomecomingDetected(Guid PresenceId, DateTimeOffset DetectedAt) : DomainEvent;
