using backend.Models.Enums;

namespace backend.Models;

public class Subscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string StripeCustomerId { get; set; } = string.Empty;
    public string StripeSubscriptionId { get; set; } = string.Empty;
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public DateTime CurrentPeriodEnd {get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public void Activate()
    {
        Status = SubscriptionStatus.Active;
        User?.SetPlanType(PlanType.Pro);
    }

    public void Cancel()
    {
        Status = SubscriptionStatus.Cancelled;
        User?.SetPlanType(PlanType.Free);
    }
}