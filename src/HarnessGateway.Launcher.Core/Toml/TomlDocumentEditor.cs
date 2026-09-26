using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace HarnessGateway.Launcher.Core.Toml;

/// <summary>
/// Structural TOML editor built on Tomlyn's full-fidelity syntax tree.
/// Untouched parts of the document - comments, whitespace, key order and section
/// order - serialise back byte-for-byte; an edit only rewrites the block or key it
/// targets, so splices are a minimal diff against the original file.
/// </summary>
public sealed class TomlDocumentEditor
{
    private readonly DocumentSyntax _document;
    private readonly string _newLine;

    private TomlDocumentEditor(DocumentSyntax document, string newLine)
    {
        _document = document;
        _newLine = newLine;
    }

    /// <summary>
    /// Parses <paramref name="toml"/> for editing.
    /// </summary>
    /// <param name="toml">The TOML document text.</param>
    /// <returns>An editor over the parsed document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toml"/> is null.</exception>
    /// <exception cref="TomlEditException">The document cannot be parsed; the editor refuses to operate on it.</exception>
    public static TomlDocumentEditor Parse(string toml)
    {
        ArgumentNullException.ThrowIfNull(toml);

        DocumentSyntax document = SyntaxParser.Parse(toml);
        if (document.HasErrors)
        {
            string[] diagnostics = [.. document.Diagnostics.Select(diagnostic => diagnostic.ToString())];
            throw new TomlEditException(
                $"Cannot edit invalid TOML ({diagnostics.Length} diagnostic(s)): {string.Join("; ", diagnostics)}",
                diagnostics);
        }

        return new TomlDocumentEditor(document, DetectNewLine(toml));
    }

    /// <summary>
    /// Detects whether the document contains a <c>[blockName]</c> section. Dotted names
    /// match the full header, for example <c>model_providers.harnessgateway</c>.
    /// Table arrays (<c>[[name]]</c>) do not match.
    /// </summary>
    public bool BlockExists(string blockName)
    {
        return FindBlock(SplitName(blockName, nameof(blockName))) is not null;
    }

    /// <summary>
    /// Inserts the named block when absent, or rewrites an existing block so it contains
    /// exactly <paramref name="values"/>. Keys already present keep their position and
    /// surrounding comments; keys missing from <paramref name="values"/> are removed.
    /// Supported values: string, bool, int, long, float, double.
    /// </summary>
    public void UpsertBlock(string blockName, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        string[] path = SplitName(blockName, nameof(blockName));

        TableSyntax? table = FindBlock(path);
        if (table is null)
        {
            table = new TableSyntax(BuildKey(path));
            table.EndOfLineToken = new SyntaxToken(TokenKind.NewLine, _newLine);
            List<SyntaxTrivia>? separator = BuildAppendSeparator();
            if (separator is not null)
            {
                table.LeadingTrivia = separator;
            }
            _document.Tables.Add(table);
        }

        ApplyValues(table, values);
    }

    /// <summary>
    /// Sets a top-level key, replacing its value in place when it already exists and
    /// appending it after the last top-level key (before the first section) otherwise.
    /// Supported values: string, bool, int, long, float, double.
    /// </summary>
    public void SetTopLevelValue(string key, object? value)
    {
        string[] path = SplitName(key, nameof(key));

        foreach (KeyValueSyntax item in _document.KeyValues)
        {
            if (item.Key is not null && PathEquals(ResolveKey(item.Key), path))
            {
                item.Value = CreateValue(value);
                return;
            }
        }

        _document.KeyValues.Add(CreateKeyValue(path, value));
    }

    /// <summary>
    /// Serialises the document back to TOML text with all edits applied.
    /// </summary>
    public string ToToml()
    {
        return _document.ToString();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return ToToml();
    }

    private void ApplyValues(TableSyntax table, IReadOnlyDictionary<string, object?> values)
    {
        List<(string[] Path, object? Value)> payload = [];
        foreach (KeyValuePair<string, object?> pair in values)
        {
            payload.Add((SplitName(pair.Key, nameof(values)), pair.Value));
        }

        List<(KeyValueSyntax Node, string[] Path)> existing = [];
        foreach (KeyValueSyntax item in table.Items)
        {
            existing.Add((item, item.Key is null ? [] : ResolveKey(item.Key)));
        }

        HashSet<KeyValueSyntax> survivors = [];
        foreach ((KeyValueSyntax node, string[] itemPath) in existing)
        {
            foreach ((string[] payloadPath, object? payloadValue) in payload)
            {
                if (!PathEquals(payloadPath, itemPath))
                {
                    continue;
                }

                node.Value = CreateValue(payloadValue);
                survivors.Add(node);
                break;
            }
        }

        for (int i = table.Items.ChildrenCount - 1; i >= 0; i--)
        {
            KeyValueSyntax item = table.Items.GetChild(i)!;
            if (survivors.Contains(item))
            {
                continue;
            }

            MoveTrailingTrivia(table, i, survivors);
            table.Items.RemoveChildAt(i);
        }

        foreach ((string[] path, object? value) in payload)
        {
            if (existing.Any(entry => PathEquals(entry.Path, path)))
            {
                continue;
            }

            table.Items.Add(CreateKeyValue(path, value));
        }
    }

    /// <summary>
    /// When a key-value line is removed, the trivia after it (blank lines and comments
    /// introducing the next line) belongs to the surrounding text, not to the removed
    /// key - move it to the previous surviving line so it survives the edit.
    /// </summary>
    private static void MoveTrailingTrivia(TableSyntax table, int removedIndex, HashSet<KeyValueSyntax> survivors)
    {
        KeyValueSyntax removed = table.Items.GetChild(removedIndex)!;
        List<SyntaxTrivia>? orphaned = removed.EndOfLineToken?.TrailingTrivia ?? removed.TrailingTrivia;
        if (orphaned is null || orphaned.Count == 0)
        {
            return;
        }

        SyntaxNode? target = null;
        for (int i = removedIndex - 1; i >= 0; i--)
        {
            KeyValueSyntax previous = table.Items.GetChild(i)!;
            if (survivors.Contains(previous))
            {
                target = previous.EndOfLineToken is not null ? previous.EndOfLineToken : previous;
                break;
            }
        }

        target ??= table.EndOfLineToken;
        if (target is null)
        {
            return;
        }

        target.TrailingTrivia = target.TrailingTrivia is null
            ? orphaned
            : [.. target.TrailingTrivia, .. orphaned];

        removed.EndOfLineToken!.TrailingTrivia = null;
        removed.TrailingTrivia = null;
    }

    private TableSyntax? FindBlock(string[] path)
    {
        foreach (TableSyntaxBase candidate in _document.Tables)
        {
            if (candidate is TableSyntax table && table.Name is not null && PathEquals(ResolveKey(table.Name), path))
            {
                return table;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the separator that must precede a block appended at the end of the
    /// document: close an unterminated last line and keep exactly one blank line
    /// between previous structural content and the new block. Pure trivia documents
    /// (empty or comment-only) need no separator.
    /// </summary>
    private List<SyntaxTrivia>? BuildAppendSeparator()
    {
        string text = _document.ToString();
        bool hasStructure = _document.KeyValues.ChildrenCount > 0 || _document.Tables.ChildrenCount > 0;
        if (text.Length == 0 || !hasStructure)
        {
            return null;
        }

        List<SyntaxTrivia> separator = [];
        if (!text.EndsWith(_newLine, StringComparison.Ordinal))
        {
            separator.Add(CreateNewLineTrivia());
            text += _newLine;
        }

        if (!text.EndsWith(_newLine + _newLine, StringComparison.Ordinal))
        {
            separator.Add(CreateNewLineTrivia());
        }

        return separator;
    }

    private KeyValueSyntax CreateKeyValue(string[] path, object? value)
    {
        KeyValueSyntax keyValue = new KeyValueSyntax(BuildKey(path), CreateValue(value));
        keyValue.EndOfLineToken = new SyntaxToken(TokenKind.NewLine, _newLine);
        return keyValue;
    }

    private static KeySyntax BuildKey(string[] path)
    {
        KeySyntax key = new KeySyntax(path[0]);
        for (int i = 1; i < path.Length; i++)
        {
            key.DotKeys.Add(new DottedKeyItemSyntax(path[i]));
        }

        return key;
    }

    private static ValueSyntax CreateValue(object? value)
    {
        return value switch
        {
            string text => new StringValueSyntax(text),
            bool flag => new BooleanValueSyntax(flag),
            int number => new IntegerValueSyntax(number),
            long number => new IntegerValueSyntax(number),
            float number => new FloatValueSyntax(number),
            double number => new FloatValueSyntax(number),
            _ => throw new ArgumentException(
                $"Unsupported TOML value of type '{value?.GetType().FullName ?? "null"}'. Supported: string, bool, int, long, float, double.",
                nameof(value)),
        };
    }

    private static string[] ResolveKey(KeySyntax key)
    {
        List<string> parts = [ResolveKeyPart(key.Key)];
        foreach (DottedKeyItemSyntax dotKey in key.DotKeys)
        {
            parts.Add(ResolveKeyPart(dotKey.Key));
        }

        return [.. parts];
    }

    private static string ResolveKeyPart(BareKeyOrStringValueSyntax? part)
    {
        return part switch
        {
            BareKeySyntax bare => bare.Key?.Text ?? string.Empty,
            StringValueSyntax text => text.Value ?? string.Empty,
            _ => string.Empty,
        };
    }

    private static bool PathEquals(string[] left, string[] right)
    {
        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private static string[] SplitName(string name, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A dotted TOML name is required.", parameterName);
        }

        string[] parts = name.Split('.');
        if (parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"'{name}' is not a valid dotted TOML name.", parameterName);
        }

        return parts;
    }

    private static string DetectNewLine(string toml)
    {
        return toml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    private SyntaxTrivia CreateNewLineTrivia()
    {
        return new SyntaxTrivia(TokenKind.NewLine, _newLine);
    }
}
