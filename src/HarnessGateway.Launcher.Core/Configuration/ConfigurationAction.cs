namespace HarnessGateway.Launcher.Core.Configuration;

// T045 (amending T023): the closed family is exactly these two variants.
// Harness env-var configuration is declared on LaunchPlan.Process.EnvironmentVariables,
// not as an action - the launch path never mutates the launcher process environment.
public abstract record ConfigurationAction;

public sealed record CliArgumentAction(string Argument, string? Value) : ConfigurationAction;

public sealed record ConfigFileWriteAction(string TargetPath, string Content) : ConfigurationAction;
