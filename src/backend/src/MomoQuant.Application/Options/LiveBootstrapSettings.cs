namespace MomoQuant.Application.Options;

public sealed class LiveBootstrapSettings
{
    public int WarmupCandles { get; set; } = 300;

    public int MaxBootstrapCandles { get; set; } = 500;

    public bool AllowAutoBootstrap { get; set; } = true;
}
