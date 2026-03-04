namespace UE5DumpUI.Services;

/// <summary>
/// Minimal parser for Valve's VDF (KeyValues) format.
/// Only extracts Steam library folder paths from libraryfolders.vdf.
/// </summary>
internal static class VdfParser
{
    /// <summary>
    /// Parse libraryfolders.vdf content and extract library paths.
    /// Returns empty list on any parse failure (never throws).
    /// </summary>
    public static List<string> ParseLibraryFolders(string vdfContent)
    {
        var paths = new List<string>();
        if (string.IsNullOrWhiteSpace(vdfContent))
            return paths;

        try
        {
            var tokens = Tokenize(vdfContent);
            ExtractPaths(tokens, paths);
        }
        catch
        {
            // Graceful failure — return whatever we found so far
        }

        return paths;
    }

    /// <summary>
    /// Tokenize VDF content into quoted strings and braces.
    /// </summary>
    private static List<string> Tokenize(string content)
    {
        var tokens = new List<string>();
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
                tokens.Add(c.ToString());
                i++;
                continue;
            }

            // Quoted string
            if (c == '"')
            {
                i++; // skip opening quote
                int start = i;
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
                tokens.Add(sb.ToString());
                continue;
            }

            // Unquoted token (rare in VDF, but handle gracefully)
            {
                int start = i;
                while (i < content.Length && !char.IsWhiteSpace(content[i])
                       && content[i] != '{' && content[i] != '}' && content[i] != '"')
                    i++;
                tokens.Add(content[start..i]);
            }
        }

        return tokens;
    }

    /// <summary>
    /// Extract "path" values from numbered entries in the VDF token stream.
    /// Expected structure: "libraryfolders" { "0" { "path" "C:\..." ... } "1" { ... } }
    /// </summary>
    private static void ExtractPaths(List<string> tokens, List<string> paths)
    {
        // Walk tokens tracking brace depth.
        // At depth 2 (inside a numbered entry), look for "path" followed by a value.
        int depth = 0;

        for (int i = 0; i < tokens.Count; i++)
        {
            string t = tokens[i];

            if (t == "{")
            {
                depth++;
                continue;
            }

            if (t == "}")
            {
                depth--;
                continue;
            }

            // At depth 2: inside "libraryfolders" -> "N" -> { ... }
            // Look for key "path" followed by a value string
            if (depth == 2
                && string.Equals(t, "path", StringComparison.OrdinalIgnoreCase)
                && i + 1 < tokens.Count
                && tokens[i + 1] != "{" && tokens[i + 1] != "}")
            {
                string path = tokens[i + 1];
                if (!string.IsNullOrWhiteSpace(path))
                    paths.Add(path);
                i++; // skip the value token
            }
        }
    }
}
