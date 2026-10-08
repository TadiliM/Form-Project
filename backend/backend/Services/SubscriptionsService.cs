using Stripe;
using Stripe.Checkout;
using backend.Data;
using backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public class SubscriptionsService : ISubscriptionsService
{
    /// <summary>
    /// Where Stripe sends the user back after a successful payment. The
    /// "{CHECKOUT_SESSION_ID}" placeholder is filled in by Stripe: the page reads it and the
    /// API uses it to confirm the payment (see ConfirmCheckoutSessionAsync).
    /// </summary>
    public const string DefaultSuccessUrl =
        "http://localhost:3000/success?session_id={CHECKOUT_SESSION_ID}";

    /// <summary>Where Stripe sends the user back when the payment is abandoned.</summary>
    public const string DefaultCancelUrl = "http://localhost:3000/cancel";

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

        var options = BuildCheckoutOptions(userId);

        var service = new SessionService();
        Session session = await service.CreateAsync(options);

        return session.Url;
    }

    /// <summary>
    /// Builds the Stripe Checkout options. Kept separate from the API call so the return
    /// URLs and the price can be asserted in tests without contacting Stripe.
    ///
    /// The URLs are configuration-driven ("Stripe:SuccessUrl" / "Stripe:CancelUrl") because
    /// the frontend lives on a different origin in production: hardcoded localhost URLs would
    /// send paying users to a dead page.
    /// </summary>
    public SessionCreateOptions BuildCheckoutOptions(Guid userId) => new()
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
        SuccessUrl = _configuration["Stripe:SuccessUrl"] ?? DefaultSuccessUrl,
        CancelUrl = _configuration["Stripe:CancelUrl"] ?? DefaultCancelUrl
    };

    public async Task HandleWebhookAsync(string json, string stripeSignature)
    {
        var webhookSecret = _configuration["Stripe:WebhookSecret"];

        var stripeEvent = EventUtility.ConstructEvent(
            json, stripeSignature, webhookSecret, throwOnApiVersionMismatch: false);

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                if (stripeEvent.Data.Object is Session checkoutSession)
                    await ActivateSubscriptionAsync(checkoutSession);
                break;

            case "customer.subscription.deleted":
                await HandleSubscriptionDeletedAsync(stripeEvent);
                break;

            case "invoice.paid":
                await HandleInvoicePaidAsync(stripeEvent);
                break;
        }
    }

    /// <summary>
    /// Reconciles a payment when the buyer comes back from Stripe: reads the Checkout Session
    /// from the Stripe API and applies it, exactly like the webhook would.
    ///
    /// The success page cannot rely on the webhook alone: Stripe has to reach the API over the
    /// public internet, which never works on localhost, so a local buyer would stay on the Free
    /// plan forever after paying. Confirming on return makes the upgrade immediate (and keeps
    /// working in production when a webhook is delayed). Idempotent: the second call is a no-op.
    /// </summary>
    public async Task ConfirmCheckoutSessionAsync(Guid userId, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("Missing checkout session id.");

        Session session;
        try
        {
            session = await new SessionService().GetAsync(sessionId);
        }
        catch (StripeException ex)
        {
            throw new InvalidOperationException("This checkout session is unknown to Stripe.", ex);
        }

        EnsureSessionBelongsToUser(session, userId);

        if (session.PaymentStatus is not ("paid" or "no_payment_required"))
            throw new InvalidOperationException("This checkout session has not been paid yet.");

        await ActivateSubscriptionAsync(session);
    }

    /// <summary>
    /// Refuses a session that belongs to somebody else: the session id travels in the URL, so
    /// without this check anyone signed in could activate another buyer's subscription.
    /// </summary>
    public static void EnsureSessionBelongsToUser(Session session, Guid userId)
    {
        if (!Guid.TryParse(session.ClientReferenceId, out var owner))
            throw new InvalidOperationException("This checkout session is not linked to a user.");

        if (owner != userId)
            throw new InvalidOperationException("This checkout session belongs to another user.");
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

        // A subscription Stripe has already cancelled must not fail here: Stripe refuses to
        // cancel it again, and that refusal used to show "Error while cancelling" while the
        // account stayed stuck on Pro forever. Reading the Stripe state first makes the call
        // idempotent and catches the local record up when Stripe is already done.
        var stripeSubscription = await service.GetAsync(subscription.StripeSubscriptionId);
        if (stripeSubscription.Status != "canceled")
            await service.CancelAsync(subscription.StripeSubscriptionId);

        // Mirror the cancellation locally right away: the customer.subscription.deleted
        // webhook cannot reach a local API, so waiting for it would leave the user on Pro.
        // Idempotent with the webhook, which skips an already Cancelled subscription.
        subscription.Cancel();
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Creates the local subscription and moves its owner to Pro. Shared by the webhook and by
    /// the return-from-Stripe confirmation, so both paths behave identically.
    /// </summary>
    public async Task ActivateSubscriptionAsync(Session session)
    {
        // A session with no subscription (one-off payment, or a checkout that was never
        // completed) has nothing to store: activating it would grant a Pro plan for free.
        if (string.IsNullOrEmpty(session.SubscriptionId))
            return;

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