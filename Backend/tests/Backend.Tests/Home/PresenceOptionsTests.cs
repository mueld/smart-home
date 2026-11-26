using Backend.Modules.Home;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.Tests.Home;

public sealed class PresenceOptionsTests
{
    [Theory]
    [InlineData(null, 50.0)]
    [InlineData("100.5", 100.5)]
    public void The_desk_busy_threshold_comes_from_configuration(string? configured, double expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configured is null ? [] : new Dictionary<string, string?> { ["Presence:DeskBusyAboveWatts"] = configured })
            .Build();
        using var services = new ServiceCollection().AddHome(configuration).BuildServiceProvider();

        var options = services.GetRequiredService<IOptions<PresenceOptions>>().Value;

        Assert.Equal(expected, options.DeskBusyThreshold.Watts);
    }
}
