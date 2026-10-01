using backend.Data;
using backend.Models;
using backend.Models.Dtos;
using backend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace backend.Tests;

/// <summary>
/// Shared base class for service tests.
///
/// Two ideas shape this class:
///
/// 1. Tests run against a REAL PostgreSQL (Testcontainers container), never against a
///    simulated provider: cascades, the ON DELETE RESTRICT on Answer.FieldId and TPH
///    therefore behave exactly as they do in production.
///
/// 2. Every test works in ITS OWN database ("test_&lt;guid&gt;"), created by EF from the
///    model. No test can pollute another one, and no cleanup is needed: the database
///    goes away with the container at the end of the run.
/// </summary>
public abstract class ServiceTestBase : IAsyncLifetime
{
    /// <summary>Password of the users seeded by the tests.</summary>
    protected const string KnownPassword = "TestPassword!123";

    /// <summary>
    /// BCrypt hash computed once for the whole suite: BCrypt is slow by design (~100 ms),
    /// so there is no point recomputing it for every seeded user of every test.
    /// </summary>
    private static readonly string KnownPasswordHash = BCrypt.Net.BCrypt.HashPassword(KnownPassword);

    /// <summary>
    /// Fixed date instead of "now": PostgreSQL timestamp-with-time-zone columns round to the
    /// microsecond, which would make a strict equality on DateTime.UtcNow fragile. A date
    /// without fractional seconds does not move when stored.
    /// </summary>
    private static readonly DateTime FixedCreatedAt = new(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

    private readonly PostgresFixture _postgres;
    private string _connectionString = string.Empty;

    /// <summary>EF context bound to this test's own database (used for arranging).</summary>
    protected AppDbContext Context { get; private set; } = null!;

    /// <summary>In-memory configuration (JWT, Stripe): no appsettings file needed.</summary>
    protected IConfiguration Configuration { get; private set; } = null!;

    protected ServiceTestBase(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _connectionString = new NpgsqlConnectionStringBuilder(_postgres.Container.GetConnectionString())
        {
            Database = $"test_{Guid.NewGuid():N}"
        }.ConnectionString;

        Context = CreateContext();

        // EnsureCreated builds the schema from the EF model.
        // Migrations, on the other hand, contain production-specific PostgreSQL SQL and are
        // not needed here: what we test is the behavior of the services.
        await Context.Database.EnsureCreatedAsync();

        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-jwt-key-long-enough-for-hmac-sha256-0123456789",
                ["Jwt:Issuer"] = "backend-tests",
                ["Jwt:Audience"] = "backend-tests-clients",
                ["Stripe:PriceId"] = "price_test_123",
                ["Stripe:WebhookSecret"] = StripeWebhookTestHelper.WebhookSecret
            })
            .Build();
    }

    public Task DisposeAsync()
    {
        Context.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>FRESH context on the same database: used to re-read what was actually persisted.</summary>
    protected AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString)
            .Options);

    /// <summary>
    /// Runs a read on a fresh context. This checks the data written to the database, not just
    /// the state kept in the arranging context's change tracker.
    /// </summary>
    protected async Task<T> ReadAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        await using var db = CreateContext();
        return await read(db);
    }

    // Arrange helpers

    protected async Task<User> SeedUserAsync(
        PlanType plan = PlanType.Free,
        string email = "user@test.dev",
        string name = "Test User")
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Name = name,
            PasswordHash = KnownPasswordHash,
            PlanType = plan,
            CreatedAt = FixedCreatedAt
        };

        Context.Users.Add(user);
        await Context.SaveChangesAsync();
        return user;
    }

    protected async Task<Form> SeedFormAsync(
        Guid userId,
        string title = "Test form",
        string fieldLabel = "Name",
        bool fieldRequired = false,
        DateTime? createdAt = null)
    {
        var form = new Form
        {
            UserId = userId,
            Title = title,
            PublicUrlSlug = $"slug-{Guid.NewGuid():N}",
            CreatedAt = createdAt ?? DateTime.UtcNow
        };

        form.AddField(new TextField
        {
            Label = fieldLabel,
            Type = FieldType.Text,
            IsRequired = fieldRequired,
            MaxLength = 500,
            Order = 0
        });

        Context.Forms.Add(form);
        await Context.SaveChangesAsync();
        return form;
    }

    protected async Task<FormResponse> SeedResponseAsync(
        Guid formId,
        (Guid FieldId, string Value)[] answers,
        DateTime? submittedAt = null)
    {
        var response = new FormResponse
        {
            FormId = formId,
            SubmittedAt = submittedAt ?? DateTime.UtcNow
        };

        foreach (var (fieldId, value) in answers)
            response.Answers.Add(new Answer { FieldId = fieldId, Value = value });

        Context.FormResponses.Add(response);
        await Context.SaveChangesAsync();
        return response;
    }

    protected async Task<Subscription> SeedSubscriptionAsync(
        Guid userId,
        string stripeSubscriptionId,
        SubscriptionStatus status = SubscriptionStatus.Active)
    {
        var subscription = new Subscription
        {
            UserId = userId,
            StripeCustomerId = "cus_test_123",
            StripeSubscriptionId = stripeSubscriptionId,
            Status = status,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1)
        };

        Context.Subscriptions.Add(subscription);
        await Context.SaveChangesAsync();
        return subscription;
    }

    // DTO factories (arrange)

    protected static CreateFormRequest FormRequest(string title, params FieldRequestDto[] fields) =>
        new() { Title = title, Fields = fields.ToList() };

    protected static FieldRequestDto TextFieldDto(
        string label, bool isRequired = false, int? maxLength = null, int order = 0) =>
        new() { Type = "text", Label = label, IsRequired = isRequired, MaxLength = maxLength, Order = order };

    protected static FieldRequestDto ChoiceFieldDto(
        string label, IEnumerable<string> options, bool isRequired = false, int order = 0) =>
        new() { Type = "choice", Label = label, IsRequired = isRequired, Options = options.ToList(), Order = order };

    protected static FieldRequestDto NumberFieldDto(
        string label, decimal? min = null, decimal? max = null, bool isRequired = false, int order = 0) =>
        new() { Type = "number", Label = label, IsRequired = isRequired, Min = min, Max = max, Order = order };

    protected static SubmitResponseRequest ResponseRequest(params (Guid FieldId, string Value)[] answers) =>
        new()
        {
            Answers = answers
                .Select(a => new AnswerRequestDto { FieldId = a.FieldId, Value = a.Value })
                .ToList()
        };
}
