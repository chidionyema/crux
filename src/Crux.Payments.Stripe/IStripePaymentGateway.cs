namespace Crux.Payments.Stripe;

/// <summary>
/// Core Stripe payment gateway abstraction. Covers PaymentIntent (escrow),
/// capture, refund, and webhook verification. Idempotency-first — every
/// mutating call accepts an idempotency key.
/// </summary>
public interface IStripePaymentGateway
{
    /// <summary>
    /// Creates an escrow PaymentIntent (manual-capture authorisation).
    /// The hold is authorised but not captured — capture happens at settlement.
    /// </summary>
    Task<EscrowIntent> CreateEscrowIntentAsync(
        string idempotencyKey, long amountCents, string currency,
        string? description = null, Dictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a PaymentIntent with immediate capture (automatic).
    /// </summary>
    Task<EscrowIntent> CreateImmediateChargeAsync(
        string idempotencyKey, long amountCents, string currency,
        string? description = null, Dictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Captures a previously authorised escrow PaymentIntent.
    /// Returns the capture outcome — handles already-captured and expired holds.
    /// </summary>
    Task<CaptureOutcome> CaptureAsync(string paymentIntentId, string? idempotencyKey = null, CancellationToken ct = default);

    /// <summary>
    /// Refunds or voids a PaymentIntent. Routes by status:
    /// captured → refund, authorised-but-uncaptured → void.
    /// </summary>
    Task<string> RefundOrVoidAsync(string paymentIntentId, string? idempotencyKey = null, CancellationToken ct = default);

    /// <summary>
    /// Reads the live status of a PaymentIntent from Stripe.
    /// Read-only — does not move money.
    /// </summary>
    Task<PaymentIntentStatus> GetStatusAsync(string paymentIntentId, CancellationToken ct = default);

    /// <summary>
    /// Verifies a Stripe webhook signature and returns the parsed event.
    /// Throws <see cref="StripeException"/> if the signature is invalid.
    /// </summary>
    StripeWebhook VerifyWebhook(string rawBody, string signatureHeader);
}

/// <summary>An authorised escrow hold: the PaymentIntent id and client secret.</summary>
public sealed record EscrowIntent(string PaymentIntentId, string ClientSecret);

/// <summary>Outcome of a capture attempt.</summary>
public enum CaptureOutcome
{
    Captured,
    AlreadyCaptured,
    HoldExpired
}

/// <summary>Coarse PaymentIntent status for reconciliation.</summary>
public enum PaymentIntentStatus
{
    Authorised,
    Captured,
    Released
}

/// <summary>A verified Stripe webhook event.</summary>
/// <param name="ChargeId">The captured charge id (<c>PaymentIntent.LatestChargeId</c>) when the event
/// carries a PaymentIntent; null otherwise. Lets callers reconcile to a charge without a second API call.</param>
public sealed record StripeWebhook(string EventId, string EventType, string? PaymentIntentId, long AmountCents, string? ChargeId = null);
