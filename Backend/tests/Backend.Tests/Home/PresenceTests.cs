using Backend.Kernel.Domain;
using Backend.Modules.Home.Domain;

namespace Backend.Tests.Home;

public sealed class PresenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 17, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int minutes, int seconds = 0) => Start.AddMinutes(minutes).AddSeconds(seconds);

    private static Presence AwaySince(DateTimeOffset lastSeen)
    {
        var presence = Presence.Create(lastSeen);
        presence.RecordIpPresence(reachable: false, lastSeen + Presence.AwayAfter);
        Assert.Equal(PresenceStatus.Away, presence.Status);
        presence.ClearEvents();
        return presence;
    }

    [Fact]
    public void Starts_present()
    {
        var presence = Presence.Create(Start);

        Assert.Equal(PresenceStatus.Present, presence.Status);
        Assert.Empty(presence.DomainEvents);
    }

    [Fact]
    public void Motion_while_away_without_ip_is_a_homecoming()
    {
        var presence = AwaySince(Start);

        presence.RecordMotion(At(30));

        Assert.Equal(PresenceStatus.Present, presence.Status);
        var homecoming = Assert.IsType<HomecomingDetected>(Assert.Single(presence.DomainEvents));
        Assert.Equal(presence.Id, homecoming.PresenceId);
        Assert.Equal(At(30), homecoming.DetectedAt);
    }

    [Fact]
    public void Motion_while_away_after_ip_returned_is_a_homecoming()
    {
        var presence = AwaySince(Start);
        presence.RecordIpPresence(reachable: true, At(30));

        presence.RecordMotion(At(30, 20));

        Assert.Equal(PresenceStatus.Present, presence.Status);
        Assert.IsType<HomecomingDetected>(Assert.Single(presence.DomainEvents));
    }

    [Fact]
    public void Ip_returning_while_away_changes_nothing()
    {
        var presence = AwaySince(Start);

        presence.RecordIpPresence(reachable: true, At(30));
        presence.RecordIpPresence(reachable: true, At(31));

        Assert.Equal(PresenceStatus.Away, presence.Status);
        Assert.Empty(presence.DomainEvents);
    }

    [Fact]
    public void Motion_after_ip_returned_and_left_again_is_still_a_homecoming()
    {
        var presence = AwaySince(Start);
        presence.RecordIpPresence(reachable: true, At(30));
        presence.RecordIpPresence(reachable: false, At(35));

        presence.RecordMotion(At(40));

        Assert.Equal(PresenceStatus.Present, presence.Status);
        Assert.IsType<HomecomingDetected>(Assert.Single(presence.DomainEvents));
    }

    [Fact]
    public void Motion_while_present_changes_nothing()
    {
        var presence = Presence.Create(Start);

        presence.RecordMotion(At(1));

        Assert.Equal(PresenceStatus.Present, presence.Status);
        Assert.Empty(presence.DomainEvents);
    }

    [Fact]
    public void Further_motion_after_homecoming_changes_nothing()
    {
        var presence = AwaySince(Start);
        presence.RecordMotion(At(30));

        presence.RecordMotion(At(30, 3));
        presence.RecordMotion(At(31));

        Assert.Equal(PresenceStatus.Present, presence.Status);
        Assert.Single(presence.DomainEvents.OfType<HomecomingDetected>());
    }

    [Fact]
    public void Motion_while_leaving_does_not_prevent_becoming_away()
    {
        var presence = Presence.Create(Start);
        presence.RecordMotion(At(0, 10));

        presence.RecordIpPresence(reachable: false, At(2));

        Assert.Equal(PresenceStatus.Away, presence.Status);
        var away = Assert.IsType<BecameAway>(Assert.Single(presence.DomainEvents));
        Assert.Equal(At(2), away.BecameAwayAt);
    }

    [Fact]
    public void Away_deadline_restarts_with_homecoming()
    {
        var presence = AwaySince(Start);
        presence.RecordMotion(At(300));
        presence.ClearEvents();

        presence.RecordIpPresence(reachable: false, At(300, 30));

        Assert.Equal(PresenceStatus.Present, presence.Status);
        Assert.Empty(presence.DomainEvents);
    }

    [Fact]
    public void Becomes_away_again_two_minutes_after_homecoming_without_ip()
    {
        var presence = AwaySince(Start);
        presence.RecordMotion(At(300));
        presence.ClearEvents();

        presence.RecordIpPresence(reachable: false, At(302));
        presence.RecordMotion(At(310));

        Assert.Collection(
            presence.DomainEvents,
            e => Assert.Equal(At(302), Assert.IsType<BecameAway>(e).BecameAwayAt),
            e => Assert.Equal(At(310), Assert.IsType<HomecomingDetected>(e).DetectedAt));
    }
}

internal static class AggregateRootTestExtensions
{
    // Mimics a save: the interceptor takes the events off the aggregate after SaveChanges.
    public static void ClearEvents(this AggregateRoot aggregate) =>
        typeof(AggregateRoot)
            .GetMethod("TakeDomainEvents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(aggregate, null);
}
