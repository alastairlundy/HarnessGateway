using HarnessGateway.Launcher.Core.Toml;

namespace HarnessGateway.Launcher.Tests.Toml;

public class TomlDocumentEditorTests
{
    private static readonly string ConfigWithoutProviderBlock = ("""
        # ~/.codex/config.toml
        model = "gpt-5-codex"
        model_provider = "openai" # default provider

        # Keep the workspace writable for edits
        sandbox_mode = "workspace-write"

        [model_providers.openai]
        name = "OpenAI"
        base_url = "https://api.openai.com/v1"
        env_key = "OPENAI_API_KEY"
        wire_api = "chat"

        [features]
        multi_agent = true
        """ + "\n").ReplaceLineEndings("\n");

    private static readonly string ConfigWithProviderBlock = ("""
        # ~/.codex/config.toml
        model = "gpt-5-codex"
        model_provider = "openai" # default provider

        [model_providers.openai]
        name = "OpenAI"
        base_url = "https://api.openai.com/v1"
        env_key = "OPENAI_API_KEY"
        wire_api = "chat"

        [model_providers.harnessgateway]
        # launcher-managed block - do not edit by hand
        name = "HarnessGateway"
        base_url = "http://localhost:8081/v1"
        env_key = "HARNESSGATEWAY_API_KEY"
        wire_api = "chat"
        stale_key = "remove me"

        [features]
        multi_agent = true
        """ + "\n").ReplaceLineEndings("\n");

    [Test]
    public async Task Round_trips_representative_config_byte_for_byte()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(ConfigWithoutProviderBlock);

        string output = editor.ToToml();

        await Assert.That(output).IsEqualTo(ConfigWithoutProviderBlock);
        // Idempotent: re-parsing the serialised output reproduces it exactly.
        await Assert.That(TomlDocumentEditor.Parse(output).ToToml()).IsEqualTo(output);
    }

    [Test]
    public async Task Round_trips_config_with_provider_block_byte_for_byte()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(ConfigWithProviderBlock);

        await Assert.That(editor.ToToml()).IsEqualTo(ConfigWithProviderBlock);
    }

    [Test]
    public async Task Round_trips_empty_comment_only_and_whitespace_only_documents()
    {
        await Assert.That(TomlDocumentEditor.Parse(string.Empty).ToToml()).IsEqualTo(string.Empty);

        const string commentOnly = "# just a note\n\n# and another note\n";
        await Assert.That(TomlDocumentEditor.Parse(commentOnly).ToToml()).IsEqualTo(commentOnly);

        const string whitespaceOnly = "   \n\t\n";
        await Assert.That(TomlDocumentEditor.Parse(whitespaceOnly).ToToml()).IsEqualTo(whitespaceOnly);
    }

    [Test]
    public async Task Round_trips_crlf_document_byte_for_byte()
    {
        string crlfConfig = ConfigWithProviderBlock.ReplaceLineEndings("\r\n");

        TomlDocumentEditor editor = TomlDocumentEditor.Parse(crlfConfig);

        await Assert.That(editor.ToToml()).IsEqualTo(crlfConfig);
    }

    [Test]
    public async Task BlockExists_probes_named_blocks()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(ConfigWithoutProviderBlock);

        await Assert.That(editor.BlockExists("model_providers.openai")).IsTrue();
        await Assert.That(editor.BlockExists("features")).IsTrue();
        await Assert.That(editor.BlockExists("model_providers.harnessgateway")).IsFalse();
        // Only the full dotted header matches; the trailing segment alone does not.
        await Assert.That(editor.BlockExists("harnessgateway")).IsFalse();
        await Assert.That(editor.BlockExists("openai")).IsFalse();
    }

    [Test]
    public async Task NameInUse_matches_headers_and_conflicting_key_forms()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(ConfigWithoutProviderBlock);

        await Assert.That(editor.NameInUse("model_providers.openai")).IsTrue();
        await Assert.That(editor.NameInUse("features")).IsTrue();
        await Assert.That(editor.NameInUse("model_providers.harnessgateway")).IsFalse();
        // Values are not keys: the model_provider value mentioning a name does not count.
        await Assert.That(editor.NameInUse("harnessgateway")).IsFalse();
    }

    [Test]
    public async Task NameInUse_detects_table_array_headers()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse("[[model_providers.harnessgateway]]\nname = \"Mine\"\n");

        await Assert.That(editor.NameInUse("model_providers.harnessgateway")).IsTrue();
    }

    [Test]
    public async Task NameInUse_detects_inline_table_under_parent_section()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(
            "[model_providers]\nharnessgateway = { name = \"Mine\", wire_api = \"responses\" }\n");

        await Assert.That(editor.NameInUse("model_providers.harnessgateway")).IsTrue();
        // Unrelated keys in the same parent section do not conflict.
        await Assert.That(editor.NameInUse("model_providers.openai")).IsFalse();
    }

    [Test]
    public async Task NameInUse_detects_dotted_keys_defining_the_name()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(
            "[model_providers]\nharnessgateway.name = \"Mine\"\n");

        await Assert.That(editor.NameInUse("model_providers.harnessgateway")).IsTrue();
    }

    [Test]
    public async Task NameInUse_detects_top_level_dotted_and_ancestor_keys()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(
            "model_providers.harnessgateway = \"scalar\"\n");

        await Assert.That(editor.NameInUse("model_providers.harnessgateway")).IsTrue();

        TomlDocumentEditor ancestorEditor = TomlDocumentEditor.Parse(
            "model_providers = \"scalar\"\n");

        await Assert.That(ancestorEditor.NameInUse("model_providers.harnessgateway")).IsTrue();
    }

    [Test]
    public async Task UpsertBlock_inserts_missing_block_after_a_blank_line()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(ConfigWithoutProviderBlock);

        editor.UpsertBlock("model_providers.harnessgateway", ProviderBlockValues());

        string expected = ConfigWithoutProviderBlock
            + "\n"
            + "[model_providers.harnessgateway]\n"
            + "name = \"HarnessGateway\"\n"
            + "base_url = \"http://localhost:9999/v1\"\n"
            + "env_key = \"HARNESSGATEWAY_API_KEY\"\n"
            + "wire_api = \"responses\"\n";
        string output = editor.ToToml();
        await Assert.That(output).IsEqualTo(expected);
        await Assert.That(editor.BlockExists("model_providers.harnessgateway")).IsTrue();
        await Assert.That(TomlDocumentEditor.Parse(output).ToToml()).IsEqualTo(output);
    }

    [Test]
    public async Task UpsertBlock_appends_after_a_document_without_a_trailing_newline()
    {
        const string noTrailingNewline = "model = \"gpt-5-codex\"";

        TomlDocumentEditor editor = TomlDocumentEditor.Parse(noTrailingNewline);

        editor.UpsertBlock("provider", new Dictionary<string, object?> { ["name"] = "Mine" });

        await Assert.That(editor.ToToml()).IsEqualTo(noTrailingNewline + "\n\n[provider]\nname = \"Mine\"\n");
    }

    [Test]
    public async Task UpsertBlock_successive_appends_keep_one_blank_line_between_blocks()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(ConfigWithoutProviderBlock);

        editor.UpsertBlock("model_providers.harnessgateway", ProviderBlockValues());
        editor.UpsertBlock("providers.other", new Dictionary<string, object?> { ["name"] = "Other" });

        string output = editor.ToToml();
        await Assert.That(output).IsEqualTo(
            ConfigWithoutProviderBlock
            + "\n"
            + "[model_providers.harnessgateway]\n"
            + "name = \"HarnessGateway\"\n"
            + "base_url = \"http://localhost:9999/v1\"\n"
            + "env_key = \"HARNESSGATEWAY_API_KEY\"\n"
            + "wire_api = \"responses\"\n"
            + "\n"
            + "[providers.other]\n"
            + "name = \"Other\"\n");
        await Assert.That(TomlDocumentEditor.Parse(output).ToToml()).IsEqualTo(output);
    }

    [Test]
    public async Task UpsertBlock_inserts_block_into_empty_document()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(string.Empty);

        editor.UpsertBlock("a", new Dictionary<string, object?> { ["b"] = "c" });

        await Assert.That(editor.ToToml()).IsEqualTo("[a]\nb = \"c\"\n");
    }

    [Test]
    public async Task UpsertBlock_replaces_existing_block_and_preserves_unrelated_sections()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(ConfigWithProviderBlock);
        const string blockHeader = "[model_providers.harnessgateway]";
        const string nextSection = "[features]";
        int blockStart = ConfigWithProviderBlock.IndexOf(blockHeader, StringComparison.Ordinal);
        int nextSectionStart = ConfigWithProviderBlock.IndexOf(nextSection, StringComparison.Ordinal);
        await Assert.That(blockStart >= 0 && nextSectionStart > blockStart).IsTrue();

        editor.UpsertBlock("model_providers.harnessgateway", ProviderBlockValues());

        // Everything outside the spliced block - including comments, blank lines and
        // section order - must survive byte-for-byte.
        string expectedPrefix = ConfigWithProviderBlock[..blockStart];
        string expectedSuffix = ConfigWithProviderBlock[nextSectionStart..];
        string expectedBlock = blockHeader + "\n"
            + "# launcher-managed block - do not edit by hand\n"
            + "name = \"HarnessGateway\"\n"
            + "base_url = \"http://localhost:9999/v1\"\n"
            + "env_key = \"HARNESSGATEWAY_API_KEY\"\n"
            + "wire_api = \"responses\"\n"
            + "\n";
        string output = editor.ToToml();
        await Assert.That(output).IsEqualTo(expectedPrefix + expectedBlock + expectedSuffix);
        await Assert.That(output.Contains("stale_key", StringComparison.Ordinal)).IsFalse();
        await Assert.That(TomlDocumentEditor.Parse(output).ToToml()).IsEqualTo(output);
    }

    [Test]
    public async Task SetTopLevelValue_replaces_existing_top_level_key_in_place()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(ConfigWithoutProviderBlock);

        editor.SetTopLevelValue("model", "gpt-5-mini");

        string expected = ConfigWithoutProviderBlock.Replace(
            "model = \"gpt-5-codex\"",
            "model = \"gpt-5-mini\"",
            StringComparison.Ordinal);
        string output = editor.ToToml();
        await Assert.That(output).IsEqualTo(expected);
        await Assert.That(TomlDocumentEditor.Parse(output).ToToml()).IsEqualTo(output);
    }

    [Test]
    public async Task SetTopLevelValue_appends_missing_key_before_first_section()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(ConfigWithoutProviderBlock);
        int firstSectionStart = ConfigWithoutProviderBlock.IndexOf(
            "[model_providers.openai]",
            StringComparison.Ordinal);
        await Assert.That(firstSectionStart >= 0).IsTrue();

        editor.SetTopLevelValue("stream_web_search", true);

        string expected = ConfigWithoutProviderBlock[..firstSectionStart]
            + "stream_web_search = true\n"
            + ConfigWithoutProviderBlock[firstSectionStart..];
        string output = editor.ToToml();
        await Assert.That(output).IsEqualTo(expected);
        await Assert.That(TomlDocumentEditor.Parse(output).ToToml()).IsEqualTo(output);
    }

    [Test]
    public async Task SetTopLevelValue_writes_integer_scalars()
    {
        TomlDocumentEditor editor = TomlDocumentEditor.Parse(ConfigWithoutProviderBlock);
        int firstSectionStart = ConfigWithoutProviderBlock.IndexOf(
            "[model_providers.openai]",
            StringComparison.Ordinal);

        editor.SetTopLevelValue("history_lines", 500);

        string expected = ConfigWithoutProviderBlock[..firstSectionStart]
            + "history_lines = 500\n"
            + ConfigWithoutProviderBlock[firstSectionStart..];
        await Assert.That(editor.ToToml()).IsEqualTo(expected);
    }

    [Test]
    public async Task Parse_refuses_unparseable_input()
    {
        TomlEditException? caught = null;
        try
        {
            TomlDocumentEditor.Parse("[unclosed");
        }
        catch (TomlEditException exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message.Contains("Cannot edit invalid TOML", StringComparison.Ordinal)).IsTrue();
        await Assert.That(caught.Diagnostics).IsNotEmpty();
    }

    [Test]
    public async Task Parse_refuses_duplicate_keys()
    {
        TomlEditException? caught = null;
        try
        {
            TomlDocumentEditor.Parse("dup = 1\ndup = 2\n");
        }
        catch (TomlEditException exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Diagnostics).IsNotEmpty();
    }

    private static Dictionary<string, object?> ProviderBlockValues()
    {
        return new Dictionary<string, object?>
        {
            ["name"] = "HarnessGateway",
            ["base_url"] = "http://localhost:9999/v1",
            ["env_key"] = "HARNESSGATEWAY_API_KEY",
            ["wire_api"] = "responses",
        };
    }
}
