namespace UE5DumpUI.Services;

/// <summary>
/// Minimal parser for Valve's VDF (KeyValues) format.
/// Only extracts Steam library folder paths from libraryfolders.vdf.
/// </summary>
internal static class VdfParser
{
    /// <summary>What a token IS, not just what it says. Without this the extractor could
    /// not tell a quoted value <c>"path"</c> from the key <c>"path"</c>, nor a quoted
    /// string <c>"[$WIN32]"</c> from a bare platform conditional. (audit #5 AC12)</summary>
    private enum TokenKind { BraceOpen, BraceClose, Quoted, Bare }

    private readonly record struct Token(TokenKind Kind, string Text);

    /// <summary>
    /// Parse libraryfolders.vdf content and extract library paths.
    /// Returns empty list on any parse failure (never throws).
    /// </summary>
    public static List<string> ParseLibraryFolders(string vdfContent)
        => ParseLibraryFolders(vdfContent, out _);

    /// <summary>
    /// Parse libraryfolders.vdf content and extract library paths.
    /// </summary>
    /// <param name="error">
    /// <c>null</c> when the document was structurally sound all the way to the end;
    /// otherwise a short description of the FIRST structural fault. Paths accepted
    /// before that point are still returned — this feeds a game scan whose every hit is
    /// re-checked with <c>Directory.Exists</c>, so degrading is better than going blind,
    /// but the caller must be able to say so in the log instead of reporting a healthy
    /// parse that quietly found nothing.
    /// </param>
    public static List<string> ParseLibraryFolders(string vdfContent, out string? error)
    {
        var paths = new List<string>();
        error = null;
        if (string.IsNullOrWhiteSpace(vdfContent))
            return paths;

        try
        {
            error = ExtractPaths(Tokenize(vdfContent), paths);
        }
        catch (Exception ex)
        {
            // Graceful failure — return whatever we found so far
            error = $"unexpected {ex.GetType().Name}: {ex.Message}";
        }

        return paths;
    }

    /// <summary>
    /// Tokenize VDF content into quoted strings, bare words and braces.
    /// </summary>
    private static List<Token> Tokenize(string content)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < content.Length)
        {
            char c = content[i];

            // Skip whitespace
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Skip line comments
            if (c == '/' && i + 1 < content.Length && content[i + 1] == '/')
            {
                while (i < content.Length && content[i] != '\n')
                    i++;
                continue;
            }

            // Braces
            if (c == '{' || c == '}')
            {
                tokens.Add(new Token(c == '{' ? TokenKind.BraceOpen : TokenKind.BraceClose,
                                     c.ToString()));
                i++;
                continue;
            }

            // Quoted string
            if (c == '"')
            {
                i++; // skip opening quote
                var sb = new System.Text.StringBuilder();
                while (i < content.Length && content[i] != '"')
                {
                    if (content[i] == '\\' && i + 1 < content.Length)
                    {
                        // Unescape: \\ -> \, \" -> ", \n -> newline, etc.
                        char next = content[i + 1];
                        sb.Append(next switch
                        {
                            '\\' => '\\',
                            '"' => '"',
                            'n' => '\n',
                            't' => '\t',
                            _ => next
                        });
                        i += 2;
                    }
                    else
                    {
                        sb.Append(content[i]);
                        i++;
                    }
                }
                if (i < content.Length) i++; // skip closing quote
                tokens.Add(new Token(TokenKind.Quoted, sb.ToString()));
                continue;
            }

            // Unquoted token (rare in VDF, but handle gracefully)
            {
                int start = i;
                while (i < content.Length && !char.IsWhiteSpace(content[i])
                       && content[i] != '{' && content[i] != '}' && content[i] != '"')
                    i++;
                tokens.Add(new Token(TokenKind.Bare, content[start..i]));
            }
        }

        return tokens;
    }

    /// <summary>
    /// Extract "path" values from numbered entries in the VDF token stream.
    /// Expected structure: "libraryfolders" { "0" { "path" "C:\..." ... } "1" { ... } }
    ///
    /// KeyValues alternates key → value inside every block, where a value is either a
    /// string or a nested block. The old walker tracked only brace depth, so it had no
    /// idea which side of that alternation a token sat on and accepted ANY depth-2 token
    /// reading "path" as a key. Two consequences, both fixed here by carrying the
    /// alternation explicitly (audit #5 AC12):
    ///
    ///   • a VALUE could masquerade as a key. <c>"label" "path"</c> — a library the user
    ///     labelled "path" — made the walker treat the NEXT key ("contentid") as a
    ///     library folder, injecting a directory Steam never named;
    ///   • nesting was never validated. A stray <c>}</c> drove depth negative and every
    ///     later block was read one level shallow, so "path" keys stopped being seen and
    ///     the file yielded zero libraries with nothing anywhere saying why.
    ///
    /// Returns null when the document is structurally sound, otherwise a description of
    /// the first fault. Extraction stops at that point: past a structural break the token
    /// positions mean nothing, so continuing would be guessing.
    /// </summary>
    private static string? ExtractPaths(List<Token> tokens, List<string> paths)
    {
        // Depth 2 = inside "libraryfolders" -> "N" -> { ... }, the only level a library
        // "path" key can legitimately appear at.
        const int PathDepth = 2;

        int depth = 0;
        string? pendingKey = null;   // non-null => the last token was a key awaiting its value

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];

            // Bare [$PLATFORM] conditionals suffix a statement and are neither key nor
            // value. Only a BARE token can be one — a quoted "[$WIN32]" is a real string
            // and must keep its place in the alternation.
            if (t.Kind == TokenKind.Bare && t.Text.Length >= 2
                && t.Text[0] == '[' && t.Text[^1] == ']')
                continue;

            switch (t.Kind)
            {
                case TokenKind.BraceOpen:
                    // A block IS the value of the key that precedes it.
                    if (pendingKey == null)
                        return $"'{{' at token {i} does not follow a key";
                    depth++;
                    pendingKey = null;
                    break;

                case TokenKind.BraceClose:
                    if (depth == 0)
                        return $"unbalanced '}}' at token {i}";
                    if (pendingKey != null)
                        return $"key '{pendingKey}' at token {i} has no value";
                    depth--;
                    break;

                default:
                    if (pendingKey == null)
                    {
                        pendingKey = t.Text;          // this token is a KEY
                    }
                    else
                    {
                        // ...and this one is its VALUE, so it can never be read as a key.
                        if (depth == PathDepth
                            && string.Equals(pendingKey, "path", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(t.Text))
                        {
                            paths.Add(t.Text);
                        }
                        pendingKey = null;
                    }
                    break;
            }
        }

        if (depth != 0)
            return $"{depth} block(s) left unclosed at end of document";
        if (pendingKey != null)
            return $"trailing key '{pendingKey}' has no value";

        return null;
    }
}
