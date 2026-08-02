namespace Aegis.Application.Abstractions.Notifications;

/// <summary>An outbound email message.</summary>
/// <param name="To">Recipient address.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="PlainTextBody">Plain-text body.</param>
public sealed record EmailMessage(string To, string Subject, string PlainTextBody);

/// <summary>
/// Sends transactional email.
/// </summary>
/// <remarks>
/// <para>
/// <b>Status: the only implementation writes to the log.</b> Stated plainly rather than left to be
/// discovered. Invitation emails are not delivered anywhere in this build — the port exists so the
/// invitation flow can be built, tested and reviewed now, and so that adding SMTP or SES later is
/// one adapter rather than a change to the identity module.
/// </para>
/// <para>
/// The alternative would have been to return the invitation token in the API response so a client
/// could construct the link itself. That is worse than an unwired port: it makes the token visible
/// to anything that logs responses, and it is the kind of development shortcut that survives into
/// production because it works.
/// </para>
/// </remarks>
public interface IEmailSender
{
    /// <summary>Sends a message.</summary>
    /// <remarks>
    /// Failure is reported by exception, not by a return value. A failed invitation email means the
    /// invitee will never receive their link, so the caller needs to know rather than proceed as
    /// though the invitation had been delivered.
    /// </remarks>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
