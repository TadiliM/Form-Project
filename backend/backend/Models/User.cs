using backend.Models.Enums;

namespace backend.Models;

public class User
{
    public Guid Id {get; set;}
    public string Email {get; set; } = string.Empty;
    public string Name {get; set; } = string.Empty;
    public string PasswordHash {get; set; } = string.Empty;
    public PlanType PlanType {get; set; } = PlanType.Free;
    public DateTime CreatedAt {get; set; } = DateTime.UtcNow;

    public List<Form> Forms {get; set; } = new();
    public List<Subscription> Subscriptions {get; set; } = new();

    public void Register()
    {
        
    }

    public void Login()
    {
        
    }

    public void SetPlanType(PlanType newPlan)
    {
        PlanType = newPlan;
    }
}