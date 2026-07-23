using MomoQuant.Shared.Constants;

namespace MomoQuant.UnitTests;

public class AppConstantsTests
{
    [Fact]
    public void ApplicationName_IsConfigured()
    {
        Assert.Equal("MOMO Quant", AppConstants.ApplicationName);
    }
}
