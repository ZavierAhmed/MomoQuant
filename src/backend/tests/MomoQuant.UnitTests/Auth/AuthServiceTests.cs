using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Auth;
using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Identity;

namespace MomoQuant.UnitTests.Auth;

public class AuthServiceTests
{
    [Fact]
    public async Task LoginAsync_RejectsInactiveUser()
    {
        var user = new User
        {
            Id = 1,
            Email = "trader@momoquant.local",
            PasswordHash = "hash",
            Role = UserRole.Trader,
            IsActive = false
        };

        var userRepository = new Mock<IUserRepository>();
        userRepository.Setup(repo => repo.GetByEmailForLoginAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(hasher => hasher.Verify("Admin123!", user.PasswordHash)).Returns(true);

        var service = new AuthService(
            userRepository.Object,
            passwordHasher.Object,
            Mock.Of<IJwtTokenService>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>());

        var result = await service.LoginAsync(
            new LoginRequest { Email = user.Email, Password = "Admin123!" },
            null,
            null);

        Assert.False(result.Succeeded);
        Assert.Equal("Account is inactive.", result.ErrorMessage);
    }

    [Fact]
    public async Task LoginAsync_ReturnsGenericMessageForInvalidCredentials()
    {
        var userRepository = new Mock<IUserRepository>();
        userRepository.Setup(repo => repo.GetByEmailForLoginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var service = new AuthService(
            userRepository.Object,
            Mock.Of<IPasswordHasher>(),
            Mock.Of<IJwtTokenService>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>());

        var result = await service.LoginAsync(
            new LoginRequest { Email = "missing@momoquant.local", Password = "Admin123!" },
            null,
            null);

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid email or password.", result.ErrorMessage);
    }
}
