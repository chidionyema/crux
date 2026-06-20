namespace Crux.Notifications;

/// <summary>Notification channel.</summary>
public enum NotificationChannel { Email, Sms, Push }

/// <summary>Priority level for delivery ordering.</summary>
public enum NotificationPriority { Low, Normal, High, Critical }

/// <summary>Delivery status.</summary>
public enum NotificationStatus { Pending, Sent, Delivered, Failed, Bounced }

/// <summary>
/// Request to send a notification through one or more channels.
/// </summary>
public sealed class SendNotificationRequest
{
    public string RecipientId { get; init; } = string.Empty;
    public string RecipientAddress { get; init; } = string.Empty;
    public NotificationChannel Channel { get; init; }
    public NotificationPriority Priority { get; init; } = NotificationPriority.Normal;
    public string TemplateKey { get; init; } = string.Empty;
    public Dictionary<string, object>? TemplateData { get; init; }
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// Renders a notification template into a deliverable message.
/// </summary>
public interface ITemplateRenderer
{
    Task<RenderedMessage> RenderAsync(string template, Dictionary<string, object> data, CancellationToken ct = default);
}

/// <summary>
/// A rendered notification message ready for delivery.
/// </summary>
public sealed record RenderedMessage(string Subject, string BodyHtml, string BodyText);

/// <summary>
/// Email provider abstraction. Implementations: SendGrid, SES, Postmark, SMTP.
/// </summary>
public interface IEmailProvider
{
    Task<SendResult> SendAsync(string to, string subject, string htmlBody, string textBody, string? idempotencyKey = null, CancellationToken ct = default);
}

/// <summary>
/// SMS provider abstraction. Implementations: Twilio, Vonage.
/// </summary>
public interface ISmsProvider
{
    Task<SendResult> SendAsync(string to, string body, string? idempotencyKey = null, CancellationToken ct = default);
}

/// <summary>
/// Push notification provider. Implementations: FCM, APNs.
/// </summary>
public interface IPushProvider
{
    Task<SendResult> SendAsync(string deviceToken, string title, string body, Dictionary<string, string>? data = null, string? idempotencyKey = null, CancellationToken ct = default);
}

/// <summary>Result of sending a notification.</summary>
public sealed record SendResult(bool Success, string? ProviderMessageId = null, string? Error = null);

/// <summary>
/// Notification dispatch service — the single entry point for sending notifications.
/// Routes to the correct provider, handles preferences/suppression, and records delivery status.
/// </summary>
public interface INotificationDispatchService
{
    Task<SendResult> SendAsync(SendNotificationRequest request, CancellationToken ct = default);
}
