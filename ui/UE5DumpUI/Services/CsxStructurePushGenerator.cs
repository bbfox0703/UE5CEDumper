using System.Globalization;
using System.Text;
using System.Xml.Linq;

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
        var root = XDocument.Parse(csx).Root?.Element("Structure")
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

    private static Struct ParseStruct(XElement node)
    {
        var s = new Struct((string?)node.Attribute("Name") ?? "", new List<Elem>());
        foreach (var e in node.Element("Elements")?.Elements("Element") ?? Enumerable.Empty<XElement>())
        {
            string vartype = (string?)e.Attribute("Vartype") ?? "";
            // As CE's import: a nested structure counts only under a Pointer element.
            var childNode = vartype == "Pointer" ? e.Element("Structure") : null;
            s.Elements.Add(new Elem(
                Int(e, "Offset") ?? 0,
                (string?)e.Attribute("Description") ?? "",
                vartype,
                Int(e, "Bytesize") ?? 0,
                ((string?)e.Attribute("DisplayMethod")) switch
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

    private static int? Int(XElement e, string attr)
        => int.TryParse((string?)e.Attribute(attr), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : null;

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
