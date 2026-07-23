using MomoQuant.Application.TradingSystems;

namespace MomoQuant.UnitTests.TradingSystems;

public class TradingSystemServiceTests
{
    [Fact]
    public void GetSystems_ReturnsSkSystem()
    {
        var service = new TradingSystemService();

        var systems = service.GetSystems();

        var sk = Assert.Single(systems);
        Assert.Equal("SK_SYSTEM", sk.Code);
        Assert.True(sk.AnalysisOnly);
        Assert.Contains("15m", sk.SupportedPrimaryTimeframes);
        Assert.Contains("4h", sk.SupportedHigherTimeframes);
    }
}
