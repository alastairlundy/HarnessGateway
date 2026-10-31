using System.Text.Json.Serialization;

namespace HarnessGateway.Launcher.Core.Configuration;

/// <summary>
/// Source-generated serializer for backup records crash recovery replays.
/// Compact output matches the previous reflection-based settings (no indent).
/// </summary>
[JsonSerializable(typeof(BackupRecord))]
internal sealed partial class BackupJsonContext : JsonSerializerContext;
