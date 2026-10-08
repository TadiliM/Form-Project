using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Services;
using backend.Models.Dtos;
using System.Security.Claims;
using Stripe;

namespace backend.Controllers;

[ApiController]
[Route("api/subscriptions")]
public class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionsService _subscriptionsService;

    public SubscriptionsController(ISubscriptionsService subscriptionsService)
    {
        _subscriptionsService = subscriptionsService;
    }

    [Authorize]
    [HttpPost("checkout")]
    public async Task<ActionResult<CreateCheckoutSessionResponseDto>> CreateCheckoutSession()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var checkoutUrl = await _subscriptionsService.CreateCheckoutSessionAsync(userId);

        return Ok(new CreateCheckoutSessionResponseDto { CheckoutUrl = checkoutUrl });
    }

    /// <summary>
    /// Called by the success page with the Checkout Session id Stripe put in the URL. The service
    /// re-reads the session from Stripe and activates the plan.
    /// </summary>
    [Authorize]
    [HttpPost("confirm")]
    public async Task<IActionResult> ConfirmCheckoutSession(
        [FromBody] ConfirmCheckoutSessionRequestDto request)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        try
        {
            await _subscriptionsService.ConfirmCheckoutSessionAsync(userId, request.SessionId);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> HandleStripeWebhook()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        var stripeSignature = Request.Headers["Stripe-Signature"].ToString();

        if (string.IsNullOrEmpty(stripeSignature))
            return BadRequest(new { message = "Missing Stripe signature." });

        try
        {
            await _subscriptionsService.HandleWebhookAsync(json, stripeSignature);
            return Ok();
        }
        catch (StripeException ex)
        {
            Console.Error.WriteLine($"Webhook rejected: {ex.Message}");
            return BadRequest(new { message = "Invalid signature." });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error while processing the webhook: {ex}");
            return StatusCode(500);
        }
    }

    [Authorize]
    [HttpPost("cancel")]
    public async Task<IActionResult> CancelSubscription()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        try
        {
            await _subscriptionsService.CancelSubscriptionAsync(userId);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (StripeException ex)
        {
            Console.Error.WriteLine($"Stripe error while cancelling: {ex.Message}");
            return StatusCode(500, new { message = "Error while cancelling." });
        }
    }
}