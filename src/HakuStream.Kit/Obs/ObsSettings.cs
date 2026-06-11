namespace HakuStream.Kit.Obs;

public sealed class ObsSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 4455;
    public string? Password { get; set; }
}
