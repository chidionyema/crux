using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.RateLimiting;
using Polly.Retry;
using Stripe;

namespace Crux.Payments.Stripe;

/// <summary>
/// Production Stripe payment gateway. Escrow = manual-capture PaymentIntent
/// (authorise on create, capture on settlement, refund/void on release).
/// Resilience: Polly v8 pipeline (retry → circuit breaker → rate limiter).
/// Idempotency: every mutating call uses a client-supplied idempotency key.
/// </summary>
public sealed class StripePaymentGateway : IStripePaymentGateway
{
    private readonly StripeClient _client;
    private readonly string _webhookSecret;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<StripePaymentGateway> _logger;

    public StripePaymentGateway(IConfiguration configuration, ILogger<StripePaymentGateway> logger)
    {
        var secretKey = configuration["Stripe:SecretKey"]
            ?? throw new InvalidOperationException("Stripe:SecretKey is required.");
        _webhookSecret = configuration["Stripe:WebhookSecret"]
            ?? throw new InvalidOperationException("Stripe:WebhookSecret is required.");
        _client = new StripeClient(secretKey);
        _logger = logger;

        var maxRetries = configuration.GetValue("Stripe:MaxRetries", 3);
        var maxRps = configuration.GetValue("Stripe:MaxRequestsPerSecond", 10.0);

        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<StripeException>(ex => ex.HttpStatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    .Handle<HttpRequestException>(),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                MaxRetryAttempts = maxRetries,
                Delay = TimeSpan.FromSeconds(1)
            })
            .AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<StripeException>()
                    .Handle<HttpRequestException>(),
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(10),
                MinimumThroughput = 3,
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .AddRateLimiter(new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = (int)maxRps,
                TokensPerPeriod = (int)maxRps,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 100
            }))
            .Build();
    }

    public Task<EscrowIntent> CreateEscrowIntentAsync(
        string idempotencyKey, long amountCents, string currency,
        string? description = null, Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        return CreatePaymentIntentAsync(idempotencyKey, amountCents, currency,
            captureMethod: "manual", description, metadata, ct);
    }

    public Task<EscrowIntent> CreateImmediateChargeAsync(
        string idempotencyKey, long amountCents, string currency,
        string? description = null, Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        return CreatePaymentIntentAsync(idempotencyKey, amountCents, currency,
            captureMethod: "automatic", description, metadata, ct);
    }

    private async Task<EscrowIntent> CreatePaymentIntentAsync(
        string idempotencyKey, long amountCents, string currency,
        string captureMethod, string? description, Dictionary<string, string>? metadata,
        CancellationToken ct)
    {
        var options = new RequestOptions { IdempotencyKey = idempotencyKey };

        return await _resiliencePipeline.ExecuteAsync(async token =>
        {
            var createOptions = new PaymentIntentCreateOptions
            {
                Amount = amountCents,
                Currency = currency.ToLowerInvariant(),
                CaptureMethod = captureMethod,
                Description = description,
                Metadata = metadata,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                    AllowRedirects = "never"
                }
            };

            var pi = await new PaymentIntentService(_client).CreateAsync(createOptions, options, token);
            _logger.LogInformation("PaymentIntent {Id} created ({Amount} {Currency}, capture={Method})",
                pi.Id, amountCents, currency, captureMethod);
            return new EscrowIntent(pi.Id, pi.ClientSecret);
        }, ct);
    }

    public async Task<CaptureOutcome> CaptureAsync(string paymentIntentId, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var options = new RequestOptions { IdempotencyKey = idempotencyKey ?? Guid.NewGuid().ToString() };

        return await _resiliencePipeline.ExecuteAsync(async token =>
        {
            var piService = new PaymentIntentService(_client);
            var pi = await piService.GetAsync(paymentIntentId, null, null, token);

            switch (pi.Status)
            {
                case "requires_capture":
                    await piService.CaptureAsync(paymentIntentId, null, options, token);
                    _logger.LogInformation("PaymentIntent {Id} captured", paymentIntentId);
                    return CaptureOutcome.Captured;
                case "succeeded":
                    return CaptureOutcome.AlreadyCaptured;
                case "canceled":
                    return CaptureOutcome.HoldExpired;
                default:
                    throw new InvalidOperationException(
                        $"PaymentIntent {paymentIntentId} is in unexpected status '{pi.Status}' — not capturing.");
            }
        }, ct);
    }

    public async Task<string> RefundOrVoidAsync(string paymentIntentId, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var options = new RequestOptions { IdempotencyKey = idempotencyKey ?? Guid.NewGuid().ToString() };

        return await _resiliencePipeline.ExecuteAsync(async token =>
        {
            var piService = new PaymentIntentService(_client);
            var pi = await piService.GetAsync(paymentIntentId, null, null, token);

            switch (pi.Status)
            {
                case "canceled":
                    return pi.Id;
                case "requires_capture":
                case "requires_confirmation":
                case "requires_payment_method":
                case "requires_action":
                case "processing":
                {
                    var canceled = await piService.CancelAsync(paymentIntentId, null, options, token);
                    _logger.LogInformation("PaymentIntent {Id} voided (was {Status})", paymentIntentId, pi.Status);
                    return canceled.Id;
                }
                default:
                {
                    var refund = await new RefundService(_client).CreateAsync(
                        new RefundCreateOptions { PaymentIntent = paymentIntentId }, options, token);
                    _logger.LogInformation("PaymentIntent {Id} refunded", paymentIntentId);
                    return refund.Id;
                }
            }
        }, ct);
    }

    public async Task<PaymentIntentStatus> GetStatusAsync(string paymentIntentId, CancellationToken ct = default)
    {
        return await _resiliencePipeline.ExecuteAsync(async token =>
        {
            var pi = await new PaymentIntentService(_client).GetAsync(paymentIntentId, null, null, token);
            return pi.Status switch
            {
                "succeeded" => PaymentIntentStatus.Captured,
                "canceled" => PaymentIntentStatus.Released,
                _ => PaymentIntentStatus.Authorised,
            };
        }, ct);
    }

    public StripeWebhook VerifyWebhook(string rawBody, string signatureHeader)
    {
        var stripeEvent = EventUtility.ConstructEvent(rawBody, signatureHeader, _webhookSecret);
        string? piId = null;
        long amount = 0;
        string? chargeId = null;

        if (stripeEvent.Data.Object is PaymentIntent pi)
        {
            piId = pi.Id;
            amount = pi.Amount;
            // Carry the captured charge id so callers can reconcile the event to a specific charge
            // (refund-by-charge, source_transaction) without a second Stripe round-trip.
            chargeId = pi.LatestChargeId;
        }

        return new StripeWebhook(stripeEvent.Id, stripeEvent.Type, piId, amount, chargeId);
    }
}

/// <summary>
/// Extension methods for registering Stripe payment services.
/// </summary>
public static class StripePaymentsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IStripePaymentGateway"/> with the Stripe implementation.
    /// Requires <c>Stripe:SecretKey</c> and <c>Stripe:WebhookSecret</c> configuration.
    /// </summary>
    public static IServiceCollection AddCruxStripePayments(this IServiceCollection services)
    {
        services.TryAddSingleton<IStripePaymentGateway, StripePaymentGateway>();
        return services;
    }
}
