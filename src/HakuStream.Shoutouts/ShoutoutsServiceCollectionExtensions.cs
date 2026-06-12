using HakuStream.Kit.Obs;
using HakuStream.Kit.Twitch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HakuStream.Shoutouts;

public static class ShoutoutsServiceCollectionExtensions
{
    public static IServiceCollection AddShoutouts(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddObs(configuration);
        services.AddSingleton(configuration.GetSection("Shoutout").Get<ShoutoutSettings>()
                              ?? new ShoutoutSettings());
        services.AddSingleton<ShoutoutPlayer>();
        services.AddSingleton<LastClipTracker>();
        services.AddHostedService<LastClipWatcher>();
        services.AddChatCommands(typeof(ShoutoutsServiceCollectionExtensions).Assembly);
        return services;
    }
}
