using Backend.Modules.Home.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.Modules.Home;

public static class HomeModule
{
    /// <summary>Registers the Home Assistant gateway (AD-18) as a background service behind <see cref="IHomeAssistantGateway"/>.</summary>
    public static IServiceCollection AddHome(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HomeAssistantOptions>().Bind(configuration.GetSection(HomeAssistantOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<HomeAssistantGateway>();
        services.AddSingleton<IHomeAssistantGateway>(provider => provider.GetRequiredService<HomeAssistantGateway>());
        services.AddHostedService(provider => provider.GetRequiredService<HomeAssistantGateway>());

        return services;
    }
}
