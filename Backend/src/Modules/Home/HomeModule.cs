using Backend.Kernel;
using Backend.Modules.Home.Features.Shared;
using Backend.Modules.Home.Infrastructure.HomeAssistant;
using Backend.Modules.Home.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;

namespace Backend.Modules.Home;

public static class HomeModule
{
    /// <summary>Registers the Home Assistant gateway (AD-18) as a background service behind <see cref="IHomeAssistantGateway"/>.</summary>
    public static IServiceCollection AddHome(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HomeAssistantOptions>().Bind(configuration.GetSection(HomeAssistantOptions.SectionName));
        services.AddOptions<PresenceOptions>().Bind(configuration.GetSection(PresenceOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        // Named, not typed: the gateway is a singleton and creates a client per use, so the factory can rotate handlers.
        services.AddHttpClient(HomeAssistantGateway.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<HomeAssistantOptions>>().Value;
            client.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/");
            client.Timeout = options.ConnectTimeout;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        });

        services.AddSingleton<HomeAssistantGateway>();
        services.AddSingleton<IHomeAssistantGateway>(provider => provider.GetRequiredService<HomeAssistantGateway>());
        services.AddHostedService(provider => provider.GetRequiredService<HomeAssistantGateway>());

        services.AddModuleDbContext<HomeDbContext>(typeof(HomeModule).Namespace!, configuration.GetConnectionString("database")!, HomeDbContext.Schema);
        services.AddScoped<IHomeDbContext>(provider => provider.GetRequiredService<HomeDbContext>());

        return services;
    }

    public static async Task MigrateHomeAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken
    )
    {
        using IServiceScope scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HomeDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
