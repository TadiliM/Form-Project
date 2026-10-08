using System.Globalization;

namespace backend.Models.Dtos;

public class CreateCheckoutSessionResponseDto
{
    public string CheckoutUrl {get; set;} = string.Empty;
}

/// <summary>
/// Body of POST /api/subscriptions/confirm: the Checkout Session id Stripe appended to the
/// success URL, which the API re-reads from Stripe before activating the plan.
/// </summary>
public class ConfirmCheckoutSessionRequestDto
{
    public string SessionId {get; set;} = string.Empty;
}