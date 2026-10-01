using Stripe;
using Stripe.Checkout;
using backend.Data;
using backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public class SubscriptionsService : ISubscriptionsService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;

    public SubscriptionsService(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public async Task<string> CreateCheckoutSessionAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user is null)
            throw new InvalidOperationException("User not found.");

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    Price = _configuration["Stripe:PriceId"],
                    Quantity = 1
                }
            },
            ClientReferenceId = userId.ToString(),
            SuccessUrl = "http://localhost:3000/success",
            CancelUrl = "http://localhost:3000/cancel"
        };

        var service = new SessionService();
        Session session = await service.CreateAsync(options);

        return session.Url;
    }

    public async Task HandleWebhookAsync(string json, string stripeSignature)
    {
        var webhookSecret = _configuration["Stripe:WebhookSecret"];

        var stripeEvent = EventUtility.ConstructEvent(
            json, stripeSignature, webhookSecret, throwOnApiVersionMismatch: false);

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutCompletedAsync(stripeEvent);
                break;

            case "customer.subscription.deleted":
                await HandleSubscriptionDeletedAsync(stripeEvent);
                break;

            case "invoice.paid":
                await HandleInvoicePaidAsync(stripeEvent);
                break;
        }
    }

    public async Task CancelSubscriptionAsync(Guid userId)
    {
        var subscription = await _context.Subscriptions
            .Include(s => s.User)
            .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        if (subscription is null)
            throw new InvalidOperationException("No active subscription to cancel.");

        if (string.IsNullOrEmpty(subscription.StripeSubscriptionId))
            throw new InvalidOperationException("Stripe subscription not found.");

        var service = new Stripe.SubscriptionService();
        await service.CancelAsync(subscription.StripeSubscriptionId);
    }

    private async Task HandleCheckoutCompletedAsync(Event stripeEvent)
    {
        var session = stripeEvent.Data.Object as Session;
        if (session is null) return;

        if (!Guid.TryParse(session.ClientReferenceId, out var userId))
            return;

        // Webhooks can be delivered more than once: skip an event that was already processed.
        var alreadyProcessed = await _context.Subscriptions
            .AnyAsync(s => s.StripeSubscriptionId == session.SubscriptionId);
        if (alreadyProcessed)
            return;

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return;

        // Ask Stripe for the real period end; when Stripe is unreachable, keep the one-month fallback.
        DateTime periodEnd = DateTime.UtcNow.AddMonths(1);
        try
        {
            var stripeSubscription = await new Stripe.SubscriptionService()
                .GetAsync(session.SubscriptionId);
            var item = stripeSubscription.Items.Data.FirstOrDefault();
            if (item?.CurrentPeriodEnd is DateTime end)
                periodEnd = end;
        }
        catch (StripeException ex)
        {
            Console.Error.WriteLine($"Could not read the Stripe billing period: {ex.Message}");
        }

        var subscription = new Models.Subscription
        {
            UserId = userId,
            StripeCustomerId = session.CustomerId,
            StripeSubscriptionId = session.SubscriptionId,
            Status = SubscriptionStatus.Active,
            CreatedAt = DateTime.UtcNow,
            CurrentPeriodEnd = periodEnd
        };

        _context.Subscriptions.Add(subscription);
        user.SetPlanType(PlanType.Pro);

        await _context.SaveChangesAsync();
    }

    private async Task HandleSubscriptionDeletedAsync(Event stripeEvent)
    {
        var stripeSubscription = stripeEvent.Data.Object as Stripe.Subscription;
        if (stripeSubscription is null) return;

        var subscription = await _context.Subscriptions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscription.Id);

        if (subscription is null)
            return;

        if (subscription.Status == SubscriptionStatus.Cancelled)
            return;

        subscription.Cancel();
        await _context.SaveChangesAsync();
    }

    private async Task HandleInvoicePaidAsync(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice is null) return;

        var stripeSubscriptionId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
        if (string.IsNullOrEmpty(stripeSubscriptionId))
            return;

        var subscription = await _context.Subscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId);

        if (subscription is null)
            return;

        try
        {
            var stripeSubscription = await new Stripe.SubscriptionService()
                .GetAsync(stripeSubscriptionId);
            var item = stripeSubscription.Items.Data.FirstOrDefault();
            if (item?.CurrentPeriodEnd is DateTime end)
                subscription.CurrentPeriodEnd = end;
        }
        catch (StripeException ex)
        {
            Console.Error.WriteLine($"Could not read the Stripe billing period: {ex.Message}");
            return;
        }

        subscription.Status = SubscriptionStatus.Active;

        await _context.SaveChangesAsync();
    }
}