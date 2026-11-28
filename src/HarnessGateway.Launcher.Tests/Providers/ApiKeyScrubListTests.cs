using HarnessGateway.Launcher.Core.Providers.Models;

namespace HarnessGateway.Launcher.Tests.Providers;

/// <summary>
/// The curated list is a closed set and its casing rule follows the platform:
/// Windows names are case-insensitive, Unix names are not.
/// </summary>
public class ApiKeyScrubListTests
{
    private static readonly ApiKeyScrubList List = new();

    [Test]
    public async Task Curated_list_contains_the_protocol_keys_and_ZAI()
    {
        await Assert.That(List.VariableNames).Contains("OPENAI_API_KEY");
        await Assert.That(List.VariableNames).Contains("ANTHROPIC_API_KEY");
        await Assert.That(List.VariableNames).Contains("ZAI_API_KEY");
    }

    [Test]
    public async Task Unknown_names_are_never_scrubbed()
    {
        await Assert.That(List.Contains("HGW_KEEP_THIS")).IsFalse();
        await Assert.That(List.Contains("OPENAI_BASE_URL")).IsFalse();
    }

    [Test]
    public async Task Casing_follows_the_platform_rule()
    {
        bool matched = List.Contains("openai_api_key");

        await Assert.That(matched).IsEqualTo(OperatingSystem.IsWindows());
    }

    [Test]
    public async Task The_platform_rule_is_reflected_in_the_curated_names_too()
    {
        // The same comparison the flow uses when removing differently-cased
        // keys from a plan; assert it against a listed name, not a fixture.
        string listedName = List.VariableNames.First();
        string otherCase = listedName.ToLowerInvariant();

        await Assert.That(List.Contains(otherCase)).IsEqualTo(
            OperatingSystem.IsWindows() || listedName == otherCase);
    }
}
