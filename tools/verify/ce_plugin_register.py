r"""Register / unregister UE5Dumper.dll as a Cheat Engine plugin, reversibly.

    py tools/verify/ce_plugin_register.py status
    py tools/verify/ce_plugin_register.py register
    py tools/verify/ce_plugin_register.py unregister

WHY THIS EXISTS. Some rows can only be reached through the CE-plugin surface — the
`Methode.cpp` `OnInjectAndConnect` callback and everything it logs (`B29`, and the
`CEPlugin:` lines generally). CE loads plugins listed under

    HKCU\Software\Cheat Engine\Plugins64
        "<8-digit index> A"  REG_SZ     path to the DLL
        "<8-digit index> B"  REG_DWORD  1 = enabled (ticked), 0 = disabled

Format confirmed by reading the two entries CE already had
(`.\plugins\AOBMaker_CEPlugin.dll` = 1, `.\plugins\CE-Handwire.dll` = 0), which matches
the Settings→Plugins dialog exactly.

⭐ AN ABSOLUTE PATH TO `dist\`, DELIBERATELY — NOT A COPY INTO CE's FOLDER.
CE accepts either. Pointing at `dist\UE5Dumper.dll` is strictly better here:

  * `C:\Program Files\Cheat Engine\plugins\` is NOT writable non-elevated on this host
    (measured: PermissionError), unlike `autorun\` which is — so a copy needs elevation;
  * more importantly, a copy is a SECOND artifact that goes stale the moment the DLL is
    rebuilt. That is `[STALEDLL-2026-08-18]`(a) exactly: a February `UE5Dumper.dll` sat in
    CE's install folder and silently answered the `.CT` DLL-discovery probe for months,
    blocking that row until the maintainer deleted it. Referencing `dist\` means there is
    only ever ONE copy and it is the one that was just built.

⚠ THE COST OF THAT CHOICE, AND IT IS REAL: while CE is running with the plugin enabled it
holds `dist\UE5Dumper.dll` open, so a rebuild (`build.ps1 -Target DLL`) will fail to write
it. Close CE — or `unregister` — before building. This is the trade for never having a
stale copy, and it fails LOUDLY (a link error) rather than silently (a stale DLL answering
a probe), which is the right way round.

⚠ `unregister` matches on the PATH, never on the index: CE renumbers entries when it
rewrites the list, so an index recorded at register time is not trustworthy later. It also
COMPACTS the remaining entries back to a dense 0..N-1 range, because that is the shape CE
writes and a gap is not worth finding out about the hard way.
"""
from __future__ import annotations

import pathlib
import sys
import winreg

SEP = chr(92)
KEY = "Software" + SEP + "Cheat Engine" + SEP + "Plugins64"
DLL = pathlib.Path(__file__).resolve().parents[2] / "dist" / "UE5Dumper.dll"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")


def read_entries():
    """[(index, path, enabled)] sorted by index."""
    try:
        k = winreg.OpenKey(winreg.HKEY_CURRENT_USER, KEY)
    except OSError:
        return []
    paths, flags = {}, {}
    n = winreg.QueryInfoKey(k)[1]
    for i in range(n):
        name, val, _t = winreg.EnumValue(k, i)
        parts = name.rsplit(" ", 1)
        if len(parts) != 2:
            continue
        idx, kind = parts
        if kind == "A":
            paths[idx] = str(val)
        elif kind == "B":
            flags[idx] = int(val)
    k.Close()
    return sorted(((i, paths[i], flags.get(i, 0)) for i in paths), key=lambda t: t[0])


def write_entries(entries):
    """Rewrite the key as a dense 0..N-1 list."""
    k = winreg.CreateKeyEx(winreg.HKEY_CURRENT_USER, KEY, 0, winreg.KEY_ALL_ACCESS)
    # delete every existing A/B pair first, so a shrink cannot leave orphans behind
    old = []
    n = winreg.QueryInfoKey(k)[1]
    for i in range(n):
        old.append(winreg.EnumValue(k, i)[0])
    for name in old:
        try:
            winreg.DeleteValue(k, name)
        except OSError:
            pass
    for new_idx, (_i, path, enabled) in enumerate(entries):
        tag = "%08d" % new_idx
        winreg.SetValueEx(k, tag + " A", 0, winreg.REG_SZ, path)
        winreg.SetValueEx(k, tag + " B", 0, winreg.REG_DWORD, int(enabled))
    k.Close()


def is_ours(path):
    return pathlib.Path(path).name.lower() == "ue5dumper.dll"


def status():
    say("plugin DLL : %s" % DLL)
    say("  exists   : %s%s" % (DLL.is_file(),
                               ("  (%d bytes)" % DLL.stat().st_size) if DLL.is_file() else ""))
    ents = read_entries()
    say("HKCU%s%s -- %d entry(ies)" % (SEP, KEY, len(ents)))
    for idx, path, en in ents:
        mark = "   <-- OURS" if is_ours(path) else ""
        say("  [%s] enabled=%d  %s%s" % (idx, en, path, mark))
    if not any(is_ours(p) for _i, p, _e in ents):
        say("  -> UE5Dumper.dll is NOT registered")
    return 0


def register():
    if not DLL.is_file():
        say("MISSING %s -- build first" % DLL)
        return 1
    ents = read_entries()
    if any(is_ours(p) for _i, p, _e in ents):
        say("already registered:")
        return status()
    ents.append(("append", str(DLL), 1))
    write_entries(ents)
    say("registered %s (enabled)" % DLL)
    say("")
    return status()


def unregister():
    ents = read_entries()
    keep = [e for e in ents if not is_ours(e[1])]
    if len(keep) == len(ents):
        say("nothing of ours to remove")
        return status()
    write_entries(keep)
    say("removed %d UE5Dumper entry(ies)" % (len(ents) - len(keep)))
    say("")
    return status()


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "status"
    sys.exit({"register": register, "unregister": unregister}.get(cmd, status)())
