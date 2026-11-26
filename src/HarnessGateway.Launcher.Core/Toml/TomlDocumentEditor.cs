using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace HarnessGateway.Launcher.Core.Toml;

/// <summary>
/// Structural TOML editor built on Tomlyn's full-fidelity syntax tree.
/// Untouched parts of the document - comments, whitespace, key order, and section
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

        return new(document, DetectNewLine(toml));
    }

    /// <summary>
    /// Detects whether the document contains a <c>[blockName]</c> section. Dotted names
    /// match the full header, for example <c>model_providers.harnessgateway</c>.
    /// Table arrays (<c>[[name]]</c>) do not match.
    /// </summary>
    public bool BlockExists(string blockName)
    {
        return FindBlock(SplitName(blockName, nameof(blockName))).Success;
    }

    /// <summary>
    /// Detects whether the dotted <paramref name="name"/> is already claimed anywhere in
    /// the document: as a <c>[name]</c> or <c>[[name]]</c> header, as a key path (for
    /// example an inline-table value under a parent section), or as a dotted key that
    /// would implicitly define the name (for example <c>name.sub = ...</c>). Broader
    /// than <see cref="BlockExists"/>, which matches plain table headers only.
    /// </summary>
    public bool NameInUse(string name)
    {
        string[] path = SplitName(name, nameof(name));

        foreach (TableSyntaxBase table in _document.Tables)
        {
            if (table.Name is not null && ClaimsOrExtends(ResolveKey(table.Name), path))
            {
                return true;
            }

            string[] tablePath = table.Name is null ? [] : ResolveKey(table.Name);
            foreach (KeyValueSyntax item in table.Items)
            {
                if (item.Key is null)
                {
                    continue;
                }

                string[] fullPath = [.. tablePath, .. ResolveKey(item.Key)];
                if (ClaimsOrExtends(fullPath, path))
                {
                    return true;
                }
            }
        }

        foreach (KeyValueSyntax item in _document.KeyValues)
        {
            if (item.Key is null)
            {
                continue;
            }

            string[] keyPath = ResolveKey(item.Key);
            // A scalar key at an ancestor position (e.g. a top-level
            // `model_providers = "..."`) also blocks the name.
            if (ClaimsOrExtends(keyPath, path) || ExtendsOrMatches(keyPath, path))
            {
                return true;
            }
        }

        return false;
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

        Result<TableSyntax> existing = FindBlock(path);
        if (existing.Success)
        {
            ApplyValues(existing.Value, values);
            return;
        }

        TableSyntax created = new(BuildKey(path));
        created.EndOfLineToken = new(TokenKind.NewLine, _newLine);
        List<SyntaxTrivia>? separator = BuildAppendSeparator();
        if (separator is not null)
        {
            created.LeadingTrivia = separator;
        }

        _document.Tables.Add(created);
        ApplyValues(created, values);
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
            KeyValueSyntax item = ChildAt(table.Items, i);
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
        KeyValueSyntax removed = ChildAt(table.Items, removedIndex);
        List<SyntaxTrivia>? orphaned = removed.EndOfLineToken?.TrailingTrivia ?? removed.TrailingTrivia;
        if (orphaned is null || orphaned.Count == 0)
        {
            return;
        }

        SyntaxNode? target = null;
        for (int i = removedIndex - 1; i >= 0; i--)
        {
            KeyValueSyntax previous = ChildAt(table.Items, i);
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

        if (removed.EndOfLineToken is { } eol)
        {
            eol.TrailingTrivia = null;
        }

        removed.TrailingTrivia = null;
    }

    /// <summary>
    /// Tomlyn's <c>GetChild</c> is annotated as nullable even though in-range
    /// access never returns null (out-of-range access throws), so bind the child
    /// explicitly here rather than suppressing the warning at each call site.
    /// </summary>
    private static KeyValueSyntax ChildAt(SyntaxList<KeyValueSyntax> children, int index)
    {
        if (children.GetChild(index) is { } child)
        {
            return child;
        }

        throw new InvalidOperationException(
            $"The parsed TOML document has no key-value child at index {index}.");
    }

    private Result<TableSyntax> FindBlock(string[] path)
    {
        foreach (TableSyntaxBase candidate in _document.Tables)
        {
            if (candidate is TableSyntax table && table.Name is not null && PathEquals(ResolveKey(table.Name), path))
            {
                return Result.Ok(table);
            }
        }

        return Result.Fail<TableSyntax>("");
    }

    /// <summary>
    /// Builds the separator that must precede a block appended at the end of the
    /// document: close an unterminated last line and keep exactly one blank line
    /// between previous structural content and the new block. Pure trivia documents
    /// (empty or comment-only) need no separator.
    /// </summary>
    /// <remarks>
    /// Probes the document's serialized tail from the syntax tree instead of
    /// serializing the whole document: the tail after the last structural node is
    /// exactly the end-of-line token, its trailing trivia, the node's trailing
    /// trivia, the carrying table's trailing trivia, and the document's trailing
    /// trivia, in serialization order - so the same
    /// <c>EndsWith</c> checks run over the tail alone instead of the full text.
    /// </remarks>
    private List<SyntaxTrivia>? BuildAppendSeparator()
    {
        bool hasStructure = _document.KeyValues.ChildrenCount > 0 || _document.Tables.ChildrenCount > 0;
        if (!hasStructure)
        {
            return null;
        }

        string tail = ComputeDocumentTail();

        List<SyntaxTrivia> separator = [];
        if (!tail.EndsWith(_newLine, StringComparison.Ordinal))
        {
            separator.Add(CreateNewLineTrivia());
            tail += _newLine;
        }

        if (!tail.EndsWith(_newLine + _newLine, StringComparison.Ordinal))
        {
            separator.Add(CreateNewLineTrivia());
        }

        return separator;
    }

    /// <summary>
    /// The serialized text that follows the last structural content of the
    /// document. Tomlyn distributes the document tail onto the deepest last
    /// node's end-of-line token and trailing trivia (the document node does not
    /// carry it), so walking that chain reproduces the exact suffix of the
    /// serialized output without serializing the untouched prefix.
    /// </summary>
    private string ComputeDocumentTail()
    {
        StringBuilder tail = new();

        if (_document.Tables.ChildrenCount > 0)
        {
            TableSyntaxBase lastTable =
                (TableSyntaxBase)_document.Tables.GetChild(_document.Tables.ChildrenCount - 1)!;
            SyntaxNode lastNode = lastTable.Items.ChildrenCount > 0
                ? ChildAt(lastTable.Items, lastTable.Items.ChildrenCount - 1)
                : lastTable;
            AppendTailFrom(tail, lastNode);

            // The carrying table serializes its own trailing trivia after its
            // last item, before anything that follows the document.
            AppendTriviaText(tail, lastTable.TrailingTrivia);
        }
        else if (_document.KeyValues.ChildrenCount > 0)
        {
            AppendTailFrom(tail, ChildAt(_document.KeyValues, _document.KeyValues.ChildrenCount - 1));
        }

        AppendTriviaText(tail, _document.TrailingTrivia);
        return tail.ToString();
    }

    private static void AppendTailFrom(StringBuilder tail, SyntaxNode node)
    {
        SyntaxToken? endOfLine = node switch
        {
            KeyValueSyntax key => key.EndOfLineToken,
            TableSyntaxBase table => table.EndOfLineToken,
            _ => null,
        };

        if (endOfLine is not null)
        {
            tail.Append(endOfLine.Text ?? string.Empty);
            AppendTriviaText(tail, endOfLine.TrailingTrivia);
        }

        AppendTriviaText(tail, node.TrailingTrivia);
    }

    private static void AppendTriviaText(StringBuilder tail, List<SyntaxTrivia>? trivia)
    {
        if (trivia is null)
        {
            return;
        }

        foreach (SyntaxTrivia piece in trivia)
        {
            if (!string.IsNullOrEmpty(piece.Text))
            {
                tail.Append(piece.Text);
            }
        }
    }

    private KeyValueSyntax CreateKeyValue(string[] path, object? value)
    {
        KeyValueSyntax keyValue = new(BuildKey(path), CreateValue(value));
        keyValue.EndOfLineToken = new(TokenKind.NewLine, _newLine);
        return keyValue;
    }

    private static KeySyntax BuildKey(string[] path)
    {
        KeySyntax key = new(path[0]);
        for (int i = 1; i < path.Length; i++)
        {
            key.DotKeys.Add(new(path[i]));
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

    /// <summary>
    /// True when <paramref name="candidate"/> equals <paramref name="path"/> or is
    /// strictly deeper than it (a header or dotted key that implicitly defines
    /// <paramref name="path"/>). A shallower candidate does not conflict here - a plain
    /// parent section header is normal TOML.
    /// </summary>
    private static bool ClaimsOrExtends(string[] candidate, string[] path)
    {
        if (candidate.Length < path.Length)
        {
            return false;
        }

        return path.SequenceEqual(candidate[..path.Length], StringComparer.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="candidate"/> equals <paramref name="path"/> or is a
    /// strict ancestor of it.
    /// </summary>
    private static bool ExtendsOrMatches(string[] candidate, string[] path)
    {
        if (candidate.Length > path.Length)
        {
            return false;
        }

        return candidate.SequenceEqual(path[..candidate.Length], StringComparer.Ordinal);
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
        return new(TokenKind.NewLine, _newLine);
    }
}
