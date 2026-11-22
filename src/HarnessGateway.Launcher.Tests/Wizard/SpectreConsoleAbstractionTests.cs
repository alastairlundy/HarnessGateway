using HarnessGateway.Launcher.Wizard;
using HarnessGateway.Launcher.Wizard.Models;
using Spectre.Console;

namespace HarnessGateway.Launcher.Tests.Wizard;

public class SpectreConsoleAbstractionTests
{
    [Test]
    public async Task BuildDisplayText_escapes_markup_looking_name_and_annotation()
    {
        ConsoleChoice choice = new ConsoleChoice("[add new provider]", "create a session entry");

        string display = SpectreConsoleAbstraction.BuildDisplayText(choice);

        string expected =
            $"{Markup.Escape("[add new provider]")} [dim]({Markup.Escape("create a session entry")})[/]";

        await Assert.That(display).IsEqualTo(expected);
        // Literal form: the name's brackets are doubled so Spectre's parser
        // reads them as text, while the [dim] wrapper stays real markup.
        await Assert.That(display).IsEqualTo("[[add new provider]] [dim](create a session entry)[/]");
    }

    [Test]
    public async Task BuildDisplayText_escapes_provider_name_shaped_markup_with_null_annotation()
    {
        ConsoleChoice choice = new ConsoleChoice("[red]");

        string display = SpectreConsoleAbstraction.BuildDisplayText(choice);

        await Assert.That(display).IsEqualTo(Markup.Escape("[red]"));
        await Assert.That(display).IsEqualTo("[[red]]");
    }

    [Test]
    public async Task BuildDisplayText_display_parses_as_markup_and_renders_text_literally()
    {
        // The display string is what SelectionPrompt.AddChoice receives, and
        // Spectre renders choices through its markup parser. Prove the string
        // parses to the literal choice text instead of being read as a style.
        ConsoleChoice choice = new ConsoleChoice("[add new provider]", "create a session entry");

        string display = SpectreConsoleAbstraction.BuildDisplayText(choice);

        string rendered = string.Concat(AnsiMarkup.Parse(display).Select(segment => segment.Text));

        await Assert.That(rendered).IsEqualTo("[add new provider] (create a session entry)");
    }

    [Test]
    public async Task BuildDisplayText_unescaped_display_is_the_original_crash()
    {
        // Documents why escaping is required: the pre-fix display string is
        // parsed as a style named "add" and Spectre throws.
        string unescaped = "[add new provider] [dim](create a session entry)[/]";

        Exception? caught = null;
        try
        {
            AnsiMarkup.Parse(unescaped).ToList();
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("Could not find color or style 'add'");
    }

    [Test]
    public async Task BuildDisplayText_unavailable_choice_renders_the_whole_line_red()
    {
        // Unavailable means the entire line - name plus annotation -
        // carries the red foreground, even when the line also carries an
        // incompatible indication (red takes precedence there).
        ConsoleChoice choice = new(
            "UnavailableAgent",
            "not installed; incompatible: anthropic",
            Unavailable: true,
            IncompatibleProtocols: "anthropic");

        string display = SpectreConsoleAbstraction.BuildDisplayText(choice);

        List<AnsiMarkupSegment> segments = [.. AnsiMarkup.Parse(display)];

        await Assert.That(string.Concat(segments.Select(s => s.Text)))
            .IsEqualTo("UnavailableAgent (not installed; incompatible: anthropic)");
        await Assert.That(segments.All(s => Color.Red.Equals(s.Style.Foreground))).IsTrue();
    }

    [Test]
    public async Task BuildDisplayText_incompatible_segment_renders_orange_and_the_rest_stays_dim()
    {
        // The incompatible segment replaces the dim
        // rendering with orange; the name and the rest of the annotation do
        // not pick up the orange colour.
        ConsoleChoice choice = new(
            "OpenAiOnly",
            "not installed; incompatible: anthropic",
            IncompatibleProtocols: "anthropic");

        string display = SpectreConsoleAbstraction.BuildDisplayText(choice);

        List<AnsiMarkupSegment> segments = [.. AnsiMarkup.Parse(display)];

        // The style changes must not alter the text the round-trip matches on.
        await Assert.That(string.Concat(segments.Select(s => s.Text)))
            .IsEqualTo("OpenAiOnly (not installed; incompatible: anthropic)");

        AnsiMarkupSegment incompatible = segments.Single(
            s => s.Text.Contains("incompatible: anthropic", StringComparison.Ordinal));
        await Assert.That(Color.Orange1.Equals(incompatible.Style.Foreground)).IsTrue();

        foreach (AnsiMarkupSegment other in segments.Where(s => !ReferenceEquals(s, incompatible)))
        {
            await Assert.That(Color.Orange1.Equals(other.Style.Foreground)).IsFalse();
        }
    }

    [Test]
    public async Task BuildDisplayText_available_choice_without_state_renders_unchanged()
    {
        // Available and compatible choices keep the plain rendering: no
        // colour of their own on either the name or the annotation.
        ConsoleChoice choice = new("AnthropicCapable", "not installed");

        string display = SpectreConsoleAbstraction.BuildDisplayText(choice);

        List<AnsiMarkupSegment> segments = [.. AnsiMarkup.Parse(display)];

        await Assert.That(segments.All(s => Color.Default.Equals(s.Style.Foreground))).IsTrue();
        await Assert.That(string.Concat(segments.Select(s => s.Text)))
            .IsEqualTo("AnthropicCapable (not installed)");
    }
}
