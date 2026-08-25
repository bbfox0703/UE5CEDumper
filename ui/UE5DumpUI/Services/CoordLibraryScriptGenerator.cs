using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Emits the coordinate library as a self-contained CE AA Script: a fenced,
/// hand-editable data block followed by a generic picker form that filters the list
/// and teleports to the chosen entry via the DLL mailbox.
///
/// Format and its rationale: docs/teleport-coord-library-spec.md §4.
///
/// The fenced block is SIMULTANEOUSLY the runtime data and the round-trip source —
/// there is no second copy to drift. Shape adapted from AOBMaker's
/// <c>@AOBMAKER:AA_TOGGLE v1</c> block (named fields, brace-balanced parse), which
/// solves the same problem, but in OUR namespace: AOBMaker's end marker is the
/// feature-less, shared <c>-- @AOBMAKER:END</c> matched by unanchored IndexOf, so a
/// block of ours pasted into an AA Toggle script would make AOBMaker slice from its
/// own start marker to our END, parse zero entries, and silently untick every record
/// in the tree.
///
/// UI is built only from CE controls verified in the wild (CrimsonDesert.CT
/// CheatEntry 357): createForm / createPanel / createLabel / createEdit /
/// createRadioGroup / createButton / createListView. Notably NOT createListBox or
/// createComboBox — neither appears anywhere in this repo and the project rule is to
/// never invent a CE Lua API.
/// </summary>
public static class CoordLibraryScriptGenerator
{
    public const string MarkerStart = "-- @UE5CD:COORDS v1";
    public const string MarkerEnd = "-- @UE5CD:END";
    public const string GeneratedSeparator = "---- GENERATED CODE (do not edit below) ----";

    /// <summary>Current data-format version, emitted in <see cref="MarkerStart"/>.</summary>
    public const int FormatVersion = 1;

    /// <summary>Groups offered as RadioGroup buttons besides "All". Beyond this the
    /// overflow stays reachable via All + the filter box (the group name is part of
    /// the search key), and the caller is told what was folded.</summary>
    public const int MaxGroupButtons = 8;

    /// <summary>Radio buttons per row in the group selector. Drives the emitted
    /// RadioGroup height, so it must match what the Lua sets.</summary>
    public const int GroupColumns = 5;

    // Mailbox layout — dll/src/Mimic.h (MailboxData / TeleportOp).
    private const int CmdTeleport = CeMailboxLayout.CmdTeleport;
    private const int OpExplicit = 13;   // TP_OP_EXPLICIT
    private const int OpGetPose = 0;     // TP_OP_GET_POSE (also yields the map name)

    /// <summary>Description used for the CE memory record.</summary>
    public const string RecordDescription = "Teleport: Coordinate Library (picker)";

    /// <summary>Which teleport mechanism the emitted picker uses.</summary>
    public enum Flavour
    {
        /// <summary>Engine invoke via the DLL mailbox. Full fidelity: rotation is
        /// restored, and the current map is readable so the picker can guard against
        /// a cross-map jump.</summary>
        Dll,

        /// <summary>Raw memory write through the standalone trainer's baked offsets.
        /// No DLL needed, but weaker: the map name is unavailable (so the map guard
        /// degrades to a label), the offsets go stale when the game is patched, and on
        /// games that do not refresh the cached world transform the coordinates change
        /// without the character visibly moving.</summary>
        NoDll,
    }

    /// <summary>Description for the no-DLL record (kept distinct so both flavours can
    /// coexist in one cheat table).</summary>
    public const string NoDllRecordDescription = "UE5 Trainer: Coordinate Library (picker, no DLL)";

    /// <summary>The Lua global holding this flavour's form. MUST differ per flavour:
    /// a shared global makes the second record enabled re-show the first's window and
    /// return early, so its own picker is never created.</summary>
    internal static string FormGlobal(Flavour f) =>
        f == Flavour.Dll ? "UE5CD_form" : "UE5CD_form_nodll";

    /// <summary>
    /// Build the full [ENABLE]/[DISABLE] AA script.
    /// </summary>
    /// <param name="entries">Entries to bake. Pre-sorted here, so the Lua never sorts.</param>
    /// <param name="foldedGroups">Receives group names that did NOT get a radio button.</param>
    public static string Generate(IEnumerable<CoordEntry> entries, out List<string> foldedGroups)
        => Generate(entries, Flavour.Dll, 0, out foldedGroups);

    /// <inheritdoc cref="Generate(IEnumerable{CoordEntry}, Flavour, double, out List{string})"/>
    public static string Generate(
        IEnumerable<CoordEntry> entries, Flavour flavour, out List<string> foldedGroups)
        => Generate(entries, flavour, 0, out foldedGroups);

    /// <summary>
    /// Build the script for a specific <see cref="Flavour"/>. Both flavours share the
    /// SAME fenced data block and the same form skeleton — only the teleport call and
    /// the map handling differ. Sharing matters: a divergent data block would break
    /// the round trip for one of the two.
    /// </summary>
    /// <param name="zTolerance">Extra height added to Z at teleport time (0 = off).
    /// Baked into the fenced block, so it survives the round trip.</param>
    public static string Generate(
        IEnumerable<CoordEntry> entries, Flavour flavour, double zTolerance,
        out List<string> foldedGroups)
    {
        var list = entries?.ToList() ?? new List<CoordEntry>();

        // Sort at generation time: Group asc, then Label natural-ascending so
        // "Chest 2" precedes "Chest 10".
        list.Sort((a, b) =>
        {
            int g = string.Compare(a.Group, b.Group, StringComparison.OrdinalIgnoreCase);
            return g != 0 ? g : ViewModels.TeleportViewModel.NaturalCompare(a.Label, b.Label);
        });

        var groups = list
            .Select(e => e.Group)
            .Where(g => !string.IsNullOrEmpty(g))
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .ToList();

        var shownGroups = groups.Take(MaxGroupButtons).ToList();
        foldedGroups = groups.Skip(MaxGroupButtons).ToList();

        // Each flavour owns its OWN form global. Sharing one made the second record
        // to be enabled re-show the first's window and return, so its own picker was
        // never built -- and left [DISABLE] able to touch a caFree'd form.
        string formVar = FormGlobal(flavour);

        var sb = new StringBuilder(4096 + list.Count * 128);
        Line(sb, "[ENABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        CeLuaHygiene.AppendDebugPreamble(sb);
        Line(sb, $"-- {CeLuaHygiene.Attribution}");
        if (flavour == Flavour.NoDll)
        {
            Line(sb, "-- NO-DLL flavour: teleport is a RAW memory write through the standalone");
            Line(sb, "-- trainer's baked offsets. Weaker than the DLL path -- on games that do");
            Line(sb, "-- not refresh the cached world transform the coordinates change but the");
            Line(sb, "-- character may not visibly move, and the offsets go stale when the game");
            Line(sb, "-- is patched. The current map cannot be read without the DLL, so entries");
            Line(sb, "-- from other maps are listed but NOT guarded -- check the Map column.");
            Line(sb, "-- Requires 'UE5 Trainer: Setup' to have been enabled first.");
        }
        Line(sb);
        AppendDataBlock(sb, list, shownGroups, zTolerance);
        Line(sb);
        Line(sb, GeneratedSeparator);
        Line(sb);
        AppendPickerForm(sb, shownGroups.Count, flavour);
        Line(sb, "{$asm}");
        Line(sb, "[DISABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        Line(sb, "-- Close the picker if it is still open.");
        Line(sb, $"if {formVar} ~= nil and not {formVar}.Destroyed then {formVar}.close() end");
        Line(sb, "{$asm}");
        return sb.ToString();
    }

    // ── The fenced, hand-editable data block ────────────────────────────

    private static void AppendDataBlock(
        StringBuilder sb, List<CoordEntry> list, List<string> shownGroups, double zTolerance)
    {
        Line(sb, MarkerStart);
        Line(sb, "-- Edit the rows below, or paste this whole script back into UE5CEDumper");
        Line(sb, "-- (Coordinate Library -> Import Lua) to load them into the editor.");
        Line(sb, "-- Field order does not matter and unknown keys are ignored, so a newer");
        Line(sb, "-- build's extra field will not break an older one. Keep uid if you want an");
        Line(sb, "-- edited row to UPDATE its library entry instead of adding a new one.");
        Line(sb, "local COORDS = {");
        foreach (var e in list)
        {
            sb.Append("  { uid='").Append(CeLuaHygiene.EscapeLuaString(e.Uid)).Append('\'')
              .Append(", label='").Append(CeLuaHygiene.EscapeLuaString(e.Label)).Append('\'')
              .Append(", group='").Append(CeLuaHygiene.EscapeLuaString(e.Group)).Append('\'')
              .Append(", map='").Append(CeLuaHygiene.EscapeLuaString(e.Map)).Append('\'')
              .Append(", x=").Append(Num(e.X))
              .Append(", y=").Append(Num(e.Y))
              .Append(", z=").Append(Num(e.Z))
              .Append(", pitch=").Append(Num(e.Pitch))
              .Append(", yaw=").Append(Num(e.Yaw))
              .Append(", roll=").Append(Num(e.Roll))
              .Append(" },\n");
        }
        Line(sb, "}");
        Line(sb);
        Line(sb, "-- Extra height added to Z on arrival (0 = off). A saved position sits");
        Line(sb, "-- exactly on the floor it was captured on, and landing on that plane can");
        Line(sb, "-- drop you through it; a few units of clearance avoids that.");
        Line(sb, $"local Z_TOLERANCE = {Num(zTolerance)}");
        Line(sb);
        Line(sb, "local GROUPS = {");
        foreach (var g in shownGroups)
            Line(sb, $"  '{CeLuaHygiene.EscapeLuaString(g)}',");
        Line(sb, "}");
        Line(sb, MarkerEnd);
    }

    /// <summary>Canonical numeric text — rounded, then shortest-round-trippable, so a
    /// value exported here matches the CSV and the JSON store byte for byte.</summary>
    private static string Num(double v) => CoordPrecision.Text(CoordPrecision.Round(v));

    // ── The generic picker form ─────────────────────────────────────────

    private static void AppendPickerForm(StringBuilder sb, int groupCount, Flavour flavour)
    {
        bool dll = flavour == Flavour.Dll;
        string formVar = FormGlobal(flavour);
        if (dll)
        {
            // Contract check BEFORE the first write, at chunk level -- before call() and
            // teleport() are even defined. Those helpers do not run now: they run later,
            // from a button click, possibly hours into a session and possibly from a .CT
            // saved months ago. Verifying the layout once at ENABLE is the only point
            // where a mismatch can still be reported instead of scribbled into memory.
            //
            // The NO-DLL flavour deliberately gets no check: it never touches the
            // mailbox, so there is no layout in question (see the else branch below).
            CeLuaHygiene.AppendContractCheck(sb, "Coordinate Library",
                                             MailboxTimeout.UntickAndReturn);
            Line(sb);
            // Offsets come from CeMailboxLayout (the single source of truth mirroring
            // dll/src/Mimic.h) -- never hand-written here.
            Line(sb, $"local MB_CMD, MB_STATUS, MB_RESULT = {CeMailboxLayout.OffCmd}, " +
                     $"{CeMailboxLayout.OffStatus}, {CeMailboxLayout.OffResult}");
            Line(sb, $"local MB_UFUNC, MB_INST, MB_PARAMS = {CeMailboxLayout.OffUfuncAddr}, " +
                     $"{CeMailboxLayout.OffInstanceAddr}, {CeMailboxLayout.OffParamsData}");
        }
        Line(sb, "local DISPLAY_CAP = 2000   -- rows rendered at once; filter to narrow");
        Line(sb);
        Line(sb, "-- Reuse the window if it is already open (re-enabling the record).");
        Line(sb, $"if {formVar} ~= nil and not {formVar}.Destroyed then");
        Line(sb, $"  {formVar}.show(); {formVar}.BringToFront(); return");
        Line(sb, "end");
        Line(sb);
        if (dll)
        {
            Line(sb, "local function mailbox()");
            Line(sb, "  local mb = getAddressSafe('g_invokeMailbox')");
            Line(sb, "  if not mb or mb == 0 then mb = getAddressSafe('UE5Dumper.g_invokeMailbox') end");
            Line(sb, "  if not mb or mb == 0 then return nil end");
            Line(sb, "  return mb");
            Line(sb, "end");
            Line(sb);
            Line(sb, "-- Round-trip one mailbox command. Returns the Wirbel result code, or nil");
            Line(sb, "-- on timeout / no DLL. A timeout is an ERROR: never fall through to a close.");
            Line(sb, "local function call(op, writeParams)");
            Line(sb, "  local mb = mailbox()");
            Line(sb, "  if mb == nil then return nil, 'g_invokeMailbox not found -- is UE5Dumper.dll injected?' end");
            // R3: was a single sample of cmd. Teleporting to two coordinates in quick
            // succession is the ordinary use of this script, and that is exactly the
            // pattern the single read gets wrong.
            CeLuaHygiene.AppendIdleWait(sb, "mb",
                "return nil, 'the DLL mailbox is busy -- try again in a moment'", "  ",
            "return nil, 'the mailbox could not be read -- the game process has most likely exited (re-inject UE5Dumper.dll if it is still running)'");
            Line(sb, "  if writeParams then writeParams(mb + MB_PARAMS) end");
            Line(sb, "  writeQword(mb + MB_UFUNC, 0)");
            Line(sb, "  writeQword(mb + MB_INST, op)");
            Line(sb, "  writeInteger(mb + MB_STATUS, 0)");
            Line(sb, $"  writeInteger(mb + MB_CMD, {CmdTeleport})   -- write LAST");
            // The shared wait, in its by-value mode. Hand-rolled until now, and it
            // carried the same two defects build 2743 fixed elsewhere: it counted
            // sleep(1) iterations against a millisecond constant (15.47 ms each, so "10
            // s" was ~155 s), and 'the DLL did not respond' asserted a cause the mailbox
            // can actually distinguish.
            //
            // ReturnReason, not the toggles' UntickAndReturn: `call` answers its caller
            // with (code, err) and go() does the reporting. A return here would leave
            // only this helper, and unticking from inside it would kill the record while
            // the picker form is still open.
            CeLuaHygiene.AppendMailboxWait(
                sb, "Coordinate Library", MailboxTimeout.ReturnReason, indent: "  ");
            // Signed: the Wirbel rc is an int32 and readInteger defaults to UNSIGNED, which
            // surfaced -1 to the user as "code 4294967295" (Coordinate Library, 2026-08-07).
            Line(sb, "  return readInteger(mb + MB_RESULT, true), nil, mb");
            Line(sb, "end");
            Line(sb);
            Line(sb, "-- Current map, read from GET_POSE's pose block (map name at params+48).");
            Line(sb, "local function currentMap()");
            Line(sb, $"  local code, err, mb = call({OpGetPose}, nil)");
            // Propagate err. Dropping it made the caller run a SECOND round-trip that
            // failed the same way, so a dead game cost two full idle-wait deadlines
            // before saying anything (observed 2026-08-07: ~3 s, message shown once).
            Line(sb, "  if err ~= nil then return nil, err end");
            Line(sb, "  if code ~= 0 or mb == nil then return nil end");
            Line(sb, "  return readString(mb + MB_PARAMS + 48, 127, false)");
            Line(sb, "end");
            Line(sb);
            Line(sb, "local function teleport(e)");
            Line(sb, $"  local code, err = call({OpExplicit}, function(pd)");
            Line(sb, "    writeDouble(pd + 0,  e.x)");
            Line(sb, "    writeDouble(pd + 8,  e.y)");
            Line(sb, "    writeDouble(pd + 16, e.z + Z_TOLERANCE)");
            Line(sb, "    writeDouble(pd + 24, e.pitch)");
            Line(sb, "    writeDouble(pd + 32, e.yaw)");
            Line(sb, "    writeDouble(pd + 40, e.roll)");
            Line(sb, "    writeBytes(pd + 48, 1)   -- hasRot: restore the saved rotation");
            Line(sb, "  end)");
            Line(sb, "  return code, err");
            Line(sb, "end");
            Line(sb);
        }
        else
        {
            // No-DLL flavour: teleport is a raw write to RootComponent.RelativeLocation
            // through the standalone trainer's baked offsets + helpers (UE5T_*), which
            // the "UE5 Trainer: Setup" record must have defined first.
            Line(sb, "-- The map name is only readable through the DLL, so this flavour has no");
            Line(sb, "-- map guard: entries from other maps are listed, not blocked.");
            Line(sb, "local function currentMap() return nil end");
            Line(sb);
            Line(sb, "local function teleport(e)");
            Line(sb, "  if not UE5T_ready then");
            // Lua DOUBLE quotes here: the message itself contains single quotes, and
            // C#'s \' escape collapses to a bare ' -- which silently produced an
            // unterminated Lua string (in-game syntax error, build 2269).
            Line(sb, "    return nil, \"enable 'UE5 Trainer: Setup' first\"");
            Line(sb, "  end");
            Line(sb, "  local p = UE5T_pawn()");
            Line(sb, "  local rc = p and UE5T_deref(p, UE5T.rootOff)");
            Line(sb, "  if not rc then return nil, 'could not resolve pawn/RootComponent' end");
            Line(sb, "  local a = rc + UE5T.relLocOff");
            Line(sb, "  UE5T_wrv(a, e.x)");
            Line(sb, "  UE5T_wrv(a + UE5T.vecWidth, e.y)");
            Line(sb, "  UE5T_wrv(a + 2 * UE5T.vecWidth, e.z + Z_TOLERANCE)");
            Line(sb, "  -- Rotation via the Controller back-ref, when Setup resolved it.");
            Line(sb, "  if UE5T.controllerOff and UE5T.controllerOff >= 0");
            Line(sb, "     and UE5T.ctrlRotOff and UE5T.ctrlRotOff >= 0 then");
            Line(sb, "    local ctrl = UE5T_deref(p, UE5T.controllerOff)");
            Line(sb, "    if ctrl then");
            Line(sb, "      local r = ctrl + UE5T.ctrlRotOff");
            Line(sb, "      UE5T_wrv(r, e.pitch)");
            Line(sb, "      UE5T_wrv(r + UE5T.vecWidth, e.yaw)");
            Line(sb, "      UE5T_wrv(r + 2 * UE5T.vecWidth, e.roll)");
            Line(sb, "    end");
            Line(sb, "  end");
            Line(sb, "  -- Read the position back. A raw RelativeLocation write is the WEAK path:");
            Line(sb, "  -- on games that do not refresh the cached world transform, or where the");
            Line(sb, "  -- movement component re-integrates, the value simply does not stick. Say so");
            Line(sb, "  -- instead of reporting a success the user cannot see -- an unverifiable");
            Line(sb, "  -- 'Teleported to X' next to a motionless character is worse than an error.");
            Line(sb, "  local backX = UE5T_rdv(a)");
            Line(sb, "  if backX == nil or math.abs(backX - e.x) > 1.0 then");
            Line(sb, "    return nil, 'the write did not stick (the game reverted it) -- this is the'");
            Line(sb, "      .. ' known weakness of the no-DLL path; use the DLL flavour for this game'");
            Line(sb, "  end");
            Line(sb, "  return 0, nil");
            Line(sb, "end");
            Line(sb);
        }

        Line(sb, "-- space = AND over label + group + map, matching the app's filter rule.");
        Line(sb, "for i = 1, #COORDS do");
        Line(sb, "  local e = COORDS[i]");
        Line(sb, "  e.key = string.lower(e.label .. ' ' .. e.group .. ' ' .. e.map)");
        Line(sb, "end");
        Line(sb);
        Line(sb, "local function terms(text)");
        Line(sb, "  local t = {}");
        Line(sb, "  for w in string.gmatch(string.lower(text or ''), '%S+') do t[#t + 1] = w end");
        Line(sb, "  return t");
        Line(sb, "end");
        Line(sb);
        Line(sb, "local function matches(e, ts)");
        Line(sb, "  for i = 1, #ts do");
        Line(sb, "    if string.find(e.key, ts[i], 1, true) == nil then return false end");
        Line(sb, "  end");
        Line(sb, "  return true");
        Line(sb, "end");
        Line(sb);
        Line(sb, "synchronize(function()");
        Line(sb, "  local mapNow = currentMap()   -- refreshed in go(); see there");
        Line(sb);
        Line(sb, $"  {formVar} = createForm(false)");
        Line(sb, $"  {formVar}.Caption = 'UE5CEDumper -- Coordinate Library'");
        Line(sb, $"  {formVar}.Width = 900");
        Line(sb, $"  {formVar}.Height = 600");
        Line(sb, $"  {formVar}.Position = 'poScreenCenter'");
        Line(sb, "  -- Resizable, and every control below is laid out from ClientWidth rather");
        Line(sb, "  -- than hardcoded pixels: the form does not always get the width it asks");
        Line(sb, "  -- for (observed 2269 -- a ~577px window clipped the map selector), and a");
        Line(sb, "  -- relative layout survives that as well as any user resize.");
        Line(sb, $"  {formVar}.BorderStyle = bsSizeable");   // bare, per InvokeScriptGenerator
        Line(sb, $"  local CW = {formVar}.ClientWidth");
        Line(sb);
        Line(sb, "  -- Panel creation order is load-bearing: alTop panels stack in creation");
        Line(sb, "  -- order and the alClient control must be created LAST.");
        Line(sb, $"  local pnlTop = createPanel({formVar})");
        Line(sb, "  pnlTop.Align = 'alTop'");
        // Placeholder: the real height is measured from the controls once they exist
        // (see the "pnlTop.Height =" assignment after the last row is created). Guessing
        // it up front is what clipped the selectors.
        Line(sb, "  pnlTop.Height = 40");
        Line(sb, "  pnlTop.BevelOuter = 'bvNone'");
        Line(sb);
        Line(sb, "  local lblFilter = createLabel(pnlTop)");
        Line(sb, "  lblFilter.Caption = 'Filter:'");
        Line(sb, "  lblFilter.Left = 10");
        Line(sb, "  lblFilter.Top = 14");
        Line(sb);
        Line(sb, "  local edtFilter = createEdit(pnlTop)");
        Line(sb, "  edtFilter.Left = 60");
        Line(sb, "  edtFilter.Top = 10");
        Line(sb, "  edtFilter.Width = math.max(120, CW - 70)");
        Line(sb, "  -- Leave the TEdit height at its default: the reference form never sets");
        Line(sb, "  -- one, and a hardcoded value is what clipped it before.");
        Line(sb);
        if (dll)
        {
        Line(sb, "  local rgMap = createRadioGroup(pnlTop)");
        Line(sb, "  rgMap.Left = 10");
        Line(sb, "  rgMap.Top = 40");
        Line(sb, "  rgMap.Width = math.max(240, CW - 20)");
        // 56 = the reference form's working height for a CAPTIONED RadioGroup with one
        // item row (CrimsonDesert.CT rgList). 40 clipped both caption and items.
        Line(sb, "  rgMap.Height = 56");
        Line(sb, "  rgMap.Caption = 'Map'");
        Line(sb, "  rgMap.Columns = 2");
        Line(sb, "  rgMap.Items.add('Current map')");
        Line(sb, "  rgMap.Items.add('All maps')");
        Line(sb, "  rgMap.ItemIndex = (mapNow ~= nil and mapNow ~= '') and 0 or 1");
        }
        if (groupCount > 0)
        {
            Line(sb);
            Line(sb, "  local rgGroup = createRadioGroup(pnlTop)");
            // "All" + one button per group, wrapped at GroupColumns per row. Height is
            // 32 (caption band + margins) + 24 per item ROW, calibrated so a single row
            // lands on the reference's proven 56.
            int groupRows = (groupCount + 1 + GroupColumns - 1) / GroupColumns;
            Line(sb, "  rgGroup.Left = 10");
            Line(sb, dll ? "  rgGroup.Top = 100" : "  rgGroup.Top = 40");
            Line(sb, "  rgGroup.Width = math.max(240, CW - 20)");
            Line(sb, $"  rgGroup.Height = {32 + groupRows * 24}");
            Line(sb, "  rgGroup.Caption = 'Group'");
            Line(sb, $"  rgGroup.Columns = {GroupColumns}");
            Line(sb, "  rgGroup.Items.add('All')");
            Line(sb, "  for i = 1, #GROUPS do rgGroup.Items.add(GROUPS[i]) end");
            Line(sb, "  rgGroup.ItemIndex = 0");
        }
        Line(sb);
        Line(sb);
        Line(sb, "  -- Size the top panel from what the controls ACTUALLY occupy. Guessing this");
        Line(sb, "  -- up front is what clipped the selectors: control heights come from CE's");
        Line(sb, "  -- font metrics, which we cannot know here.");
        Line(sb, "  local lastTop = " + (groupCount > 0
                 ? "rgGroup.Top + rgGroup.Height"
                 : (dll ? "rgMap.Top + rgMap.Height" : "edtFilter.Top + edtFilter.Height")));
        Line(sb, "  pnlTop.Height = lastTop + 8");
        Line(sb);
        Line(sb, $"  local pnlStatus = createPanel({formVar})");
        Line(sb, "  pnlStatus.Align = 'alTop'");
        Line(sb, "  pnlStatus.Height = 24");
        Line(sb, "  pnlStatus.BevelOuter = 'bvNone'");
        Line(sb, "  local lblCount = createLabel(pnlStatus)");
        Line(sb, "  lblCount.Align = 'alClient'");
        Line(sb, "  lblCount.Caption = 'Type to filter (space = AND)'");
        Line(sb);
        Line(sb, $"  local pnlBottom = createPanel({formVar})");
        Line(sb, "  pnlBottom.Align = 'alBottom'");
        Line(sb, "  pnlBottom.Height = 50");
        Line(sb, "  pnlBottom.BevelOuter = 'bvNone'");
        Line(sb);
        Line(sb, "  -- Button widths are generous on purpose. 'Force teleport' at 130 rendered");
        Line(sb, "  -- as 'orce telepor': CE's font metrics are not knowable here, and a clipped");
        Line(sb, "  -- caption on the button that OVERRIDES a safety guard is the worst place to");
        Line(sb, "  -- be stingy. Lefts are chained off the previous width so they cannot drift.");
        Line(sb, "  local bx, bw = 10, 150");
        Line(sb, "  local btnGo = createButton(pnlBottom)");
        Line(sb, "  btnGo.Caption = 'Teleport'");
        Line(sb, "  btnGo.Left = bx");
        Line(sb, "  btnGo.Top = 8");
        Line(sb, "  btnGo.Width = bw");
        Line(sb, "  btnGo.Height = 32");
        Line(sb, "  bx = bx + bw + 10");
        Line(sb);
        Line(sb, "  local btnForce = createButton(pnlBottom)");
        Line(sb, "  btnForce.Caption = 'Force teleport'");
        Line(sb, "  btnForce.Left = bx");
        Line(sb, "  btnForce.Top = 8");
        Line(sb, "  btnForce.Width = 200");
        Line(sb, "  btnForce.Height = 32");
        Line(sb, "  bx = bx + 200 + 10");
        Line(sb);
        Line(sb, "  local btnClose = createButton(pnlBottom)");
        Line(sb, "  btnClose.Caption = 'Close'");
        Line(sb, "  btnClose.Left = bx");
        Line(sb, "  btnClose.Top = 8");
        Line(sb, "  btnClose.Width = 110");
        Line(sb, "  btnClose.Height = 32");
        Line(sb);
        Line(sb, "  -- alClient LAST so it fills what is left.");
        Line(sb, $"  local lv = createListView({formVar})");
        Line(sb, "  lv.Align = 'alClient'");
        Line(sb, "  lv.ViewStyle = 'vsReport'");
        Line(sb, "  lv.RowSelect = true");
        Line(sb, "  lv.ReadOnly = true");
        Line(sb, "  -- Column widths are a share of the real client width, so the numbers");
        Line(sb, "  -- stay readable in a narrow window instead of being cut off.");
        Line(sb, "  local colW = math.max(60, math.floor((CW - 40) / 6))");
        Line(sb, "  lv.Columns.add().Caption = 'Label'");
        Line(sb, "  lv.Columns[0].Width = math.floor(colW * 1.5)");
        Line(sb, "  lv.Columns.add().Caption = 'Group'");
        Line(sb, "  lv.Columns[1].Width = math.floor(colW * 0.8)");
        Line(sb, "  lv.Columns.add().Caption = 'Map'");
        Line(sb, "  lv.Columns[2].Width = math.floor(colW * 1.2)");
        Line(sb, "  lv.Columns.add().Caption = 'X'");
        Line(sb, "  lv.Columns[3].Width = math.floor(colW * 0.85)");
        Line(sb, "  lv.Columns.add().Caption = 'Y'");
        Line(sb, "  lv.Columns[4].Width = math.floor(colW * 0.85)");
        Line(sb, "  lv.Columns.add().Caption = 'Z'");
        Line(sb, "  lv.Columns[5].Width = math.floor(colW * 0.8)");
        Line(sb);
        Line(sb, "  local shown = {}   -- listview row index -> COORDS entry");
        Line(sb);
        Line(sb, "  local function rebuild()");
        Line(sb, "    local ts = terms(edtFilter.Text)");
        Line(sb, "    local wantGroup = nil");
        if (groupCount > 0)
        {
            Line(sb, "    if rgGroup.ItemIndex > 0 then wantGroup = GROUPS[rgGroup.ItemIndex] end");
        }
        Line(sb, dll
            ? "    local currentOnly = (rgMap.ItemIndex == 0) and mapNow ~= nil and mapNow ~= ''"
            : "    local currentOnly = false");
        Line(sb);
        Line(sb, "    lv.Items.beginUpdate()");
        Line(sb, "    lv.Items.clear()");
        Line(sb, "    shown = {}");
        Line(sb, "    local matched = 0");
        Line(sb, "    for i = 1, #COORDS do");
        Line(sb, "      local e = COORDS[i]");
        Line(sb, "      local ok = true");
        Line(sb, "      if currentOnly and string.lower(e.map) ~= string.lower(mapNow) then ok = false end");
        Line(sb, "      if ok and wantGroup ~= nil and string.lower(e.group) ~= string.lower(wantGroup) then ok = false end");
        Line(sb, "      if ok and not matches(e, ts) then ok = false end");
        Line(sb, "      if ok then");
        Line(sb, "        matched = matched + 1");
        Line(sb, "        if matched <= DISPLAY_CAP then");
        Line(sb, "          local row = lv.Items.add()");
        Line(sb, "          row.Caption = e.label");
        Line(sb, "          row.SubItems.add(e.group)");
        Line(sb, "          row.SubItems.add(e.map)");
        Line(sb, "          row.SubItems.add(string.format('%.3f', e.x))");
        Line(sb, "          row.SubItems.add(string.format('%.3f', e.y))");
        Line(sb, "          row.SubItems.add(string.format('%.3f', e.z))");
        Line(sb, "          shown[#shown + 1] = e");
        Line(sb, "        end");
        Line(sb, "      end");
        Line(sb, "    end");
        Line(sb, "    lv.Items.endUpdate()");
        Line(sb);
        Line(sb, "    if matched > DISPLAY_CAP then");
        Line(sb, "      lblCount.Caption = string.format(");
        Line(sb, "        'Matched %d (showing first %d) -- type more to narrow', matched, DISPLAY_CAP)");
        Line(sb, "    else");
        Line(sb, "      lblCount.Caption = string.format('Matched %d / %d', matched, #COORDS)");
        Line(sb, "    end");
        Line(sb, "  end");
        Line(sb);
        Line(sb, "  local function selected()");
        Line(sb, "    local idx = lv.ItemIndex");
        Line(sb, "    if idx == nil or idx < 0 then return nil end");
        Line(sb, "    return shown[idx + 1]");
        Line(sb, "  end");
        Line(sb);
        Line(sb, "  -- Confirm-before-teleport: plain Teleport refuses a cross-map jump. The");
        Line(sb, "  -- tool cannot LOAD another map -- only the game can -- so this is a guard,");
        Line(sb, "  -- not an inconvenience.");
        // Re-entrancy guard. The mailbox wait now pumps messages so CE's window
        // stays alive while it blocks -- which also means this button is still clickable
        // during a round-trip. Without the latch a second click starts a fresh pair of
        // commands on top of a pending one, and a timed-out command is not cancelled: it
        // still lands when the game next ticks, so the user would get two teleports.
        Line(sb, "  local busy = false");
        Line(sb, "  local function go(force)");
        Line(sb, "    if busy then return end");
        Line(sb, "    busy = true");
        // pcall so an error inside cannot strand the latch and wedge the button forever.
        Line(sb, "    local ok, err_ = pcall(function()");
        Line(sb, "    local e = selected()");
        Line(sb, "    if e == nil then lblCount.Caption = 'Select an entry first.'; return end");
        Line(sb, "    -- Re-read the map HERE. It was previously captured once when the window");
        Line(sb, "    -- opened, so after the game changed level the guard compared against a");
        Line(sb, "    -- map you had already left. There is no UI polling this for us.");
        Line(sb, "    local fresh, ferr = currentMap()");
        // Abort on the FIRST failed round-trip. Without this the teleport below runs anyway
        // and fails the same way, so a dead game cost two full idle-wait deadlines (~3 s)
        // and still showed only one message.
        Line(sb, "    if ferr ~= nil then showMessage('[Coordinate Library] ' .. ferr); return end");
        Line(sb, "    if fresh ~= nil and fresh ~= '' and fresh ~= mapNow then");
        Line(sb, "      mapNow = fresh");
        Line(sb, "      rebuild()   -- the 'current map only' filter was stale too");
        Line(sb, "    end");
        Line(sb, "    if (not force) and mapNow ~= nil and mapNow ~= ''");
        Line(sb, "       and string.lower(e.map) ~= string.lower(mapNow) then");
        Line(sb, "      showMessage('\"' .. e.label .. '\" was saved on \"' .. e.map ..");
        Line(sb, "                  '\" but you are on \"' .. mapNow ..");
        Line(sb, "                  '\".\\n\\nUse Force teleport to override -- note this cannot load' ..");
        Line(sb, "                  ' the other map, it only moves you within the current one.')");
        Line(sb, "      return");
        Line(sb, "    end");
        Line(sb, "    local code, err = teleport(e)");
        Line(sb, "    if err ~= nil then showMessage('[Coordinate Library] ' .. err); return end");
        Line(sb, "    if code ~= 0 then");
        Line(sb, "      lblCount.Caption = string.format('Teleport failed (code %d).', code)");
        Line(sb, "    else");
        Line(sb, "      lblCount.Caption = 'Teleported to ' .. e.label");
        Line(sb, "      dbg('[Coordinate Library] -> ' .. e.label)");
        Line(sb, "    end");
        Line(sb, "    end)   -- pcall");
        // Release the latch UNCONDITIONALLY, then re-raise. A latch stranded by an error
        // would disable the button for the rest of the session with no way back except
        // closing the picker.
        Line(sb, "    busy = false");
        Line(sb, "    if not ok then error(err_) end");
        Line(sb, "  end");
        Line(sb);
        Line(sb, "  edtFilter.OnChange = rebuild");
        if (dll) Line(sb, "  rgMap.OnClick = rebuild");
        if (groupCount > 0) Line(sb, "  rgGroup.OnClick = rebuild");
        Line(sb, "  btnGo.OnClick = function() go(false) end");
        Line(sb, "  btnForce.OnClick = function() go(true) end");
        Line(sb, "  lv.OnDblClick = function() go(false) end");
        Line(sb, $"  btnClose.OnClick = function() {formVar}.close() end");
        Line(sb);
        Line(sb, "  -- Interactive form: the untick + window close belong on OnClose, NOT on a");
        Line(sb, "  -- momentary timer -- the window must stay open while the user is using it.");
        Line(sb, "  --");
        Line(sb, "  -- Dropping the global FIRST is load-bearing, not tidiness: setting");
        Line(sb, "  -- memrec.Active = false runs the [DISABLE] block, which closes the form it");
        Line(sb, "  -- can still see -- re-entering OnClose and hanging CE (observed 2269).");
        Line(sb, "  -- Nil-ing it first makes [DISABLE]'s guard fail, so the close happens once.");
        Line(sb, "  -- It also stops [DISABLE] touching a caFree'd form object later.");
        Line(sb, $"  {formVar}.OnClose = function(sender)");
        Line(sb, $"    {formVar} = nil");
        Line(sb, "    if memrec ~= nil and memrec.Active then memrec.Active = false end");
        Line(sb, "    return caFree");
        Line(sb, "  end");
        Line(sb);
        Line(sb, "  rebuild()");
        Line(sb, $"  {formVar}.ActiveControl = edtFilter");
        Line(sb, $"  {formVar}.show()");
        Line(sb, "end)");
    }

    private static void Line(StringBuilder sb, string text = "")
    {
        sb.Append(text);
        sb.Append('\n');
    }
}
