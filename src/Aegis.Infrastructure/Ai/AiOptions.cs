namespace Aegis.Infrastructure.Ai;

/// <summary>Language model configuration, bound from the <c>Ai</c> configuration section.</summary>
public sealed class AiOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ai";

    /// <summary>
    /// OpenRouter API key. Supplied by user-secrets or the <c>Ai__ApiKey</c> environment variable.
    /// </summary>
    /// <remarks>
    /// Never committed, and never logged. When absent the application falls back to the rule-based
    /// extractor rather than failing to start, so a developer without a key still gets a working
    /// intake form and a green test suite.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>OpenRouter's OpenAI-compatible base address.</summary>
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// The model slug to route to.
    /// </summary>
    /// <remarks>
    /// Configuration rather than a constant, because the right model for this task is an
    /// operational judgement that changes as prices and capabilities move. Swapping it must never
    /// require a deployment of new code.
    /// </remarks>
    public string Model { get; set; } = "anthropic/claude-sonnet-4.5";

    /// <summary>
    /// How long to wait for a completion before giving up.
    /// </summary>
    /// <remarks>
    /// Short by intent. This runs while a member of the public waits on an intake form, and a
    /// twenty-second pause is one they will abandon. Timing out into the rule-based extractor is a
    /// far better outcome than a spinner.
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 12;

    /// <summary>
    /// Upper bound on the response size.
    /// </summary>
    /// <remarks>
    /// The reply is a small JSON object. A generous ceiling would only pay for a model that had
    /// started rambling, which is a response we would reject anyway.
    /// </remarks>
    public int MaxOutputTokens { get; set; } = 700;

    /// <summary>Site URL sent for OpenRouter attribution. Optional.</summary>
    public string? SiteUrl { get; set; }

    /// <summary>Application name sent for OpenRouter attribution. Optional.</summary>
    public string SiteName { get; set; } = "Aegis";

    /// <summary>True when a key is present and the model adapter should be used.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
