namespace HarnessGateway.Launcher.Core.Configuration;

// The closed family is exactly these two variants.
// Harness env-var configuration is declared on LaunchPlan.Process.EnvironmentVariables,
// not as an action: it goes on the spawned process and needs no undo.
public abstract record ConfigurationAction;

public sealed record CliArgumentAction(string Argument, string Value) : ConfigurationAction;

public sealed record ConfigFileWriteAction(string TargetPath, string Content) : ConfigurationAction;
