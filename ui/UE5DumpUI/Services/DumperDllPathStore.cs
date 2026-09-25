using System;
using System.Collections.Generic;
using System.IO;
using UE5DumpUI.Core;

namespace UE5DumpUI.Services;

/// <summary>
/// Records the folder that holds <c>UE5Dumper.dll</c> so <c>scripts/UE5CEDumper.CT</c>
/// can find the DLL when Cheat Engine gives it nothing to go on.
///
/// <para><b>Why this exists.</b> A CE table script cannot read its own <c>.CT</c> path —
/// Cheat Engine exposes no such API. The <c>.CT</c> therefore infers the folder from CE's
/// Open/Save dialog objects, which <c>File &gt; Open</c> fills in and a double-click in
/// Explorer does not. Reported 2026-08-04: a double-clicked table searched only CE's
/// install folder and <c>%LOCALAPPDATA%</c>, and failed with the DLL sitting right beside
/// it. This file is the one channel that depends on nothing CE does or does not expose.</para>
///
/// <para><b>Format:</b> plain UTF-8 text, one absolute DIRECTORY per line, newest first,
/// <c>#</c> comments. A directory rather than a full DLL path because the <c>.CT</c>'s
/// probe appends the filename itself. Plain text rather than JSON for two reasons: the
/// consumer is CE Lua, which has no JSON parser, and it keeps this trivially Native-AOT
/// safe with no <c>[JsonSerializable]</c> context.</para>
///
/// <para><b>Location:</b> the <c>%LOCALAPPDATA%\UE5CEDumper</c> root — a sibling of the
/// other state files, NOT under <c>Logs\</c>. Verified against
/// <see cref="LoggingService"/>: every one of its sweeps is rooted at the log directory
/// and globs only <c>*.log</c> / <c>{prefix}-*.log</c>, so this file is out of reach on
/// both counts.</para>
/// </summary>
public sealed class DumperDllPathStore
{
    /// <summary>Keep a short history: a user who moves the install should still be able
    /// to fall back to a previous folder, but an unbounded list would probe stale paths
    /// forever.</summary>
    private const int MaxEntries = 4;

    private readonly string _path;

    public DumperDllPathStore(IPlatformService platform)
    {
        _path = Path.Combine(platform.GetAppDataPath(),
                             Constants.LogFolderName,
                             Constants.DllPathBreadcrumbFile);
    }

    /// <summary>Full path of the breadcrumb file (for logging / tests).</summary>
    public string FilePath => _path;

    /// <summary>Recorded folders, newest first. Never throws; an unreadable file reads
    /// as empty, which leaves the <c>.CT</c> on its previous behaviour.</summary>
    public IReadOnlyList<string> Load()
    {
        var list = new List<string>();
        try
        {
            if (!File.Exists(_path)) return list;
            foreach (var raw in File.ReadAllLines(_path))
            {
                var line = raw.Trim('﻿').Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                // (fifth review, R5-04) A RELATIVE line (an older UE5CEDumper.CT's self-heal could write one) is not a
                // folder anyone can find again: the .CT would probe it against Cheat Engine's current folder.
                if (!Path.IsPathFullyQualified(line)) continue;
                list.Add(line);
                if (list.Count >= MaxEntries) break;
            }
        }
        catch
        {
            // Unreadable → behave as if empty. This is a convenience channel; it must
            // never be able to fail an app start.
        }
        return list;
    }

    /// <summary>
    /// Promote <paramref name="dllDirectory"/> to the head of the list. A no-op when it
    /// is already the head, so the steady state does no I/O and no mtime churn.
    /// Best-effort throughout: a read-only profile or a full disk leaves the previous
    /// file in place rather than throwing into start-up.
    /// </summary>
    public void Record(string dllDirectory)
    {
        if (string.IsNullOrWhiteSpace(dllDirectory)) return;
        // A newline would split one path across two lines and silently corrupt the file
        // for the Lua reader. Win32 forbids 0x00-0x1F in a path component, so reaching
        // this is a caller bug, not user input.
        if (dllDirectory.IndexOf('\r') >= 0 || dllDirectory.IndexOf('\n') >= 0) return;
        // (fifth review, R5-04) Only a fully qualified folder ('sub', 'C:rel' and '\x' depend on a current folder).
        if (!Path.IsPathFullyQualified(dllDirectory)) return;

        var dir = dllDirectory.TrimEnd('\\', '/');
        try
        {
            var existing = Load();
            if (existing.Count > 0 &&
                string.Equals(existing[0], dir, StringComparison.OrdinalIgnoreCase))
                return;   // already the head — nothing to write

            var data = new List<string> { dir };
            foreach (var old in existing)
            {
                if (data.Count >= MaxEntries) break;
                if (!string.Equals(old, dir, StringComparison.OrdinalIgnoreCase)) data.Add(old);
            }

            var lines = new List<string>
            {
                "# UE5CEDumper - folders that have held UE5Dumper.dll, newest first.",
                "# Written by UE5DumpUI at startup; read by UE5CEDumper.CT to locate the",
                "# DLL when Cheat Engine's Open/Save dialogs are empty (double-clicked .CT).",
            };
            lines.AddRange(data);

            // Do not rely on another service's constructor having created the folder.
            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            // temp + move: a crash mid-write must not leave a half-written line that the
            // Lua reader would take for a real path.
            var tmp = _path + ".tmp";
            File.WriteAllLines(tmp, lines);   // UTF-8 without BOM (StreamWriter default)
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            // Best-effort by design — see the summary.
        }
    }
}
