using System.Security.Cryptography;
using System.Text;

namespace backend.Tests;

/// <summary>
/// Builds Stripe webhooks that are correctly signed, so the SDK accepts them.
///
/// Stripe signs the "&lt;timestamp&gt;.&lt;payload&gt;" string with HMAC-SHA256 and the webhook
/// secret, then sends the result in the "Stripe-Signature" header as
/// "t=&lt;timestamp&gt;,v1=&lt;signature&gt;". The SDK recomputes and compares it: that is
/// exactly what <c>EventUtility.ConstructEvent</c> checks inside the service.
/// </summary>
public static class StripeWebhookTestHelper
{
    /// <summary>Secret shared by the test configuration and the signature computation.</summary>
    public const string WebhookSecret = "whsec_test_secret_for_the_tests";

    /// <summary>Signs an event payload the same way Stripe would.</summary>
    public static (string Payload, string Signature) Sign(string eventJson)
    {
        // Current timestamp: by default the SDK rejects a signature older than 5 minutes.
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{timestamp}.{eventJson}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(WebhookSecret));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();

        return (eventJson, $"t={timestamp},v1={hash}");
    }

    /// <summary>Deliberately wrong signature: valid timestamp, all-zero hash.</summary>
    public static string InvalidSignature()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"t={timestamp},v1={new string('0', 64)}";
    }
}
