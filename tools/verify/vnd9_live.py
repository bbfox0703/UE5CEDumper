r"""[VND9-01] [VND9-03] live: a soft pointer's sub-path, and Pipe FIRE's returned-string text.

    py tools/verify/vnd9_live.py DumperTest-Win64-Shipping   --expect-sub StrProperty       (shipping, UE 5.4)
    py tools/verify/vnd9_live.py DumperTest58-Win64-Shipping --expect-sub Utf8StrProperty   (shipping58, UE 5.8)

The fixture is running with dist\UE5Dumper.dll injected, and no UI. The first argument names the folder under
%LOCALAPPDATA%\UE5CEDumper\Logs\ that holds the DLL's log: the PROCESS name, so a Shipping fixture's folder is
`<Name>-Win64-Shipping`. `DumperTest` alone is the Development flavour's folder, and a stale one answers with an
old line (measured: the first run read a 2026-11-02 line from it and failed P1 against a correct probe).

WHAT IT CHECKS, and the evidence each check needs:

  P1  [VND9-01] the probe MEASURES SubPathString. A `search_properties` filtered to soft types previews every soft
      value it finds, and each preview goes through ReadSoftObjectPath, which runs the probe once. The DLL's
      "DetectSoftPath:" log line must then name `SubPathString <expect-sub>`. SubPathString is an FString through
      UE 5.5 and an FUtf8String from 5.6 (SoftObjectPath.h), so the expected type is the version's, given on the
      command line rather than guessed here.
  P2  [VND9-01] DumperTest only, a regression control: Soft_Mesh still reads "/Engine/BasicShapes/Cube.Cube", with
      no ':' -- an asset reference has an empty sub-path.
  P3  [VND9-01] DumperTest only, the sub-path DISPLAY. No fixture field holds a soft reference to an actor, so the
      rig gives Soft_Mesh one for a moment: its SubPathString header (the last 16 bytes of a TSoftObjectPtr on every
      version) is pointed at another FString field's buffer, `Str_Even2_OneNull`, whose text the walk reads first.
      Soft_Mesh must then read "<path>:<that text>". The header is restored by mutate_guard, with a read-back.
      ⚠ A synthetic header, not the engine assigning one: it proves the live reader finds and decodes the sub-path
      at the measured offset and width, not that a game's actor reference looks like this.
  P4  [VND9-03] FIRE `KismetStringLibrary::Conv_IntToString(42)` (static, on its class default object): the reply's
      `str_results` holds ReturnValue = "42", read. Before the fix the reply had only result_hex, whose 16 bytes
      for the return are a header pointing into the game's heap.
  P5  [VND9-04] DumperTest only: a struct array's FString member shows its TEXT -- `Arr_StrRows[0]`'s `Text`
      reads "RowStrAlpha", where it said "(str)". `Note` (an FText) is the control.
  P6  [VND9-04] DumperTest only, a NO-REGRESSION check: an FString value search still finds a direct field
      (`PayloadString`), an array element (`Arr_Str[1]`) and a struct-array member (`Arr_StrRows[0].Text`), and a
      next scan keeps each. These are the four string readers the fix rerouted through the type's own width; no
      fixture holds a UE 5.5 narrow string, so the narrow arm is unit-tested only.

Exit 0 = every check that applies passed; 1 = a failure; 2 = the fixture is not in the state the checks need.
"""
import argparse
import os
import pathlib
import struct
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from mutate_guard import Mutation, read_bytes  # noqa: E402
from pipe_client import PipeClient  # noqa: E402

LOGS = pathlib.Path(os.environ.get("LOCALAPPDATA", "")) / "UE5CEDumper" / "Logs"
MARKER = "DetectSoftPath:"
SOFT_MESH_PATH = "/Engine/BasicShapes/Cube.Cube"
ALIAS_FIELD = "Str_Even2_OneNull"


def say(s):
    sys.stdout.write(str(s) + "\n")
    sys.stdout.flush()


def newest_marker_line(folder):
    """The last DetectSoftPath line in the process's log folder, newest file first."""
    files = sorted(folder.glob("*.log"), key=lambda p: p.stat().st_mtime, reverse=True)
    for f in files:
        try:
            lines = [ln for ln in f.read_text(encoding="utf-8", errors="replace").splitlines() if MARKER in ln]
        except OSError:
            continue
        if lines:
            return f.name, lines[-1]
    return None, None


def field(walk, name):
    return next((f for f in (walk.get("fields") or []) if f.get("name") == name), None)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("process", help="the log folder name, e.g. DumperTest or DumperTest58")
    ap.add_argument("--expect-sub", required=True, choices=["StrProperty", "Utf8StrProperty"],
                    help="SubPathString's type on this engine: StrProperty through 5.5, Utf8StrProperty from 5.6")
    a = ap.parse_args()
    fails = []

    with PipeClient() as c:
        build = c.assert_build()
        c.ensure_scanned()
        say("DLL build %s" % build)

        # ---------------------------------------------------------------- P1
        say("\n== P1 [VND9-01] the probe measures SubPathString ==")
        r = c.request("search_properties", query="", types=["SoftObjectProperty", "SoftClassProperty"],
                      game_only=False, limit=200, deep=True)
        results = r.get("results") or (r.get("data") or {}).get("results") or []
        previews = [x.get("preview") for x in results if x.get("preview")]
        say("   %d soft properties found, %d with a preview; with a sub-path: %s"
            % (len(results), len(previews), [p for p in previews if ":" in p][:5] or "none"))
        name, line = newest_marker_line(LOGS / a.process)
        if not line:
            say("   FAIL: no %r line in %s -- the probe never ran, or the log folder is wrong" % (MARKER, LOGS / a.process))
            fails.append("P1")
        else:
            say("   %s: %s" % (name, line.strip()))
            want = "SubPathString %s" % a.expect_sub
            if want in line:
                say("   PASS: %s" % want)
            else:
                say("   FAIL: the line does not say %r" % want)
                fails.append("P1")

        # ---------------------------------------------------------- P2 / P3
        inst = next((i["addr"] for i in (c.request("find_instances", class_name="DumperTestActor",
                                                    max_results=5).get("instances") or [])
                     if i.get("name") == "DumperTestActor"), None)
        if not inst:
            say("\n== P2 / P3: no live DumperTestActor on this fixture -- skipped (DumperTest only) ==")
        else:
            say("\n== P2 [VND9-01] control: an asset reference reads with no sub-path ==")
            w = c.request("walk_instance", addr=inst)
            soft, alias = field(w, "Soft_Mesh"), field(w, ALIAS_FIELD)
            if not soft or not alias:
                say("   ABORT: Soft_Mesh or %s not in the walk" % ALIAS_FIELD)
                return 2
            say("   Soft_Mesh = %r (offset 0x%X, size 0x%X)" % (soft.get("value"), soft["offset"], soft["size"]))
            if soft.get("value") == SOFT_MESH_PATH:
                say("   PASS")
            else:
                say("   FAIL: expected %r" % SOFT_MESH_PATH)
                fails.append("P2")

            say("\n== P3 [VND9-01] a sub-path is shown, in the width this engine stores ==")
            base = int(inst, 16)
            sub_hdr = base + soft["offset"] + soft["size"] - 16       # SubPathString ends every TSoftObjectPtr
            alias_hdr = read_bytes(c, base + alias["offset"], 16)
            if not alias_hdr:
                say("   ABORT: %s's header did not read" % ALIAS_FIELD)
                return 2
            data, num = struct.unpack_from("<Qi", alias_hdr)
            text = alias.get("value") or ""
            say("   %s = %r (Data 0x%X, Num %d)" % (ALIAS_FIELD, text, data, num))
            if not data or num <= 1 or not text:
                say("   ABORT: the alias string is empty -- the check would be vacuous")
                return 2
            with Mutation(c, "Soft_Mesh.SubPathString", sub_hdr, 16) as m:
                if any(m.original):
                    say("   ABORT: Soft_Mesh's sub-path header is not empty (%s) -- not the slot this rig assumed"
                        % m.original.hex().upper())
                    return 2
                if not m.apply(struct.pack("<Qii", data, num, num)):
                    fails.append("P3")
                else:
                    got = field(c.request("walk_instance", addr=inst), "Soft_Mesh")
                    got = got.get("value") if got else None
                    want = "%s:%s" % (SOFT_MESH_PATH, text)
                    say("   Soft_Mesh = %r" % got)
                    if got == want:
                        say("   PASS")
                    else:
                        say("   FAIL: expected %r" % want)
                        fails.append("P3")
            if not m.ok:
                fails.append("P3-restore")

            say("\n== P5 [VND9-04] a struct array's FString member shows its text ==")
            w = c.request("walk_instance", addr=inst, array_limit=8)
            rows = field(w, "Arr_StrRows")
            sub = {s.get("n"): s.get("v") for s in ((rows or {}).get("elements") or [{}])[0].get("sf") or []}
            say("   Arr_StrRows[0] = %s" % sub)
            if sub.get("Text") == "RowStrAlpha" and sub.get("Note") == "RowNoteAlpha":
                say("   PASS")
            else:
                say("   FAIL: expected Text = 'RowStrAlpha' (and the control Note = 'RowNoteAlpha')")
                fails.append("P5")

            say("\n== P6 [VND9-04] no regression: an FString value search through each reader ==")
            payload = (field(w, "PayloadString") or {}).get("value") or ""
            for label, value, want in (("a direct field", payload, "PayloadString"),
                                       ("an array element", "StrElemBetaBeta", "Arr_Str"),
                                       ("a struct-array member", "RowStrAlpha", "Arr_StrRows")):
                if not value:
                    say("   ABORT: no value for %s" % label)
                    return 2
                r = c.request("begin_value_scan", data_type="FString", scan_type="Exact", value=value,
                              game_only=True, deep=True, max_results=1000)
                d = r.get("data", r)
                sid = d.get("session_id")

                def names():
                    q = c.request("query_candidates", session_id=sid, offset=0, limit=200)
                    qd = q.get("data", q)
                    return [x.get("field_name", "") for x in (qd.get("candidates") or qd.get("results") or [])]
                first = [n for n in names() if want in n]
                rr = c.request("refine_value_scan", session_id=sid, scan_type="Exact", value=value)
                kept = [n for n in names() if want in n]
                c.request("end_value_scan", session_id=sid)
                say("   %s %r: first %s, next %s" % (label, value, first, kept))
                if first and kept and (rr.get("ok", True) is not False):
                    say("   PASS")
                else:
                    say("   FAIL: %s not found by the first scan, or dropped by the next" % want)
                    fails.append("P6")

        # ---------------------------------------------------------------- P4
        say("\n== P4 [VND9-03] FIRE's returned string comes back as text ==")
        r = c.request("invoke_function", class_name="KismetStringLibrary", func_name="Conv_IntToString",
                      params_hex=struct.pack("<i", 42).hex().upper())
        d = r.get("data", r)
        strs = d.get("str_results")
        say("   result=%s pick=%s str_results=%s" % (d.get("result"), d.get("instance_pick"), strs))
        ret = next((s for s in (strs or []) if s.get("name") == "ReturnValue"), None)
        if d.get("result") == 0 and ret and ret.get("read") and ret.get("text") == "42":
            say("   PASS")
        else:
            say("   FAIL: expected str_results to hold ReturnValue = \"42\", read")
            fails.append("P4")

    say("\n%s" % ("ALL PASS" if not fails else "FAILED: " + ", ".join(fails)))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
