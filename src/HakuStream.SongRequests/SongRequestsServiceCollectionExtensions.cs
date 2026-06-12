using HakuStream.Kit.Twitch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HakuStream.SongRequests;

public static class SongRequestsServiceCollectionExtensions
{
    public static IServiceCollection AddSongRequests(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration.GetSection("Songs").Get<SongSettings>() ?? new SongSettings());
        services.AddSingleton<YouTubeClient>();
        services.AddSingleton<AudioDownloader>();
        services.AddSingleton<SongQueue>();
        services.AddSingleton<SongTitleStore>();
        services.AddSingleton<BackupPlaylist>();
        services.AddSingleton<SongPlayer>();
        services.AddSingleton<SongRequestService>();
        services.AddSingleton<SongPlayerService>();
        services.AddHostedService(sp => sp.GetRequiredService<SongPlayerService>());
        services.AddChatCommands(typeof(SongRequestsServiceCollectionExtensions).Assembly);
        return services;
    }
}
