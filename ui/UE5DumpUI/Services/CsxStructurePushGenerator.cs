using System.Globalization;
using System.Text;

namespace UE5DumpUI.Services;

/// <summary>[AOBM-DISSECT-INJECT] What <see cref="CsxStructurePushGenerator.Generate"/> made: the AA script, the root
/// structure's name, and how many structures and elements it builds.</summary>
public sealed record CsxPushScript(string Script, string RootName, int Structures, int Elements);

/// <summary>
/// [AOBM-DISSECT-INJECT] The CSX half: turn Live Walker's own CSX export into a CE Lua script that builds the same
/// structures in Structure Dissect, pushed through the CE plugin's <c>CreateAAScript</c>. AOBMaker declined a CSX
/// command (R12: no XML parsing inside CE), so the XML is read HERE, from our own export, and CE only runs
/// <c>createStructure</c> / <c>addElement</c>.
/// <para>It mirrors what CE's own "Import from file" does with the same CSX (7.5 <c>StructuresFrm2</c>
/// <c>TStructelement.createFromXMLNode</c>): a nested <c>&lt;Structure&gt;</c> becomes a child only under a Pointer
/// element, and only the root is listed globally. Two differences are deliberate:</para>
/// <list type="bullet">
/// <item>CE 7.5's elements have no <c>BitStart</c> / <c>BitSize</c> (7.7 added them; assigning one raises), so on CE
/// before 7.7 a Binary bit is built as its byte, named "(bit N)" -- the pre-7.7 CSX form, chosen at run time;</item>
/// <item>the record is enabled THROUGH the plugin, so a failure raises (<c>error</c>) instead of a
/// <c>showMessage</c>: a modal there holds the plugin's pipe for every client ([AOBM-TRAINER-SETUP-MODAL]). CE then
/// refuses the activation and the plugin reports its reason.</item>
/// </list>
/// The build is all or nothing: a failure destroys every structure it made and registers none, because a
/// half-built structure presented as the result is worse than none (the same rule as ue5_dissect.lua's build).
/// </summary>
public static class CsxStructurePushGenerator
{
    /// <summary>The pushed record's description prefix; the root structure's name follows it.</summary>
    public const string RecordPrefix = "UE5CEDumper: Structure ";

    private sealed record Elem(int Offset, string Name, string Vartype, int Bytesize, char Display,
                               int? BitStart, int? BitSize, Struct? Child);

    private sealed record Struct(string Name, List<Elem> Elements);

    public static CsxPushScript Generate(string csx)
    {
        var root = CsxReader.Parse(csx).Child("Structures")?.Child("Structure")
            ?? throw new FormatException("the CSX has no <Structure>");
        var model = ParseStruct(root);

        // Children before their parents: an element can only point at a structure that already exists.
        var order = new List<Struct>();
        Collect(model, order);
        var index = new Dictionary<Struct, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < order.Count; i++) index[order[i]] = i + 1;

        var sb = new StringBuilder(4096 + 96 * order.Sum(s => s.Elements.Count));
        Line(sb, "[ENABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        CeLuaHygiene.AppendDebugPreamble(sb);
        CeLuaHygiene.AppendAttribution(sb);
        Line(sb, "-- ================================================================");
        Line(sb, "-- A Structure Dissect structure built from UE5CEDumper's CSX export.");
        Line(sb, "-- Ticking (re)builds it; the record unticks itself afterwards.");
        Line(sb, "-- ================================================================");
        Line(sb, $"local NAME = '{CeLuaHygiene.EscapeLuaString(model.Name)}'");
        Line(sb, "-- CE 7.5's structure elements have no BitStart / BitSize (7.7 added them): a bit is shown as its byte there");
        Line(sb, "local hasBits = getCEVersion() >= 7.7");
        Line(sb, "local DM = { u = 'dtUnsignedInteger', s = 'dtSignedInteger', h = 'dtHexadecimal' }");
        Line(sb, "local S, made = {}, {}");
        Line(sb, "local function new(i, name)");
        Line(sb, "  local s = createStructure(name)");
        Line(sb, "  made[#made + 1] = s");
        Line(sb, "  s.beginUpdate()");
        Line(sb, "  S[i] = s");
        Line(sb, "end");
        Line(sb, "local function el(s, off, name, vt, dm, size, bit, bits, child)");
        Line(sb, "  local e = s.addElement()");
        Line(sb, "  e.Offset = off");
        Line(sb, "  e.Name = name");
        Line(sb, "  if vt == vtBinary and not hasBits then");
        Line(sb, "    e.Vartype = vtByte");
        Line(sb, "    e.Name = name .. ' (bit ' .. bit .. ')'");
        Line(sb, "  else");
        Line(sb, "    e.Vartype = vt");
        Line(sb, "    if vt == vtBinary then e.BitStart = bit e.BitSize = bits end");
        Line(sb, "  end");
        Line(sb, "  if size then e.Bytesize = size end   -- strings only: a basic type's size is fixed");
        Line(sb, "  e.DisplayMethod = DM[dm]");
        Line(sb, "  if child then e.ChildStruct = child end");
        Line(sb, "end");
        Line(sb, "local ok, err = pcall(function()");
        foreach (var s in order)
        {
            int si = index[s];
            Line(sb, s == model ? $"  new({si}, NAME)" : $"  new({si}, '{CeLuaHygiene.EscapeLuaString(s.Name)}')");
            foreach (var e in s.Elements)
                Line(sb, "  " + ElementCall(si, e, index));
        }
        Line(sb, "end)");
        Line(sb, "for _, s in ipairs(made) do s.endUpdate() end");
        Line(sb, "if not ok then");
        Line(sb, "  for _, s in ipairs(made) do pcall(function() s.destroy() end) end");
        Line(sb, "  error('[UE5CEDumper] structure ' .. NAME .. ' was not built: ' .. tostring(err), 0)");
        Line(sb, "end");
        Line(sb, "-- A second push of the same structure replaces the first.");
        Line(sb, "for i = getStructureCount() - 1, 0, -1 do");
        Line(sb, "  local old = getStructure(i)");
        Line(sb, "  if old and old.Name == NAME then old.removeFromGlobalStructureList() end");
        Line(sb, "end");
        Line(sb, $"S[{index[model]}].addToGlobalStructureList()");
        Line(sb, $"dbg('[UE5CEDumper] structure ' .. NAME .. ' built ({order.Count} structures, {order.Sum(x => x.Elements.Count)} elements)')");
        CeLuaHygiene.AppendDeferredUntick(sb);
        CeLuaHygiene.AppendCloseOnSuccess(sb);
        Line(sb, "{$asm}");
        Line(sb, "[DISABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        CeLuaHygiene.AppendDebugPreamble(sb);
        Line(sb, "-- Nothing to undo: the structure stays in Structure Dissect (and is saved with the table).");
        CeLuaHygiene.AppendCloseOnSuccess(sb);
        Line(sb, "{$asm}");

        return new CsxPushScript(sb.ToString(), model.Name, order.Count, order.Sum(x => x.Elements.Count));
    }

    private static Struct ParseStruct(CsxReader.Node node)
    {
        var s = new Struct(node.Attr("Name") ?? "", new List<Elem>());
        var elements = node.Child("Elements")?.Children.Where(c => c.Name == "Element")
                       ?? Enumerable.Empty<CsxReader.Node>();
        foreach (var e in elements)
        {
            string vartype = e.Attr("Vartype") ?? "";
            // As CE's import: a nested structure counts only under a Pointer element.
            var childNode = vartype == "Pointer" ? e.Child("Structure") : null;
            s.Elements.Add(new Elem(
                Int(e, "Offset") ?? 0,
                e.Attr("Description") ?? "",
                vartype,
                Int(e, "Bytesize") ?? 0,
                e.Attr("DisplayMethod") switch
                {
                    "signed integer" => 's',
                    "hexadecimal" => 'h',
                    _ => 'u',
                },
                Int(e, "BitStart"),
                Int(e, "BitSize"),
                childNode == null ? null : ParseStruct(childNode)));
        }
        return s;
    }

    private static int? Int(CsxReader.Node e, string attr)
        => int.TryParse(e.Attr(attr), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static void Collect(Struct s, List<Struct> order)
    {
        foreach (var e in s.Elements)
            if (e.Child != null) Collect(e.Child, order);
        order.Add(s);
    }

    private static string ElementCall(int si, Elem e, Dictionary<Struct, int> index)
    {
        string vt = e.Vartype switch
        {
            "Byte" => "vtByte",
            "2 Bytes" => "vtWord",
            "4 Bytes" => "vtDword",
            "8 Bytes" => "vtQword",
            "Float" => "vtSingle",
            "Double" => "vtDouble",
            "String" => "vtString",
            "Unicode String" => "vtUnicodeString",
            "Array of byte" => "vtByteArray",
            "Binary" => "vtBinary",
            "Pointer" => "vtPointer",
            // CsxExportService writes none other; an unknown one must not become a plausible wrong type in CE.
            _ => throw new FormatException($"CSX Vartype '{e.Vartype}' has no Cheat Engine structure type here"),
        };
        bool sized = vt is "vtString" or "vtUnicodeString" or "vtByteArray";
        string size = sized ? e.Bytesize.ToString(CultureInfo.InvariantCulture) : "nil";
        string bit = vt == "vtBinary" ? (e.BitStart ?? 0).ToString(CultureInfo.InvariantCulture) : "nil";
        string bits = vt == "vtBinary" ? (e.BitSize ?? 1).ToString(CultureInfo.InvariantCulture) : "nil";
        string child = e.Child != null ? $"S[{index[e.Child]}]" : "nil";
        return $"el(S[{si}], {e.Offset.ToString(CultureInfo.InvariantCulture)}, '{CeLuaHygiene.EscapeLuaString(e.Name)}', " +
               $"{vt}, '{e.Display}', {size}, {bit}, {bits}, {child})";
    }

    private static void Line(StringBuilder sb, string text) => sb.Append(text).Append('\n');
}

/// <summary>
/// [AOBM-DISSECT-INJECT] Just enough XML for the CSX <see cref="CsxExportService"/> writes: elements with double-quoted
/// attributes, the five entities it escapes (plus numeric ones), and the comment or declaration it may lead with.
/// XDocument did this job first and pulled System.Private.Xml into the trimmed binary -- +3.3 MB, measured on build
/// 3612, for one parse of our own output. Anything outside that subset is refused, not guessed at.
/// </summary>
internal static class CsxReader
{
    internal sealed class Node(string name)
    {
        public string Name { get; } = name;
        public Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);
        public List<Node> Children { get; } = new();
        public string? Attr(string name) => Attributes.TryGetValue(name, out var v) ? v : null;
        public Node? Child(string name) => Children.FirstOrDefault(c => c.Name == name);
    }

    /// <summary>A nameless document node whose children are the top-level elements.</summary>
    public static Node Parse(string xml)
    {
        var doc = new Node("");
        var stack = new Stack<Node>();
        stack.Push(doc);
        int i = 0;
        while (i < xml.Length)
        {
            int lt = xml.IndexOf('<', i);
            if (lt < 0) break;
            if (Starts(xml, lt, "<!--")) { i = After(xml, lt, "-->"); continue; }
            if (Starts(xml, lt, "<?")) { i = After(xml, lt, "?>"); continue; }
            if (Starts(xml, lt, "</"))
            {
                int gt = xml.IndexOf('>', lt);
                if (gt < 0) throw Bad(lt, "an unterminated end tag");
                var name = xml[(lt + 2)..gt].Trim();
                if (stack.Count < 2 || stack.Peek().Name != name)
                    throw Bad(lt, $"</{name}> does not close an open element");
                stack.Pop();
                i = gt + 1;
                continue;
            }
            i = lt + 1;
            int n = i;
            while (n < xml.Length && (char.IsLetterOrDigit(xml[n]) || xml[n] is '_' or '-' or '.' or ':')) n++;
            if (n == i) throw Bad(lt, "a '<' that starts no element");
            var node = new Node(xml[i..n]);
            i = n;
            while (true)
            {
                while (i < xml.Length && char.IsWhiteSpace(xml[i])) i++;
                if (i >= xml.Length) throw Bad(lt, "an unterminated start tag");
                if (Starts(xml, i, "/>")) { stack.Peek().Children.Add(node); i += 2; break; }
                if (xml[i] == '>') { stack.Peek().Children.Add(node); stack.Push(node); i++; break; }
                int eq = xml.IndexOf('=', i);
                if (eq < 0 || eq + 1 >= xml.Length || xml[eq + 1] != '"')
                    throw Bad(i, "an attribute without a double-quoted value");
                int close = xml.IndexOf('"', eq + 2);
                if (close < 0) throw Bad(i, "an unterminated attribute value");
                node.Attributes[xml[i..eq].Trim()] = Decode(xml[(eq + 2)..close], i);
                i = close + 1;
            }
        }
        if (stack.Count != 1) throw new FormatException($"the CSX ends with <{stack.Peek().Name}> still open");
        return doc;
    }

    private static string Decode(string s, int at)
    {
        if (s.IndexOf('&') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '&') { sb.Append(s[i]); continue; }
            int semi = s.IndexOf(';', i);
            if (semi < 0) throw Bad(at, "an unterminated entity");
            var ent = s[(i + 1)..semi];
            sb.Append(ent switch
            {
                "amp" => "&",
                "lt" => "<",
                "gt" => ">",
                "quot" => "\"",
                "apos" => "'",
                _ when ent.StartsWith("#x", StringComparison.Ordinal)
                       && int.TryParse(ent[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hx)
                    => char.ConvertFromUtf32(hx),
                _ when ent.StartsWith('#')
                       && int.TryParse(ent[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dc)
                    => char.ConvertFromUtf32(dc),
                _ => throw Bad(at, $"the unknown entity &{ent};"),
            });
            i = semi;
        }
        return sb.ToString();
    }

    private static bool Starts(string s, int at, string what)
        => string.CompareOrdinal(s, at, what, 0, what.Length) == 0;

    private static int After(string s, int at, string end)
    {
        int e = s.IndexOf(end, at, StringComparison.Ordinal);
        if (e < 0) throw Bad(at, $"no closing {end}");
        return e + end.Length;
    }

    private static FormatException Bad(int at, string what) => new($"CSX at character {at}: {what}");
}
