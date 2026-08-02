using Aegis.Application.Abstractions.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aegis.Infrastructure.Notifications;

/// <summary>
/// Writes outbound email to the log instead of sending it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A development placeholder, and it refuses to run in production.</b> The constructor throws
/// when hosted in a production environment, because the alternative failure mode is far worse: a
/// deployment where invitations appear to be sent, administrators see success responses, and no
/// invitee ever receives anything. A missing SMTP adapter should stop a release, not be discovered
/// by a user who never got their link.
/// </para>
/// <para>
/// The real adapter is a follow-up. This one exists so the invitation flow can be built and tested
/// end to end now, with the seam already in the right place.
/// </para>
/// </remarks>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    /// <summary>Initialises the sender, refusing to operate in production.</summary>
    /// <exception cref="InvalidOperationException">The host environment is production.</exception>
    public LoggingEmailSender(ILogger<LoggingEmailSender> logger, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "LoggingEmailSender is a development placeholder and must not run in production. " +
                "Register a real IEmailSender adapter before deploying: invitations would " +
                "otherwise report success while silently reaching nobody.");
        }

        _logger = logger;
    }

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // The body is logged in full, including the invitation link, which is the entire point in
        // development — and precisely why this class refuses to start in production, where that
        // would write a live credential into the log pipeline.
        _logger.LogInformation(
            "Email not sent (no mail adapter configured). To: {Recipient}; Subject: {Subject}\n{Body}",
            message.To,
            message.Subject,
            message.PlainTextBody);

        return Task.CompletedTask;
    }
}
