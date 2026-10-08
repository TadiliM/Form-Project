using System.Net;
using System.Text;
using backend.Models.Enums;
using backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;

namespace backend.Tests;

/// <summary>
/// Expected behavior of SubscriptionsService (the paid Pro subscription through Stripe).
///
/// Webhooks are built with real signed payloads (HMAC-SHA256), just like the ones Stripe
/// sends: the service is therefore tested the way it runs in production, without depending
/// on the Stripe API itself (impossible and pointless in a test suite).
/// </summary>
[Collection("postgres")]
public class SubscriptionsServiceTests : ServiceTestBase
{
    public SubscriptionsServiceTests(PostgresFixture postgres) : base(postgres) { }

    private SubscriptionsService CreateService() => new(Context, Configuration);

    // Creating a checkout session

    [Fact]
    public async Task CreateCheckoutSession_WithAnUnknownUser_ThrowsNotFound()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CreateCheckoutSessionAsync(Guid.NewGuid()));

        Assert.Equal("User not found.", exception.Message);
    }

    // Return URLs: configuration-driven, so a deployment does not send buyers to localhost.

    [Fact]
    public void BuildCheckoutOptions_WithoutConfiguredUrls_FallsBackToLocalhost()
    {
        var options = CreateService().BuildCheckoutOptions(Guid.NewGuid());

        Assert.Equal(SubscriptionsService.DefaultSuccessUrl, options.SuccessUrl);
        Assert.Equal(SubscriptionsService.DefaultCancelUrl, options.CancelUrl);
    }

    [Fact]
    public void BuildCheckoutOptions_WithConfiguredUrls_UsesThem()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:PriceId"] = "price_test_123",
                ["Stripe:SuccessUrl"] = "https://app.example.com/success",
                ["Stripe:CancelUrl"] = "https://app.example.com/cancel"
            })
            .Build();

        var options = new SubscriptionsService(Context, configuration).BuildCheckoutOptions(Guid.NewGuid());

        Assert.Equal("https://app.example.com/success", options.SuccessUrl);
        Assert.Equal("https://app.example.com/cancel", options.CancelUrl);
    }

    [Fact]
    public void BuildCheckoutOptions_CarriesThePriceAndTheUserReference()
    {
        var userId = Guid.NewGuid();

        var options = CreateService().BuildCheckoutOptions(userId);

        Assert.Equal("subscription", options.Mode);
        Assert.Equal("price_test_123", Assert.Single(options.LineItems).Price);
        Assert.Equal(userId.ToString(), options.ClientReferenceId);
    }

    // Stripe webhooks

    [Fact]
    public async Task HandleWebhook_WithAnInvalidSignature_RejectsTheEvent()
    {
        var user = await SeedUserAsync();
        var (payload, _) = StripeWebhookTestHelper.Sign(CheckoutCompletedEvent(user.Id));

        await Assert.ThrowsAsync<StripeException>(
            () => CreateService().HandleWebhookAsync(payload, StripeWebhookTestHelper.InvalidSignature()));

        Assert.Empty(await ReadAsync(db => db.Subscriptions.ToListAsync()));
    }

    [Fact]
    public async Task HandleWebhook_CheckoutCompleted_CreatesTheSubscriptionAndUpgradesTheUserToPro()
    {
        var user = await SeedUserAsync(PlanType.Free);
        var (payload, signature) = StripeWebhookTestHelper.Sign(CheckoutCompletedEvent(user.Id));

        await CreateService().HandleWebhookAsync(payload, signature);

        var subscription = await ReadAsync(db => db.Subscriptions.SingleAsync(s => s.UserId == user.Id));
        Assert.Equal("cus_test_123", subscription.StripeCustomerId);
        Assert.Equal("sub_test_123", subscription.StripeSubscriptionId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);

        // The Stripe API is not configured in tests, so the service falls back to a
        // one-month period: that is the observable behavior when Stripe is unreachable.
        Assert.InRange(subscription.CurrentPeriodEnd, DateTime.UtcNow.AddDays(25), DateTime.UtcNow.AddDays(35));

        var storedUser = await ReadAsync(db => db.Users.SingleAsync(u => u.Id == user.Id));
        Assert.Equal(PlanType.Pro, storedUser.PlanType);
    }

    [Fact]
    public async Task HandleWebhook_CheckoutCompletedDeliveredTwice_DoesNotCreateADuplicate()
    {
        var user = await SeedUserAsync(PlanType.Free);
        var (payload, signature) = StripeWebhookTestHelper.Sign(CheckoutCompletedEvent(user.Id));
        var service = CreateService();

        await service.HandleWebhookAsync(payload, signature);
        await service.HandleWebhookAsync(payload, signature);

        Assert.Equal(1, await ReadAsync(db => db.Subscriptions.CountAsync(s => s.UserId == user.Id)));
    }

    [Fact]
    public async Task HandleWebhook_CheckoutWithoutAValidUserReference_IgnoresTheEvent()
    {
        var user = await SeedUserAsync();
        var (payload, signature) = StripeWebhookTestHelper.Sign(CheckoutCompletedEvent(userId: null));

        await CreateService().HandleWebhookAsync(payload, signature);

        Assert.Empty(await ReadAsync(db => db.Subscriptions.ToListAsync()));
        Assert.Equal(
            PlanType.Free,
            await ReadAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.PlanType).SingleAsync()));
    }

    [Fact]
    public async Task HandleWebhook_SubscriptionDeleted_CancelsTheSubscriptionAndMovesTheUserBackToFree()
    {
        var user = await SeedUserAsync(PlanType.Pro);
        await SeedSubscriptionAsync(user.Id, stripeSubscriptionId: "sub_test_123");
        var (payload, signature) = StripeWebhookTestHelper.Sign(SubscriptionDeletedEvent("sub_test_123"));

        await CreateService().HandleWebhookAsync(payload, signature);

        var subscription = await ReadAsync(db => db.Subscriptions.SingleAsync(s => s.UserId == user.Id));
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);

        var storedUser = await ReadAsync(db => db.Users.SingleAsync(u => u.Id == user.Id));
        Assert.Equal(PlanType.Free, storedUser.PlanType);
    }

    [Fact]
    public async Task HandleWebhook_SubscriptionDeletedButUnknownInDatabase_HasNoEffect()
    {
        var user = await SeedUserAsync(PlanType.Pro);
        var (payload, signature) = StripeWebhookTestHelper.Sign(SubscriptionDeletedEvent("sub_unknown"));

        await CreateService().HandleWebhookAsync(payload, signature);

        Assert.Empty(await ReadAsync(db => db.Subscriptions.ToListAsync()));
        Assert.Equal(
            PlanType.Pro,
            await ReadAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.PlanType).SingleAsync()));
    }

    [Fact]
    public async Task HandleWebhook_UnhandledEvent_HasNoEffect()
    {
        var user = await SeedUserAsync(PlanType.Free);
        var (payload, signature) = StripeWebhookTestHelper.Sign(UnhandledEvent());

        await CreateService().HandleWebhookAsync(payload, signature);

        Assert.Empty(await ReadAsync(db => db.Subscriptions.ToListAsync()));
        Assert.Equal(
            PlanType.Free,
            await ReadAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.PlanType).SingleAsync()));
    }

    // Confirming the payment when the buyer comes back from Stripe

    [Fact]
    public void DefaultSuccessUrl_CarriesTheCheckoutSessionPlaceholder()
    {
        // The success page needs the session id to confirm the payment itself; without the
        // placeholder Stripe would redirect to a page that has nothing to verify.
        Assert.Contains("{CHECKOUT_SESSION_ID}", SubscriptionsService.DefaultSuccessUrl);
    }

    [Fact]
    public async Task ConfirmCheckoutSession_WithoutASessionId_ThrowsAnExplicitException()
    {
        var user = await SeedUserAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().ConfirmCheckoutSessionAsync(user.Id, "   "));

        Assert.Equal("Missing checkout session id.", exception.Message);
    }

    [Fact]
    public void EnsureSessionBelongsToUser_WithTheBuyer_DoesNotThrow()
    {
        var userId = Guid.NewGuid();

        SubscriptionsService.EnsureSessionBelongsToUser(
            new Session { ClientReferenceId = userId.ToString() }, userId);
    }

    [Fact]
    public void EnsureSessionBelongsToUser_WithAnotherUsersSession_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SubscriptionsService.EnsureSessionBelongsToUser(
                new Session { ClientReferenceId = Guid.NewGuid().ToString() }, Guid.NewGuid()));

        Assert.Equal("This checkout session belongs to another user.", exception.Message);
    }

    [Fact]
    public void EnsureSessionBelongsToUser_WithoutAReference_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SubscriptionsService.EnsureSessionBelongsToUser(new Session(), Guid.NewGuid()));

        Assert.Equal("This checkout session is not linked to a user.", exception.Message);
    }

    [Fact]
    public async Task ActivateSubscription_WithASessionWithoutSubscription_IsIgnored()
    {
        // A checkout that never became a subscription (one-off payment, abandoned session)
        // must not hand out a Pro plan: that is how a Free account used to flip to Pro with
        // an empty StripeSubscriptionId in the database.
        var user = await SeedUserAsync(PlanType.Free);

        await CreateService().ActivateSubscriptionAsync(new Session
        {
            ClientReferenceId = user.Id.ToString(),
            SubscriptionId = string.Empty
        });

        Assert.Empty(await ReadAsync(db => db.Subscriptions.ToListAsync()));
        Assert.Equal(
            PlanType.Free,
            await ReadAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.PlanType).SingleAsync()));
    }

    [Fact]
    public async Task ActivateSubscription_WithASubscriptionSession_UpgradesTheUserToPro()
    {
        // The exact path the confirm endpoint uses: the plan flips with no webhook involved.
        var user = await SeedUserAsync(PlanType.Free);

        await CreateService().ActivateSubscriptionAsync(new Session
        {
            ClientReferenceId = user.Id.ToString(),
            SubscriptionId = "sub_test_confirm",
            CustomerId = "cus_test_confirm"
        });

        var subscription = await ReadAsync(db => db.Subscriptions.SingleAsync(s => s.UserId == user.Id));
        Assert.Equal("sub_test_confirm", subscription.StripeSubscriptionId);
        Assert.Equal("cus_test_confirm", subscription.StripeCustomerId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(
            PlanType.Pro,
            await ReadAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.PlanType).SingleAsync()));
    }

    // User-side cancellation

    [Fact]
    public async Task CancelSubscription_WithoutAnActiveSubscription_ThrowsAnExplicitException()
    {
        var user = await SeedUserAsync(PlanType.Free);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CancelSubscriptionAsync(user.Id));

        Assert.Equal("No active subscription to cancel.", exception.Message);
    }

    [Fact]
    public async Task CancelSubscription_SubscriptionWithoutAStripeId_ThrowsAnExplicitException()
    {
        var user = await SeedUserAsync(PlanType.Pro);
        await SeedSubscriptionAsync(user.Id, stripeSubscriptionId: string.Empty);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CancelSubscriptionAsync(user.Id));

        Assert.Equal("Stripe subscription not found.", exception.Message);
    }

    [Fact]
    public async Task CancelSubscription_WhenStripeIsUnreachable_FailsWithoutChangingTheLocalSubscription()
    {
        var user = await SeedUserAsync(PlanType.Pro);
        await SeedSubscriptionAsync(user.Id, stripeSubscriptionId: "sub_test_123");

        // No Stripe API key is configured in tests, so the call to the real Stripe fails.
        await Assert.ThrowsAsync<StripeException>(() => CreateService().CancelSubscriptionAsync(user.Id));

        var subscription = await ReadAsync(db => db.Subscriptions.SingleAsync(s => s.UserId == user.Id));
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public async Task CancelSubscription_WhenStripeAccepts_CancelsLocallyWithoutWaitingForTheWebhook()
    {
        // The customer.subscription.deleted webhook cannot reach a local API, so the call
        // itself has to update the local state: otherwise the plan stays Pro after
        // cancelling, even after signing out and back in.
        var user = await SeedUserAsync(PlanType.Pro);
        await SeedSubscriptionAsync(user.Id, stripeSubscriptionId: "sub_test_123");

        using var stripe = new StripeApiStub(request =>
            request.Method == HttpMethod.Delete
                ? StripeJson(SubscriptionJson("sub_test_123", "canceled"))
                : StripeJson(SubscriptionJson("sub_test_123", "active")));

        await CreateService().CancelSubscriptionAsync(user.Id);

        Assert.Contains("DELETE", stripe.Handler.Methods);

        var subscription = await ReadAsync(db => db.Subscriptions.SingleAsync(s => s.UserId == user.Id));
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Equal(
            PlanType.Free,
            await ReadAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.PlanType).SingleAsync()));
    }

    [Fact]
    public async Task CancelSubscription_CalledTwice_StopsAtTheLocalStateWithoutHittingStripeAgain()
    {
        // The second click used to reach Stripe with an already-cancelled subscription and
        // answer "Error while cancelling"; it must now stop at the local state.
        var user = await SeedUserAsync(PlanType.Pro);
        await SeedSubscriptionAsync(user.Id, stripeSubscriptionId: "sub_test_123");

        using var stripe = new StripeApiStub(request =>
            request.Method == HttpMethod.Delete
                ? StripeJson(SubscriptionJson("sub_test_123", "canceled"))
                : StripeJson(SubscriptionJson("sub_test_123", "active")));

        var service = CreateService();
        await service.CancelSubscriptionAsync(user.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CancelSubscriptionAsync(user.Id));

        Assert.Equal("No active subscription to cancel.", exception.Message);
        Assert.Equal(1, stripe.Handler.Methods.Count(method => method == "DELETE"));
    }

    [Fact]
    public async Task CancelSubscription_AlreadyCancelledAtStripe_MovesTheUserBackToFreeAnyway()
    {
        // The stuck state this fixes: Stripe cancelled the subscription, the local row was
        // left Active (a lost webhook), so the plan stayed Pro and cancelling again failed.
        var user = await SeedUserAsync(PlanType.Pro);
        await SeedSubscriptionAsync(user.Id, stripeSubscriptionId: "sub_test_123");

        // Stripe's real answer to a second cancellation: an error, which used to surface as
        // "Error while cancelling" and leave the account on Pro.
        using var stripe = new StripeApiStub(request =>
            request.Method == HttpMethod.Delete
                ? StripeError(HttpStatusCode.BadRequest, "This subscription has been canceled and cannot be canceled again.")
                : StripeJson(SubscriptionJson("sub_test_123", "canceled")));

        await CreateService().CancelSubscriptionAsync(user.Id);

        // No point asking Stripe to cancel twice: the DELETE is skipped.
        Assert.DoesNotContain("DELETE", stripe.Handler.Methods);

        var subscription = await ReadAsync(db => db.Subscriptions.SingleAsync(s => s.UserId == user.Id));
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Equal(
            PlanType.Free,
            await ReadAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.PlanType).SingleAsync()));
    }

    // Stripe HTTP stubbing (the SDK builds and parses the requests for real; only the network
    // is replaced). Tests of this collection run one at a time, so swapping the SDK's global
    // client is safe as long as the previous one is restored.

    private static HttpResponseMessage StripeJson(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage StripeError(HttpStatusCode status, string message) => new(status)
    {
        Content = new StringContent(
            $$"""{ "error": { "type": "invalid_request_error", "message": "{{message}}" } }""",
            Encoding.UTF8,
            "application/json")
    };

    private static string SubscriptionJson(string id, string status) =>
        $$"""{ "id": "{{id}}", "object": "subscription", "status": "{{status}}" }""";

    private sealed class StripeApiStub : IDisposable
    {
        private readonly IStripeClient _previous;

        public StripeApiStub(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            Handler = new StubStripeHandler(responder);
            _previous = StripeConfiguration.StripeClient;
            StripeConfiguration.StripeClient = new StripeClient(
                "sk_test_stub",
                httpClient: new SystemNetHttpClient(new HttpClient(Handler)));
        }

        public StubStripeHandler Handler { get; }

        public void Dispose() => StripeConfiguration.StripeClient = _previous;
    }

    private sealed class StubStripeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubStripeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        public List<string> Methods { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method.Method);
            return Task.FromResult(_responder(request));
        }
    }

    // Event payloads (same shapes as the ones Stripe sends)

    private static string CheckoutCompletedEvent(
        Guid? userId,
        string subscriptionId = "sub_test_123",
        string customerId = "cus_test_123")
    {
        var reference = userId?.ToString() ?? "not-a-guid";

        return $$"""
        {
          "id": "evt_test_checkout_completed",
          "object": "event",
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "cs_test_123",
              "object": "checkout.session",
              "client_reference_id": "{{reference}}",
              "customer": "{{customerId}}",
              "subscription": "{{subscriptionId}}",
              "mode": "subscription"
            }
          }
        }
        """;
    }

    private static string SubscriptionDeletedEvent(string subscriptionId) => $$"""
        {
          "id": "evt_test_subscription_deleted",
          "object": "event",
          "type": "customer.subscription.deleted",
          "data": {
            "object": {
              "id": "{{subscriptionId}}",
              "object": "subscription"
            }
          }
        }
        """;

    private static string UnhandledEvent() => """
        {
          "id": "evt_test_unhandled",
          "object": "event",
          "type": "customer.created",
          "data": {
            "object": {
              "id": "cus_test_123",
              "object": "customer"
            }
          }
        }
        """;
}
