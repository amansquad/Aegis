using Aegis.Domain.Common;

namespace Aegis.Application.Identity;

/// <summary>
/// Errors returned by the identity module.
/// </summary>
/// <remarks>
/// Collected in one place so that the deliberate ambiguity of the authentication failures is
/// visible and hard to undo by accident. A future contributor adding "email not found" as a
/// distinct error would be doing so directly beneath the comment explaining why not.
/// </remarks>
public static class IdentityErrors
{
    /// <summary>
    /// The single error returned for every failed sign-in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately identical whether the address is unknown, the password is wrong, or the account
    /// is not yet confirmed. Distinguishing them turns the login form into an account enumeration
    /// oracle: an attacker submits a list of addresses, and the ones that answer "wrong password"
    /// are confirmed customers. That list is valuable on its own and makes credential stuffing and
    /// phishing dramatically more efficient.
    /// </para>
    /// <para>
    /// The handler also performs a dummy hash verification when no user is found, so the response
    /// time does not leak what the message conceals.
    /// </para>
    /// </remarks>
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        "Auth.InvalidCredentials",
        "The email address or password is incorrect.");

    /// <summary>
    /// Returned when an account is locked out.
    /// </summary>
    /// <remarks>
    /// Distinguished from <see cref="InvalidCredentials"/> on purpose, and the trade-off is
    /// deliberate. It does confirm the account exists, but a user locked out by someone else's
    /// guessing needs to know that is what happened — telling them only "wrong password" sends them
    /// into a password reset that will not help, and hides an active attack.
    /// </remarks>
    public static readonly Error AccountLocked = Error.Unauthorized(
        "Auth.AccountLocked",
        "This account is temporarily locked after too many failed sign-in attempts. Try again later.");

    /// <summary>Returned when the account exists but is deactivated or unconfirmed.</summary>
    public static readonly Error AccountNotActive = Error.Unauthorized(
        "Auth.AccountNotActive",
        "This account is not active. Contact your administrator.");

    /// <summary>Returned when the owning organization has been suspended.</summary>
    public static readonly Error OrganizationSuspended = Error.Forbidden(
        "Auth.OrganizationSuspended",
        "Access for this organization has been suspended.");

    /// <summary>Returned when a registration uses an address already taken in that organization.</summary>
    public static readonly Error EmailAlreadyRegistered = Error.Conflict(
        "Auth.EmailAlreadyRegistered",
        "An account with this email address already exists.");

    /// <summary>Returned when an organization slug is already taken platform-wide.</summary>
    public static readonly Error SlugAlreadyTaken = Error.Conflict(
        "Organization.SlugTaken",
        "An organization with this identifier already exists. Choose a different name.");

    /// <summary>Returned when a presented refresh token cannot be honoured.</summary>
    public static readonly Error InvalidRefreshToken = Error.Unauthorized(
        "Auth.InvalidRefreshToken",
        "The session could not be renewed. Please sign in again.");
}
