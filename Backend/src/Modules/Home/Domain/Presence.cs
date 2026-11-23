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

    /// <summary>Last reported power of the desk in watts; null while unknown (for example <c>unavailable</c>).</summary>
    public double? DeskPower { get; private set; }

    private bool IsDeskBusy => DeskPower > DeskBusyAbove;

    public const double DeskBusyAbove = 50;

    public static Presence Create(DateTimeOffset at) => new() { Status = PresenceStatus.Present, LastSeenAt = at };

    public void RecordIpPresence(bool reachable, DateTimeOffset checkedAt)
    {
        if (Status == PresenceStatus.Away)
        {
            return;
        }

        // A busy desk counts like an answering IP: power only arrives on change, so the check must look at it.
        if (reachable || IsDeskBusy)
        {
            SeenAt(checkedAt);
            return;
        }

        if (checkedAt - LastSeenAt >= AwayAfter)
        {
            Status = PresenceStatus.Away;
            Raise(new BecameAway(Id, LastSeenAt + AwayAfter));
        }
    }

    /// <summary>
    /// Recorded in every state, so a busy desk is known after a homecoming. While away it does not end the absence:
    /// only motion does.
    /// </summary>
    public void RecordPower(double? watts, DateTimeOffset reportedAt)
    {
        var wasBusy = IsDeskBusy;
        DeskPower = watts;

        // The away deadline starts when the desk stops being busy, not at the last IP answer before.
        if (Status == PresenceStatus.Present && wasBusy && !IsDeskBusy)
        {
            SeenAt(reportedAt);
        }
    }

    private void SeenAt(DateTimeOffset at)
    {
        if (at > LastSeenAt)
        {
            LastSeenAt = at;
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
