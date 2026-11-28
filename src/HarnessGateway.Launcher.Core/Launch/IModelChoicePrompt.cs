namespace HarnessGateway.Launcher.Core.Launch;

/// <summary>
/// The in-terminal choice a launch flow asks for when a bare model name
/// is ambiguous against the models.dev catalog. The flow composes the
/// title and hands over the provider-qualified candidates; this seam only
/// collects the answer - the same split <see cref="IConfirmationPrompt"/>
/// uses, and the picker reuses the wizard's choice-list pattern
/// (Wizard/ModelStep.cs) behind it.
/// </summary>
public interface IModelChoicePrompt
{
    /// <summary>
    /// Presents <paramref name="candidates"/> to the user and returns the
    /// chosen <c>provider/model</c> reference.
    /// </summary>
    Task<string> PickAsync(
        string title,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken = default);
}
