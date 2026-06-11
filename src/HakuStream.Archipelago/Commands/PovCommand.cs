using HakuStream.Kit.Twitch;
using Microsoft.Extensions.Logging;

namespace HakuStream.Archipelago.Commands;

[Command("ap")]
[RequirePermission(Level = PermissionLevel.Broadcaster)]
public sealed class PovCommand(
    ArchipelagoPovService pov,
    ArchipelagoObsSetupService obsSetup,
    ILogger<PovCommand> logger) : IChatCommand
{
    public async Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        switch (ctx.Arg(0)?.ToLowerInvariant())
        {
            case "setup" when ctx.Args.Length > 1:
                {
                    var names = ctx.Args[1..];
                    await obsSetup.SetupScenesAsync(names.Length + 1, ct);
                    await pov.SetupAsync(names, ct);
                    break;
                }

            case "stop":
                await pov.DisableRewardsAsync(ct);
                break;

            default:
                logger.LogWarning(
                    "!ap: bad invocation '{Args}'. Usage: !ap setup <name1> [name2] ... | !ap stop",
                    string.Join(' ', ctx.Args));
                break;
        }
    }
}
