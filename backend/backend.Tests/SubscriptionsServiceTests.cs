using backend.Models.Enums;
using backend.Services;
using Microsoft.EntityFrameworkCore;
using Stripe;

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
