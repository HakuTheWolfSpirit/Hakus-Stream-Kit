using HakuStream.Kit.Twitch.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HakuStream.Kit.Twitch;

public sealed record ParsedCommand(
    string MessageId,
    string Name,
    string[] Args,
    string User,
    string UserId,
    string Channel,
    bool IsModerator,
    bool IsBroadcaster);

public sealed class CommandDispatcher(
    IServiceProvider services,
    CommandRegistry registry,
    CooldownService cooldowns,
    TwitchChatWriter writer,
    ILogger<CommandDispatcher> logger)
{
    public async Task DispatchAsync(ParsedCommand cmd, CancellationToken ct)
    {
        if (!registry.TryGet(cmd.Name, out var info)) return;

        var permission = cmd.IsBroadcaster ? PermissionLevel.Broadcaster
            : cmd.IsModerator ? PermissionLevel.Moderator
            : PermissionLevel.Everyone;

        if (permission < info.RequiredPermission)
        {
            await writer.SendReplyAsync(cmd.MessageId, "You can't use that command.");
            return;
        }

        if (info.Cooldowns.Count > 0)
        {
            var cooldown = cooldowns.Check(info.HandlerType, cmd.UserId, info.Cooldowns);
            if (cooldown.Active) return;
        }

        var ctx = new CommandContext
        {
            CommandName = cmd.Name,
            Args = cmd.Args,
            User = cmd.User,
            UserId = cmd.UserId,
            Channel = cmd.Channel,
            IsModerator = cmd.IsModerator,
            IsBroadcaster = cmd.IsBroadcaster,
            Permission = permission,
            Reply = message => writer.SendReplyAsync(cmd.MessageId, message),
            Send = message => writer.SendMessageAsync(message)
        };

        await using var scope = services.CreateAsyncScope();
        var command = (IChatCommand)scope.ServiceProvider.GetRequiredService(info.HandlerType);

        try
        {
            await command.RunAsync(ctx, ct);
            cooldowns.Record(info.HandlerType, cmd.UserId, info.Cooldowns);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Command '{Command}' from {User} threw", cmd.Name, cmd.User);
        }
    }
}
