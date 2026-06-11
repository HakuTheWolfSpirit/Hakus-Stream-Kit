# How to add a chat command

Adding a command is one file. There is no list to register it in — it's discovered at startup
by its `[Command]` attribute when the module's assembly is passed to `AddChatCommands`.

## 1. Create the class

Drop a `.cs` file in your module:

```csharp
using HakuStream.Kit.Twitch;

namespace MyModule.Commands;

[Command("hype")]
public sealed class HypeCommand : IChatCommand
{
    public Task RunAsync(CommandContext ctx, CancellationToken ct) =>
        ctx.SendAsync("HYPE!!!");
}
```

Make sure your module's `AddX` extension calls
`services.AddChatCommands(typeof(YourModule).Assembly);` once — every `[Command]` class in the
assembly is then picked up. Run it with `!hype`.

## 2. What `ctx` gives you

`CommandContext` (see `src/HakuStream.Kit/Twitch/CommandContext.cs`) is everything about the message:

- `ctx.Arg(0)`, `ctx.Args` — the words after the command (`!so bob` → `Arg(0) == "bob"`).
- `ctx.User`, `ctx.UserId`, `ctx.Channel`, `ctx.IsModerator`, `ctx.IsBroadcaster`, `ctx.Permission`.
- `ctx.ReplyAsync("...")` — replies to the user (threaded `@user ↪`).
- `ctx.SendAsync("...")` — plain message to the channel.

## 3. Optional attributes

```csharp
[Command("so")]
[Command("shoutout")]
[RequirePermission(Level = PermissionLevel.Moderator)]
[Cooldown(Scope = CooldownScope.PerUser, Seconds = 30)]
```

Repeat `[Command]` for aliases. `RequirePermission` gates to mods or the broadcaster.
`Cooldown` rate-limits per user or per command.

## 4. Need a service? Put it in the constructor

DI fills constructor parameters. Available out of the box: `TwitchApiClient`, `TokenManager`,
`TwitchSettings`, `IEventBus`, `RewardManager`, `ObsClient` (when `AddObs` is registered), and
any `ILogger<T>`.

## Worked example

`src/HakuStream.Archipelago/Commands/PovCommand.cs` — permission gating, argument handling, and
an injected module service.
