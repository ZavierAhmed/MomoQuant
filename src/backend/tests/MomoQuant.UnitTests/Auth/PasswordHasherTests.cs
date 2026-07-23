using MomoQuant.Application.Abstractions;
using MomoQuant.Infrastructure.Security;

namespace MomoQuant.UnitTests.Auth;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_AndVerify_ReturnsTrueForCorrectPassword()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("Admin123!");

        Assert.NotEqual("Admin123!", hash);
        Assert.True(hasher.Verify("Admin123!", hash));
        Assert.False(hasher.Verify("WrongPassword!", hash));
    }
}
