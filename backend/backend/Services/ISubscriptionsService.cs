using Stripe.Checkout;

namespace backend.Services;

public interface ISubscriptionsService
{
    Task<string> CreateCheckoutSessionAsync(Guid userId);

    /// <summary>Stripe Checkout options (price and return URLs) for a user, without calling Stripe.</summary>
    SessionCreateOptions BuildCheckoutOptions(Guid userId);

    /// <summary>
    /// Verifies with Stripe the Checkout Session the buyer comes back with, and applies it.
    /// Called by the success page, because the webhook cannot reach a local API.
    /// </summary>
    Task ConfirmCheckoutSessionAsync(Guid userId, string sessionId);

    Task HandleWebhookAsync(string json, string stripeSignature);

    Task CancelSubscriptionAsync(Guid userId);
}