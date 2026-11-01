using System.Text.Json.Serialization;
using HarnessGateway.Launcher.Core.Providers;
using HarnessGateway.Launcher.Core.Settings;

namespace HarnessGateway.Launcher.Core.Serialization;

/// <summary>
/// Source-generated serializer for the settings file and the platform credential
/// store file (Windows DPAPI record). Both are written indented by design, so the
/// context carries <c>WriteIndented</c> once for both call sites.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LauncherSettings))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
