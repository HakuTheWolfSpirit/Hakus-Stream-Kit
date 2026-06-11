namespace HakuStream.Kit.Twitch;

public sealed class RedeemContext
{
    public required string RedemptionId { get; init; }

    public required string RewardId { get; init; }

    public required string RewardTitle { get; init; }

    public required int Cost { get; init; }

    public required string User { get; init; }

    public required string UserId { get; init; }

    public string? UserInput { get; init; }

    public required Func<string, Task> Send { get; init; }

    public required Func<CancellationToken, Task> Fulfill { get; init; }

    public required Func<CancellationToken, Task> Cancel { get; init; }

    public Task SendAsync(string message) => Send(message);

    public Task FulfillAsync(CancellationToken ct = default) => Fulfill(ct);

    public Task CancelAsync(CancellationToken ct = default) => Cancel(ct);
}
