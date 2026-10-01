using backend.Models.Dtos;
using backend.Models.Enums;
using backend.Services;
using Microsoft.EntityFrameworkCore;

namespace backend.Tests;

/// <summary>
/// Expected behavior of UserService (the signed-in user's "profile"):
/// what the client receives and what actually ends up in the database.
/// </summary>
[Collection("postgres")]
public class UserServiceTests : ServiceTestBase
{
    public UserServiceTests(PostgresFixture postgres) : base(postgres) { }

    private UserService CreateService() => new(Context);

    [Fact]
    public async Task GetProfile_WithAKnownUser_ReturnsTheFullProfile()
    {
        var user = await SeedUserAsync(PlanType.Pro, email: "pro@test.dev", name: "Pro Test");

        var profile = await CreateService().GetProfileAsync(user.Id);

        Assert.Equal(user.Id, profile.Id);
        Assert.Equal("pro@test.dev", profile.Email);
        Assert.Equal("Pro Test", profile.Name);
        Assert.Equal(PlanType.Pro, profile.PlanType);
        Assert.Equal(user.CreatedAt, profile.CreatedAt);
    }

    [Fact]
    public async Task GetProfile_WithAnUnknownUser_ThrowsNotFound()
    {
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateService().GetProfileAsync(Guid.NewGuid()));

        Assert.Equal("User not found.", exception.Message);
    }

    [Fact]
    public async Task UpdateProfile_WithAValidName_UpdatesAndPersistsTheName()
    {
        var user = await SeedUserAsync(name: "Old Name");

        var profile = await CreateService().UpdateProfileAsync(
            user.Id,
            new UpdateProfileRequest { Name = "  New Name  " });

        // The name is trimmed before being stored.
        Assert.Equal("New Name", profile.Name);

        var storedName = await ReadAsync(db => db.Users
            .Where(u => u.Id == user.Id)
            .Select(u => u.Name)
            .SingleAsync());
        Assert.Equal("New Name", storedName);
    }

    [Fact]
    public async Task UpdateProfile_WithAnEmptyName_RefusesAndChangesNothing()
    {
        var user = await SeedUserAsync(name: "Old Name");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().UpdateProfileAsync(user.Id, new UpdateProfileRequest { Name = "   " }));

        Assert.Equal("Name cannot be empty.", exception.Message);

        var storedName = await ReadAsync(db => db.Users
            .Where(u => u.Id == user.Id)
            .Select(u => u.Name)
            .SingleAsync());
        Assert.Equal("Old Name", storedName);
    }

    [Fact]
    public async Task UpdateProfile_WithAnUnknownUser_ThrowsNotFound()
    {
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateService().UpdateProfileAsync(Guid.NewGuid(), new UpdateProfileRequest { Name = "Someone" }));

        Assert.Equal("User not found.", exception.Message);
    }
}
