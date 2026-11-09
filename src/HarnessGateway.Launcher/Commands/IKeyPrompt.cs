namespace HarnessGateway.Launcher.Commands;

internal interface IKeyPrompt
{
    string? PromptForNewKey(string providerName, bool entryHasKey);
}