namespace HakuStream.Kit.Twitch;

public sealed class CommandContext
{
    public required string CommandName { get; init; }

    public required string[] Args { get; init; }

    public required string User { get; init; }

    public required string UserId { get; init; }

    public required string Channel { get; init; }

    public required bool IsModerator { get; init; }

    public required bool IsBroadcaster { get; init; }

    public required PermissionLevel Permission { get; init; }

    public required Func<string, Task> Reply { get; init; }

    public required Func<string, Task> Send { get; init; }

    public Task ReplyAsync(string message)
    {
        return Reply(message);
    }

    public Task SendAsync(string message)
    {
        return Send(message);
    }

    public string? Arg(int index)
    {
        return index >= 0 && index < Args.Length ? Args[index] : null;
    }
}
