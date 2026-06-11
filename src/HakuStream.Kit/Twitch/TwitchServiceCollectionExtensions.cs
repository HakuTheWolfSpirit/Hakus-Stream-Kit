using System.Reflection;
using HakuStream.Kit.Events;
using HakuStream.Kit.Twitch.Auth;
using HakuStream.Kit.Twitch.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TwitchLib.EventSub.Websockets.Extensions;

namespace HakuStream.Kit.Twitch;

public sealed record ChatCommandRegistration(Type HandlerType);

public sealed record RedeemRegistration(Type HandlerType);

public static class TwitchServiceCollectionExtensions
{
    public static IServiceCollection AddTwitch(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Twitch").Get<TwitchSettings>() ?? new TwitchSettings();
        services.AddSingleton(settings);

        services.AddSingleton<IEventBus, EventBus>();

        services.AddSingleton<TwitchOAuthClient>();
        services.AddSingleton<ISecureTokenStorage, WindowsCredentialStorage>();
        services.AddSingleton<TokenManager>();
        services.AddSingleton(_ => new TwitchApiClient(settings.ClientId));
        services.AddSingleton(sp => new TwitchAuthOrchestrator(
            sp.GetRequiredService<TwitchOAuthClient>(),
            sp.GetRequiredService<TokenManager>(),
            settings.ClientId,
            settings.ClientSecret,
            sp.GetRequiredService<ILogger<TwitchAuthOrchestrator>>()));

        services.AddSingleton<TwitchChatClient>();
        services.AddSingleton<TwitchChatWriter>();
        services.AddSingleton<TwitchChatMessageParser>();

        services.AddSingleton(sp =>
        {
            var registry = new CommandRegistry();
            foreach (var registration in sp.GetServices<ChatCommandRegistration>())
                registry.Register(registration.HandlerType);
            return registry;
        });
        services.AddSingleton<CooldownService>();
        services.AddSingleton<CommandDispatcher>();

        services.AddSingleton(sp =>
        {
            var registry = new RedeemRegistry();
            foreach (var registration in sp.GetServices<RedeemRegistration>())
                registry.Register(registration.HandlerType);
            return registry;
        });
        services.AddSingleton<RewardManager>();
        services.AddTwitchLibEventSubWebsockets();
        services.AddSingleton<RedeemRewardLifecycle>();

        services.AddHostedService<TwitchChatService>();
        services.AddHostedService<TokenRefreshService>();
        services.AddHostedService(sp => sp.GetRequiredService<RedeemRewardLifecycle>());
        services.AddHostedService<RedeemDispatcher>();
        services.AddHostedService<TwitchEventSubService>();

        return services;
    }

    public static IServiceCollection AddChatCommands(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || !typeof(IChatCommand).IsAssignableFrom(type)) continue;
            if (!type.GetCustomAttributes<CommandAttribute>().Any()) continue;

            services.AddScoped(type);
            services.AddSingleton(new ChatCommandRegistration(type));
        }

        return services;
    }

    public static IServiceCollection AddRedeems(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || !typeof(IChannelPointsRedeem).IsAssignableFrom(type)) continue;
            if (type.GetCustomAttribute<RedeemAttribute>() is null) continue;

            services.AddScoped(type);
            services.AddSingleton(new RedeemRegistration(type));
        }

        return services;
    }
}
