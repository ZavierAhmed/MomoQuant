namespace MomoQuant.Application.Options;

public sealed class BbLiquiditySweepSettings
{
    public const string SectionName = "BbLiquiditySweep";

    public List<TradingSessionWindowSettings> Sessions { get; set; } =
    [
        new() { Name = "London", Start = "02:00", End = "05:00", Timezone = "America/New_York" },
        new() { Name = "New York AM", Start = "08:30", End = "11:30", Timezone = "America/New_York" },
        new() { Name = "New York PM", Start = "13:30", End = "15:30", Timezone = "America/New_York" }
    ];
}

public sealed class TradingSessionWindowSettings
{
    public string Name { get; set; } = string.Empty;
    public string Start { get; set; } = "00:00";
    public string End { get; set; } = "23:59";
    public string Timezone { get; set; } = "America/New_York";
}
