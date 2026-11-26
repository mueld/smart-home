using Backend.Kernel.Domain;

namespace Backend.Modules.Home.Domain;

public enum PresenceStatus
{
    Present,
    Away,
}

/// <summary>
/// Whether the resident is at home. Away is entered when the phone's IP has not answered for <see cref="AwayAfter"/>
/// and the desk is not busy; it is left only by motion at the entrance (homecoming). The IP is irrelevant while away.
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

    /// <summary>Last reported use of the desk; false until the first report.</summary>
    public bool DeskIsBusy { get; private set; }

    public static Presence Create(DateTimeOffset at) => new() { Status = PresenceStatus.Present, LastSeenAt = at };

    public void RecordIpPresence(bool reachable, DateTimeOffset checkedAt)
    {
        if (Status == PresenceStatus.Away)
        {
            return;
        }

        // A busy desk counts like an answering IP: it is only reported on change, so the check must look at it.
        if (reachable || DeskIsBusy)
        {
            SeenAt(checkedAt);
            return;
        }

        BecomeAwayWhenDeadlinePassed(checkedAt);
    }

    /// <summary>
    /// Recorded in every state, so a busy desk is known after a homecoming. While away it does not end the absence:
    /// only motion does.
    /// </summary>
    public void RecordPower(double? watts, DeskBusyThreshold threshold, DateTimeOffset reportedAt) =>
        RecordDeskUse(threshold.IsBusy(watts), reportedAt);

    /// <summary>A desk without power cannot be in use.</summary>
    public void DeskHasBeenSwitchedOff(DateTimeOffset reportedAt) => RecordDeskUse(isBusy: false, reportedAt);

    private void RecordDeskUse(bool isBusy, DateTimeOffset reportedAt)
    {
        var wasBusy = DeskIsBusy;
        DeskIsBusy = isBusy;

        if (Status == PresenceStatus.Away || DeskIsBusy)
        {
            return;
        }

        // The away deadline starts when the desk stops being busy, not at the last IP answer before.
        if (wasBusy)
        {
            SeenAt(reportedAt);
        }

        BecomeAwayWhenDeadlinePassed(reportedAt);
    }

    private void BecomeAwayWhenDeadlinePassed(DateTimeOffset at)
    {
        if (at - LastSeenAt >= AwayAfter)
        {
            Status = PresenceStatus.Away;
            Raise(new BecameAwayDomainEvent(Id, LastSeenAt + AwayAfter));
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
        Raise(new HomecomingDetectedDomainEvent(Id, detectedAt));
    }
}

/// <summary>The resident became away; <paramref name="BecameAwayAt"/> is the end of the deadline, not the time of the check.</summary>
public sealed record BecameAwayDomainEvent(Guid PresenceId, DateTimeOffset BecameAwayAt) : DomainEvent;

/// <summary>The first motion at the entrance while away; <paramref name="DetectedAt"/> is the time of that motion.</summary>
public sealed record HomecomingDetectedDomainEvent(Guid PresenceId, DateTimeOffset DetectedAt) : DomainEvent;
