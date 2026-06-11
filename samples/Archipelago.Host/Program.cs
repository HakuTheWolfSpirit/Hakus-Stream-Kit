using HakuStream.Archipelago;
using HakuStream.Kit.Twitch;
using HakuStream.Kit.Twitch.Auth;
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
    Log.Error(
        "appsettings.json not found. Copy appsettings.example.json to appsettings.json (next to the exe) and fill in your values.");
    Log.CloseAndFlush();
    Console.WriteLine("Press any key to exit.");
    Console.ReadKey(true);
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(settingsPath, optional: false);

builder.Services.AddSerilog();
builder.Services.AddTwitch(builder.Configuration);
builder.Services.AddArchipelago(builder.Configuration);

var host = builder.Build();

var auth = host.Services.GetRequiredService<TwitchAuthOrchestrator>();
if (!await auth.EnsureAuthenticatedAsync())
{
    Log.Error("Twitch authentication failed; exiting. Run with 'twitchreauth' to clear the saved token.");
    Log.CloseAndFlush();
    Console.WriteLine("Press any key to exit.");
    Console.ReadKey(true);
    return;
}

Log.Information("Archipelago bot running. In chat: !ap setup <name1> [name2] [name3] to start, !ap stop when done.");
await host.RunAsync();
Log.CloseAndFlush();
