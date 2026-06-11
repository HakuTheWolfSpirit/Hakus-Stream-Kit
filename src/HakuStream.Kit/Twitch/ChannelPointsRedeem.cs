namespace HakuStream.Kit.Twitch;

public interface IChannelPointsRedeem
{
    Task RunAsync(RedeemContext ctx, CancellationToken ct);
}
