namespace HarnessGateway.Launcher.Core.Configuration;

public abstract record ConfigurationAction;

public sealed record CliArgumentAction(string Argument, string? Value) : ConfigurationAction;

public sealed record ConfigFileWriteAction(string TargetPath, string Content) : ConfigurationAction;

public sealed record EnvironmentVariableAction(string Name, string Value) : ConfigurationAction;
