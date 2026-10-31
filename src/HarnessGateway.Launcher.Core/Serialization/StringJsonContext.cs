using System.Text.Json.Serialization;

namespace HarnessGateway.Launcher.Core.Serialization;

/// <summary>
/// Source-generated serializer for single-value JSON payloads: the string
/// escaping the adapters embed into child-process configuration content.
/// </summary>
[JsonSerializable(typeof(string))]
internal sealed partial class StringJsonContext : JsonSerializerContext;
