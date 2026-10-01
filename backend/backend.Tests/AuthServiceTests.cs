using System.IdentityModel.Tokens.Jwt;
using backend.Models.Dtos;
using backend.Models.Enums;
using backend.Services;
using Microsoft.EntityFrameworkCore;

namespace backend.Tests;

/// <summary>
/// Expected behavior of AuthService: what an API client can observe
/// (returned payload, issued JWT, data actually stored in the database), never how
/// it is implemented internally.
/// </summary>
[Collection("postgres")]
public class AuthServiceTests : ServiceTestBase
{
    public AuthServiceTests(PostgresFixture postgres) : base(postgres) { }

    private AuthService CreateService() => new(Context, Configuration);

    // Registration

    [Fact]
    public async Task Register_WithAnAvailableEmail_CreatesAFreeAccountAndReturnsAToken()
    {
        var response = await CreateService().Register(new RegisterRequest
        {
            Email = "alice@test.dev",
            Name = "Alice",
            Password = KnownPassword
        });

        Assert.Equal("alice@test.dev", response.Email);
        Assert.Equal("Alice", response.Name);
        Assert.Equal(PlanType.Free, response.PlanType);
        Assert.False(string.IsNullOrWhiteSpace(response.Token));

        var storedUser = await ReadAsync(db => db.Users.SingleAsync(u => u.Email == "alice@test.dev"));
        Assert.Equal("Alice", storedUser.Name);
        Assert.Equal(PlanType.Free, storedUser.PlanType);
    }

    [Fact]
    public async Task Register_WithAnEmailAlreadyInUse_RefusesCreation()
    {
        var service = CreateService();
        var request = new RegisterRequest { Email = "alice@test.dev", Name = "Alice", Password = KnownPassword };
        await service.Register(request);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.Register(new RegisterRequest { Email = "alice@test.dev", Name = "Alice Duplicate", Password = KnownPassword }));

        Assert.Equal("This email is already in use.", exception.Message);
        Assert.Equal(1, await ReadAsync(db => db.Users.CountAsync()));
    }

    [Fact]
    public async Task Register_NeverStoresThePasswordInPlainText()
    {
        await CreateService().Register(new RegisterRequest
        {
            Email = "alice@test.dev",
            Name = "Alice",
            Password = KnownPassword
        });

        var storedUser = await ReadAsync(db => db.Users.SingleAsync(u => u.Email == "alice@test.dev"));

        Assert.NotEqual(KnownPassword, storedUser.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(KnownPassword, storedUser.PasswordHash));
    }

    [Fact]
    public async Task Register_GeneratesAJwtCarryingTheAccountIdentityAndPlan()
    {
        var response = await CreateService().Register(new RegisterRequest
        {
            Email = "alice@test.dev",
            Name = "Alice",
            Password = KnownPassword
        });

        var user = await ReadAsync(db => db.Users.SingleAsync(u => u.Email == "alice@test.dev"));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);

        Assert.Equal(user.Id.ToString(), jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("alice@test.dev", jwt.Claims.Single(c => c.Type == "email").Value);
        Assert.Equal("Free", jwt.Claims.Single(c => c.Type == "planType").Value);

        Assert.Equal("backend-tests", jwt.Issuer);
        Assert.Contains("backend-tests-clients", jwt.Audiences);

        // The token must stay valid for about 7 days (margin for the time the test runs).
        Assert.InRange((jwt.ValidTo - DateTime.UtcNow).TotalDays, 6.9, 7.1);
    }

    // Login

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsATokenAndTheProfile()
    {
        var user = await SeedUserAsync(PlanType.Pro, email: "pro@test.dev", name: "Pro Test");

        var response = await CreateService().Login(new LoginRequest
        {
            Email = "pro@test.dev",
            Password = KnownPassword
        });

        Assert.False(string.IsNullOrWhiteSpace(response.Token));
        Assert.Equal("pro@test.dev", response.Email);
        Assert.Equal("Pro Test", response.Name);
        Assert.Equal(user.PlanType, response.PlanType);
    }

    [Fact]
    public async Task Login_WithAWrongPassword_RefusesWithoutRevealingWhatIsWrong()
    {
        await SeedUserAsync(email: "alice@test.dev");

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateService().Login(new LoginRequest { Email = "alice@test.dev", Password = "WrongPassword!123" }));

        // One single message whatever the reason: never reveal whether the email exists.
        Assert.Equal("Incorrect email or password.", exception.Message);
    }

    [Fact]
    public async Task Login_WithAnUnknownEmail_RefusesWithTheSameMessageAsAWrongPassword()
    {
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateService().Login(new LoginRequest { Email = "unknown@test.dev", Password = KnownPassword }));

        Assert.Equal("Incorrect email or password.", exception.Message);
    }
}
