# HakuStream.Kit

Building blocks for a Twitch stream bot, plus ready-to-use modules built on them.

- **HakuStream.Kit** — the framework: Twitch chat + EventSub connection, OAuth device flow with token storage in Windows Credential Manager, attribute-based chat commands and channel-point redeems, an event bus, an OBS WebSocket client, and tiny atomic JSON persistence.
- **HakuStream.Archipelago** — POV switching for [Archipelago](https://archipelago.gg/) multiworld races: viewers spend channel points to choose which runner's POV the stream shows.
- **samples/Archipelago.Host** — a ready-to-run bot exe that hosts the Archipelago module. If you just want the bot, this is what you run.

Windows only (credential storage and the audio/OBS tooling assume it).

## I just want the exe

1. Download the latest `ArchipelagoBot` zip from the Releases page and unzip it anywhere.
2. Copy `appsettings.example.json` to `appsettings.json` (same folder as the exe) and fill it in:
   - **Twitch**: create an app at [dev.twitch.tv/console](https://dev.twitch.tv/console/apps) (category: Chat Bot, OAuth redirect URL: `http://localhost:3000/callback`, client type: confidential). Put its Client ID and Client Secret in the config, plus your Twitch username and channel.
   - **Obs**: in OBS, Tools → WebSocket Server Settings → enable, copy the password. Default port is 4455.
   - **Archipelago**: see the OBS scene setup below.
3. Run `ArchipelagoBot.exe`. On first start a browser window opens to authorize the bot with Twitch; the token is stored in Windows Credential Manager (run with `ArchipelagoBot.exe twitchreauth` to clear it).

### OBS scene setup

The module expects a two-layered scene layout:

- Per runner, a self-contained shared scene (e.g. `AP_SHARED_P2`) containing their capture and a text source for their name (e.g. `AP_P2_NAME`).
- Per runner, a broadcast scene (e.g. `AP_POV_P2`) that nests the shared scenes in some arrangement (their POV big, the others small).
- Your own POV broadcast scene (e.g. `AP_POV_Me`); your own name text is static, so it needs no slot.

The scene and source names are free-form — whatever you use, mirror it in the `Archipelago` section of `appsettings.json`. Each slot's `Color` is the reward button color, ideally matching the runner's frame color in OBS.

### Running a session

- `!ap setup <name1> [name2] [name3]` (broadcaster only) — writes the runner names into the name text sources and creates/enables one 1-point "POV: name" reward per runner, plus your own.
- Viewers redeem a POV reward → the program output switches to that runner's broadcast scene. The redemption is fulfilled on success and refunded if the scene switch fails.
- `!ap stop` — disables the POV rewards after the session. They are also disabled on bot shutdown.

Commands are deliberately silent in chat; outcomes are visible in OBS and the rewards list. Check `log.txt` next to the exe when something seems to not react.

Reward IDs are persisted in `data/archipelago.json` next to the exe and reused across setups (rewards are retitled rather than recreated). This keeps the bot clear of Twitch's 50-rewards-per-channel cap — don't delete that file casually.

## Using the modules in your own bot

Reference `HakuStream.Kit` and the modules you want, then compose a host:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTwitch(builder.Configuration);
builder.Services.AddArchipelago(builder.Configuration);

var host = builder.Build();

var auth = host.Services.GetRequiredService<TwitchAuthOrchestrator>();
if (!await auth.EnsureAuthenticatedAsync()) return;

await host.RunAsync();
```

`AddTwitch` registers the connection, auth, dispatchers, and registries once. Each module's `AddX` extension registers its settings, services, and its own commands/redeems — adding the module to the host is what activates it. See `samples/Archipelago.Host` for the complete picture, and `docs/adding-a-command.md` for writing commands.

## Building

```
dotnet build HakuStream.Kit.slnx
```

Requires the .NET 10 SDK on Windows.

## Conventions

- No code comments. Behavior that needs explaining is documented here or in `docs/`.
- Formatting is enforced with `dotnet format` against the repo's `.editorconfig`; CI rejects unformatted code. Run `dotnet format HakuStream.Kit.slnx` before committing.
