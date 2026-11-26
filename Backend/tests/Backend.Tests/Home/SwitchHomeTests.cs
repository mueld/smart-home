using Backend.Kernel;
using Backend.Modules.Home;
using Backend.Modules.Home.Domain;
using Backend.Modules.Home.Features.SwitchHomeOff;
using Backend.Modules.Home.Features.SwitchHomeOn;
using Backend.Modules.Home.Infrastructure.HomeAssistant;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Home;

public sealed class SwitchHomeTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 26, 17, 0, 0, TimeSpan.Zero);

    private readonly FakeHomeAssistantGateway _gateway = new();
    private readonly ServiceProvider _services;

    public SwitchHomeTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new MotionRecorder());
        services.AddSingleton(new DeskRecorder());
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddKernel();
        services.AddHome(new ConfigurationBuilder().Build());
        services.AddSingleton<IHomeAssistantGateway>(_gateway);
        _services = services.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private async Task InScope(Func<IMediator, ValueTask> action)
    {
        await using var scope = _services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<IMediator>());
    }

    [Fact]
    public async Task SwitchHomeOn_turns_all_switches_on()
    {
        await InScope(async mediator => await mediator.Send(new SwitchHomeOnCommand()));

        Assert.Equal(["on"], _gateway.SwitchCalls);
    }

    [Fact]
    public async Task SwitchHomeOff_turns_all_switches_off()
    {
        await InScope(async mediator => await mediator.Send(new SwitchHomeOffCommand()));

        Assert.Equal(["off"], _gateway.SwitchCalls);
    }

    [Fact]
    public async Task A_detected_homecoming_switches_the_home_on()
    {
        await InScope(mediator => mediator.Publish(new HomecomingDetectedDomainEvent(Guid.CreateVersion7(), T0)));

        Assert.Equal(["on"], _gateway.SwitchCalls);
    }

    [Fact]
    public async Task Becoming_away_switches_the_home_off()
    {
        await InScope(mediator => mediator.Publish(new BecameAwayDomainEvent(Guid.CreateVersion7(), T0)));

        Assert.Equal(["off"], _gateway.SwitchCalls);
    }
}
