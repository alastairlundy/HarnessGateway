namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// The yes/no question a launch flow asks before it touches a harness whose
/// application instance is already running. The flow composes the message
/// Core-side; this seam only collects the answer.
/// </summary>
/// <remarks>
/// Implementations answer the same way in both non-interactive cases: a decline
/// and an unanswerable prompt (no interactive terminal) both return
/// <c>false</c>, because the flow maps either to the same user-rectifiable
/// refusal. The scripted override (<c>--confirm-running</c>) never reaches
/// this seam - the flow skips its prompt in every context.
/// </remarks>
public interface IConfirmationPrompt
{
    /// <summary>
    /// Presents <paramref name="message"/> to the user and reports whether
    /// they confirmed; <c>false</c> when declined or when nothing can answer.
    /// </summary>
    Task<bool> ConfirmAsync(string message, CancellationToken cancellationToken = default);
}
