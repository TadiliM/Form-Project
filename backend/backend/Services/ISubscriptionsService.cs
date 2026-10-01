namespace backend.Services;

public interface ISubscriptionsService
{
    Task<string> CreateCheckoutSessionAsync(Guid userId);
    Task HandleWebhookAsync(string json, string stripeSignature);

    Task CancelSubscriptionAsync(Guid userId);
}