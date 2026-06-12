using System.Diagnostics;
using HakuStream.Kit.Twitch;
using HakuStream.Kit.Twitch.Auth;
using HakuStream.SongRequests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "log.txt"))
    .CreateLogger();

if (args.Contains("twitchreauth", StringComparer.OrdinalIgnoreCase))
{
    new WindowsCredentialStorage().Clear();
    Log.Information("Cleared the saved Twitch token. Run again without 'twitchreauth' to re-authorize.");
    Log.CloseAndFlush();
    return;
}

var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
if (!File.Exists(settingsPath))
{
    Exit("appsettings.json not found. Copy appsettings.example.json to appsettings.json (next to the exe) and fill in your values.");
    return;
}

if (!ToolExists("yt-dlp", "--version"))
{
    Exit("yt-dlp not found on PATH. Install it (e.g. 'winget install yt-dlp.yt-dlp') and restart.");
    return;
}

if (!ToolExists("ffmpeg", "-version"))
{
    Exit("ffmpeg not found on PATH. Install it (e.g. 'winget install Gyan.FFmpeg') and restart.");
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(settingsPath, optional: false);

builder.Services.AddSerilog();
builder.Services.AddTwitch(builder.Configuration);
builder.Services.AddSongRequests(builder.Configuration);

var host = builder.Build();

var auth = host.Services.GetRequiredService<TwitchAuthOrchestrator>();
if (!await auth.EnsureAuthenticatedAsync())
{
    Exit("Twitch authentication failed; exiting. Run with 'twitchreauth' to clear the saved token.");
    return;
}

Log.Information(
    "Song request bot running. In chat: !sr <youtube-url> to request, !q to see the queue, !oops to remove your last request. " +
    "Mods: !skip, !playlist. Broadcaster: !pause, !volume <0-100>, !prev.");
await host.RunAsync();
Log.CloseAndFlush();

static bool ToolExists(string fileName, string versionArgument)
{
    try
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = versionArgument,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        process!.WaitForExit(TimeSpan.FromSeconds(15));
        return true;
    }
    catch
    {
        return false;
    }
}

static void Exit(string error)
{
    Log.Error("{Error}", error);
    Log.CloseAndFlush();
    Console.WriteLine("Press any key to exit.");
    Console.ReadKey(true);
}
