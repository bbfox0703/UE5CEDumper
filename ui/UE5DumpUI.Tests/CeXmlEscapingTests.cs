using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The exported CE table must be well-formed XML no matter what the game's strings
/// contain (audit #4 B3).
///
/// <para>Description text is arbitrary game memory — TMap keys, TSet elements,
/// soft-object paths, DataTable row names. A single <c>&amp;</c> anywhere in it used
/// to produce an invalid entity reference, and Cheat Engine rejects the <b>whole
/// document</b>: a multi-thousand-entry export imports as nothing, with no indication
/// which record was at fault. A <c>TMap</c> key like <c>Bow &amp; Arrow</c> is the kind
/// of thing that hits this on an ordinary game.</para>
///
/// <para>These tests parse the output with <see cref="XDocument"/> rather than
/// string-matching for entities. Asserting on <c>"&amp;amp;"</c> would pass for output
/// that is still malformed somewhere else; asking a real parser is the property that
/// actually matters — and it is exactly the check nothing in this suite performed
/// before. <c>CheatTableBuilder</c> escaped its output, <c>CeXmlExportService</c> did
/// not, and no test noticed the difference.</para>
/// </summary>
public class CeXmlEscapingTests
{
    /// <summary>Every XML metacharacter, in one string, in a plausible shape.</summary>
    private const string Nasty = "Bow & Arrow <Rare> \"Legendary\" & 'Cursed'";

    private static LiveFieldValue NastyMapField(string key) => new()
    {
        Name = "LootTable",
        TypeName = "MapProperty",
        Offset = 0x50,
        Size = 80,
        MapCount = 1,
        MapKeyType = "StrProperty",
        MapValueType = "IntProperty",
        MapKeySize = 16,
        MapValueSize = 4,
        MapValueOffset = 16,
        MapDataAddr = "0x9000",
        MapElements = new List<ContainerElementValue>
        {
            new() { Index = 0, Key = key, Value = "100", ValueHex = "64000000" },
        },
    };

    private static string Xml(params LiveFieldValue[] fields) =>
        CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "MyObj", "UMyClass", fields);

    [Fact]
    public void An_ampersand_alone_breaks_an_unescaped_export()
    {
        // The minimal reproducer, kept first and separate so a regression names the
        // cause instead of pointing at the kitchen-sink string below.
        var doc = XDocument.Parse(Xml(NastyMapField("R&D")));
        Assert.NotNull(doc.Root);
    }

    [Fact]
    public void Every_xml_metacharacter_in_a_map_key_still_yields_well_formed_xml()
    {
        var doc = XDocument.Parse(Xml(NastyMapField(Nasty)));
        Assert.NotNull(doc.Root);
    }

    [Fact]
    public void The_text_survives_the_round_trip_unmangled()
    {
        // Escaping has to be reversible: the user must be able to read the key in CE.
        // A test that only asserted "the document parses" would also pass for output
        // that dropped or corrupted the characters instead of encoding them.
        var doc = XDocument.Parse(Xml(NastyMapField(Nasty)));
        var descriptions = doc.Descendants("Description").Select(d => d.Value).ToList();

        Assert.Contains(descriptions, v => v.Contains("Bow & Arrow"));
        Assert.Contains(descriptions, v => v.Contains("<Rare>"));
    }

    [Fact]
    public void A_less_than_in_a_key_cannot_open_a_phantom_element()
    {
        // '<' is the worse half: '&' yields an invalid entity, but '<' makes the parser
        // start reading a tag, so the damage is not even local to the record.
        var doc = XDocument.Parse(Xml(NastyMapField("HP < 50%")));
        Assert.NotNull(doc.Root);
        Assert.Contains(doc.Descendants("Description").Select(d => d.Value),
            v => v.Contains("HP < 50%"));
    }

    [Fact]
    public void The_hierarchical_emitter_escapes_too()
    {
        // A different code path with its own Description sites — escaping is not
        // supposed to be a property of one emitter.
        var xml = CeXmlExportService.GenerateHierarchicalXml(
            "\"Game.exe\"+1000", "MyObj",
            new List<UE5DumpUI.ViewModels.BreadcrumbItem>(),
            new[] { NastyMapField(Nasty) });

        Assert.NotNull(XDocument.Parse(xml).Root);
    }

    // ----- <DropDownList> bodies (audit #5 W4) --------------------------------
    //
    // Every test above puts the game string in a map KEY, which lands in <Description>.
    // That is the whole reason this defect survived audit #4 B3: the escaping was added
    // to Descriptions, and the OTHER place a game string reaches the XML — the
    // <DropDownList> body, built from FName / enum-name / element-value text — kept
    // interpolating raw. The suite passed throughout because nothing in it reached that
    // path. These tests reach it.

    /// <summary>A TMap whose VALUE is an FName: map values of NameProperty are routed
    /// into a DropDownList rather than a Description.</summary>
    private static LiveFieldValue NameValuedMap(params string[] valueTexts)
    {
        var elems = new List<ContainerElementValue>();
        for (int i = 0; i < valueTexts.Length; i++)
            elems.Add(new ContainerElementValue
            {
                Index = i,
                Key = (i + 1).ToString(),
                Value = valueTexts[i],
                ValueHex = $"{i + 1:X2}00000000000000",
            });

        return new LiveFieldValue
        {
            Name = "ItemNames",
            TypeName = "MapProperty",
            Offset = 0x50,
            Size = 80,
            MapCount = elems.Count,
            MapKeyType = "IntProperty",
            MapValueType = "NameProperty",
            MapKeySize = 4,
            MapValueSize = 8,
            MapValueOffset = 8,
            MapStride = 24,
            MapDataAddr = "0x9000",
            MapElements = elems,
        };
    }

    private static string DropDownBody(string xml)
    {
        var doc = XDocument.Parse(xml);
        var dd = doc.Descendants("DropDownList").FirstOrDefault();
        Assert.NotNull(dd);
        return dd!.Value;
    }

    [Fact]
    public void An_ampersand_in_a_dropdown_entry_still_yields_well_formed_xml()
    {
        // The reproducer for W4. Before the fix this threw
        // XmlException: "An error occurred while parsing EntityName".
        var doc = XDocument.Parse(Xml(NameValuedMap("Bow & Arrow", "Plain")));
        Assert.NotNull(doc.Root);
    }

    [Fact]
    public void Every_metacharacter_in_a_dropdown_entry_survives_the_round_trip()
    {
        // Not just "it parses" — the user has to be able to read the name in CE, so the
        // text must come back out of the parser unmangled.
        var body = DropDownBody(Xml(NameValuedMap(Nasty)));
        Assert.Contains("Bow & Arrow", body);
        Assert.Contains("<Rare>", body);
    }

    [Fact]
    public void A_less_than_in_a_dropdown_entry_cannot_open_a_phantom_element()
    {
        var xml = Xml(NameValuedMap("HP < 50%", "Plain"));
        var doc = XDocument.Parse(xml);

        // If '<' were raw, the parser would either throw or invent an element.
        Assert.NotNull(doc.Root);
        Assert.Contains("HP < 50%", DropDownBody(xml));
    }

    [Fact]
    public void A_newline_in_a_dropdown_entry_cannot_forge_an_extra_record()
    {
        // Well-formedness does NOT catch this one — the body is line-delimited, so a CR/LF
        // inside a game string would silently add a dropdown row and shift the rest.
        var body = DropDownBody(Xml(NameValuedMap("Sword\r\nOf\nTruth", "Plain")));

        var rows = body.Split('\n')
                       .Select(r => r.Trim())
                       .Where(r => r.Length > 0)
                       .ToList();

        Assert.Equal(2, rows.Count);                       // two entries in, two out
        Assert.Contains(rows, r => r.Contains("Sword Of Truth"));
    }
}
