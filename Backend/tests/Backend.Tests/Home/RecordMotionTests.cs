using Backend.Kernel;
using Backend.Modules.Home;
using Backend.Modules.Home.Domain;
using Backend.Modules.Home.Features.Shared;
using Backend.Modules.Home.Infrastructure.Persistence;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Home;

public sealed class RecordMotionTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 26, 17, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<HomeDbContext> _options;
    private readonly ServiceProvider _services;

    public RecordMotionTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<HomeDbContext>().UseSqlite(_connection).Options;
        using (var db = new HomeDbContext(_options))
        {
            db.Database.EnsureCreated();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new MotionRecorder());
        services.AddSingleton(new DeskRecorder());
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddKernel();
        services.AddHome(new ConfigurationBuilder().Build());
        services.AddScoped<IHomeDbContext>(_ => new HomeDbContext(_options));
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    private async Task<Presence> Publish(object domainEvent)
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(domainEvent);

        await using var db = new HomeDbContext(_options);
        return await db.Presences.AsNoTracking().SingleAsync();
    }

    [Fact]
    public async Task Motion_while_away_ends_the_absence()
    {
        var away = await Publish(new UserIPCheckedAtHomeDomainEvent(false, T0));
        Assert.Equal(PresenceStatus.Away, away.Status);

        var presence = await Publish(new MotionDetectedDomainEvent(T0.AddMinutes(5)));

        Assert.Equal(PresenceStatus.Present, presence.Status);
        Assert.Equal(T0.AddMinutes(5), presence.LastSeenAt);
    }

    [Fact]
    public async Task Motion_while_present_changes_nothing()
    {
        var before = await Publish(new UserIPCheckedAtHomeDomainEvent(true, T0));

        var presence = await Publish(new MotionDetectedDomainEvent(T0.AddMinutes(5)));

        Assert.Equal(PresenceStatus.Present, presence.Status);
        Assert.Equal(before.LastSeenAt, presence.LastSeenAt);
    }
}
