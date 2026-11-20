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
}
