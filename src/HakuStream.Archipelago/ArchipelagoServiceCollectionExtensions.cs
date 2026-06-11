using HakuStream.Kit.Obs;
using HakuStream.Kit.Twitch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HakuStream.Archipelago;

public static class ArchipelagoServiceCollectionExtensions
{
    public static IServiceCollection AddArchipelago(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddObs(configuration);
        services.AddSingleton(configuration.GetSection("Archipelago").Get<ArchipelagoSettings>()
                              ?? new ArchipelagoSettings());
        services.AddSingleton<ArchipelagoPovService>();
        services.AddSingleton<ArchipelagoObsSetupService>();
        services.AddHostedService(sp => sp.GetRequiredService<ArchipelagoPovService>());
        services.AddChatCommands(typeof(ArchipelagoServiceCollectionExtensions).Assembly);
        return services;
    }
}
