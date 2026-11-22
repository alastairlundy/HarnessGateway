namespace HarnessGateway.Launcher.Core.Settings;

/// <summary>
/// Outcome of resolving a provider key source: a closed family of exactly
/// three sealed variants - a secret from one of the two flags, the prompt
/// when neither flag was given, or a typed failure carrying a user-facing
/// message. <see cref="IsSuccess"/> is computed from the variant, so
/// consumers pattern-match instead of parsing message text.
/// </summary>
public abstract record ApiKeySourceResult
{
    /// <summary>True for the non-failure variants (<see cref="Resolved"/>, <see cref="Prompt"/>).</summary>
    public bool IsSuccess => this is Resolved or Prompt;

    /// <summary>Neither source flag was given: the caller falls through to the interactive prompt.</summary>
    public sealed record Prompt : ApiKeySourceResult;

    /// <summary>A source flag supplied the secret, read at invocation time.</summary>
    public sealed record Resolved(string Secret) : ApiKeySourceResult;

    /// <summary>Sourcing failed with a typed failure and a user-facing message.</summary>
    public sealed record Failed(ApiKeySourceFailure Failure, string Message) : ApiKeySourceResult;
}
