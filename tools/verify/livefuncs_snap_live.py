r"""Live check of Live Funcs steps 2 and 3 -- parameter snapshots, native stacks, functions by name -- through the pipe.

    py tools/verify/livefuncs_snap_live.py --fixture-check
    py tools/verify/livefuncs_snap_live.py --label <run> [--record-s 8]
    py tools/verify/livefuncs_snap_live.py --label avowed --choose Inventory --plain-s 20 --record-s 30
    py tools/verify/livefuncs_snap_live.py --stacks [--stack-per-ring 30] [--stack-total 200] [--pdb [DIR]]
    py tools/verify/livefuncs_snap_live.py --label avowed --stacks --choose "" --plain-s 20 --record-s 30
    py tools/verify/livefuncs_snap_live.py --label dq11s --stacks --choose "" --names [--stack-depth 62]
    py tools/verify/livefuncs_snap_live.py --self-test

`[LIVEFUNCS-STEP2]` Runs against the game whose DLL serves the pipe (one game at a time, never while the UI holds
the pipe). Its subject is the DumperTest58 fixture's snapshot probes (tools/ue-sample/README.md, "DumperTest58"):
SnapNest_Outer calls SnapProbe_Call inside its scope and the timer calls it again on its own, every argument a fixed
function of the round, so a decoded snapshot is checked from its Round alone.

--fixture-check only asks whether the package under test carries the probes: a plain recording of a few seconds,
then every probe must be in pe_profile_get with calls. It is how a stale package is told from a broken DLL.

The full run (docs/live-funcs-step2-items.md, P2), each check named after its item:
  F1  every row carries a 4-int fname_key, the same on a second fetch; per_frame on SnapProbe_PerFrame only
  F2  a tick by name scopes the trace; a name never called this recording is accepted; a key whose class string was
      altered is refused, alone refuses the Start, and leaves nothing allocated
  F3  slots_per_ring is the K the rig computes from the caps; 512 choices of 2,048 B into 8 MB are refused naming
      the count, nothing allocated; a 4 MB buffer is refused
  K1  roots == SnapNest_Outer calls; every in-scope SnapProbe_Call entry took its snapshot; no slot without params
  F4  a choice first called right before Stop (SnapLate_Call) has its layout read by Stop's last pass; a choice never
      called is `not_called` even when the trace was empty and released; Stop returns within about 2.5 s
  F5  every SnapProbe_Call entry decodes to its Round's values, the after copy to OutTwice=2R, InOut=100+R,
      ReturnValue=3R with the In parameters blank; SnapProbe_RetOnly has an after copy; Label is const_ref; no orphans
  F6  SnapNest_Outer's code_addr lies inside the game's module (a script function's "" is not checkable on a C++-only
      fixture: reported as not run)
  the budget: SnapProbe_PerFrame's parameter ring drops its lone calls over the per-function budget and keeps no more
      than about the budget a second. The budget is chosen from F1's plain rates by S5's rule (30 while it fits:
      `[SNAPRIG-STEP2-RATE]`), and the check is reported not run where none bites, or where the main recording ran
      the probe under 1.5x the budget. That rate is read from the main recording's table, which must carry the
      probe's row over a window (a check of its own): a reply that cannot give it fails there, never stands the
      budget check down; and a reply that gives a plausible, wrong one stands it down only where the probe's own
      ring agrees -- every lone call it copied or counted dropped, so a rate never above the truth

--stacks (`[LIVEFUNCS-STEP3]`, docs/live-funcs-step3-items.md, "8. Live checks") runs step 3's checks instead, on
the same fixture: SnapNest_Outer ticked by name, SnapProbe_Call chosen for its parameters, SnapProbe_Call and
SnapProbe_PerFrame chosen for a native stack (depth 16, --stack-per-ring / --stack-total a second), recorded
--record-s; then an altered stack key alone, and a stacks-only Start. Without --stack-per-ring the per-function budget
is chosen from the fixture check's plain rates (`[SNAPRIG-S5-RATE]`): 30 when it sits 1.5x below SnapProbe_PerFrame's
rate and 1.5x above SnapProbe_Call's, leaving SnapProbe_Call 1.5x its rate in the total, else a budget between the
bounds (a fixture at about 30 fps calls SnapProbe_PerFrame about 30 times a second); the choice and why are printed
and kept in the output. The wire is the
design's section 3 (docs/live-funcs-step3-design.md). Each check is named after the ledger's:
  S0  the Start echoes the choices: names.stacks (absent on a DLL without step 3, the review's M3), trace.stack's
      rings, depth and budgets, K with the stack terms; the Stop's names[] carry `stack`; an altered stack key alone
      refuses the Start; a stacks-only Start (no ticks, funcs []) is snap_only with no parameter ring, refuses its
      altered second key by name, and records lone SnapProbe_Call calls flagged 4|32 and nothing else
  S1  every entry flagged 32 has exactly one slot in its function's ring by entry_seq and every slot one such entry;
      no orphans; no slot Partial / Fault / BadSp / LowStack / NoCapturer; 3 frames or more
  S2  frame 0 is in the game's exe: by name ignoring case (the review's L11), inside the image psapi reads, and through
      the CE text `"module"+RVA` added back on psapi's base; every site's module_base + rva and fn add up
  S3  in-scope stacks hold known:"process_event" and after it an `own` frame (the outer hook); lone ones neither;
      every known frame names one function, and no stack holds two. The known halves fail since S3-M3 follows a
      chained fragment to ProcessEvent's start (the review's M4). The stack total is one total, admitted in call
      order each second, so a --stack-total that leaves the other choices no room beside what SnapProbe_PerFrame may
      keep can refuse in-scope stacks on a correct DLL. The plain rates predict it (printed, and S5's window is not
      run on it); the two in-scope checks are reported not run only where the main recording shows it -- no in-scope
      stack kept, every in-scope SnapProbe_Call entry flagged 64 -- and the total starves the others at
      SnapProbe_PerFrame's plain or main rate. Otherwise they run, over the stacks kept
  S4  recorded, not failed: an in-scope frame whose fn is SnapNest_Outer's code_addr, before the ProcessEvent frame;
      a miss is the tail-call case
  S5  SnapProbe_PerFrame's stack ring keeps about the per-function budget a second and drops the rest; the parameter
      counters stay 0. When the plain rates show the budget cannot bite (one given, or none fits, or a total that
      starves the others), or the main recording itself ran SnapProbe_PerFrame under 1.5x the budget (its table's
      rate, or the one its stack ring shows where that is higher: every lone call written or counted dropped, so
      never above the truth), the window is reported not run with those rates, never failed nor passed. The main
      recording's table must carry that rate (a check of its own): a reply without it fails there, and the window
      runs as it did before the rate was read, never stood down on a rate nobody measured. The counters are checked
      wherever the stack budget refused a call -- the main table counts more SnapProbe_PerFrame calls than its ring
      wrote, or trace.stack counts a skip or a drop -- and a nonzero one fails whatever was refused; only where
      nothing was refused, and both are 0, are they reported not run, since 0 there proves nothing
  S6  recorded: mean and max microseconds a capture, captures a second, calls/s with and without stacks, the CPU,
      and D3's re-weighed total
  S7  each release frees everything. Re-running the default checks and livefuncs_trace_live.py on the same DLL is a
      separate invocation (cut 4, which the review's H1 took)
--stacks --pdb [DIR] (the ledger's S3-R2) then names the stacks' function starts against the game's PDB, once the main
trace is released: dbghelp in a private session reads the exe whose path the game's process gives, at the base psapi
reads, and the PDB that matches it in DIR, else beside the exe (the fixture ships it there). Never the game's memory.
  PDB every distinct fn_rva of a site in the exe with unwind data names a symbol at displacement 0: the DLL's fn is a
      .pdata function start, a chained fragment followed to its primary function (S3-M3), so a displacement is a
      fragment's start
  PDB every known:"process_event" site names ProcessEvent; SnapNest_Outer's native entry (pe_trace_names' code_addr,
      S4's frame) names SnapNest_Outer
  recorded, not failed: the first in-scope stack, frame by frame, by name
Without a PDB that matches the exe, the PDB checks are reported not run, never failed.
No red run on a DLL without step 3 (H1): it sends no names.stacks, so S0 fails by construction and S1-S6 cannot run.
--stacks --choose is the design's 8.3 on a real game: the busiest named functions whose class or name holds one of
the substrings ("" for any), chosen for stacks alone at the DLL's default budgets (a budget given on the command line
is sent instead); it reports their cost. --stack-depth N (1..62, 16 unless given) is the depth its stacks are taken
at; the fixture run refuses it, its checks being written for 16. --names (S3-A1 on a real game, only with --stacks
--choose) then checks the names the DLL puts on those stacks' frames, grouped by entry (ufunc, fn), the most frequent
NAMES_MAX asked and the rest counted:
  A1  every named entry's ufunc is a Function (or a delegate's) of that name, in that class (get_object); an entry
      named "" (a name read that gave nothing) is wrong, never a match of two empty names
  A1  every named entry's fn is stored inside its UFunction, at one offset common to all (read_mem): the DLL reads one
      slot for every name. That the slot is UFunction::Func is shown by the frame order recorded next, and a PDB.
      read_mem is all or nothing, so a read of the DLL's Func window (0x160 bytes) that fails is retried smaller. The
      DLL named each frame by reading that slot, so an entry read short of the offset, or not read at all, has its slot
      there read alone (8 bytes): fn holds, another value fails (and rules out a candidate offset first, a decoy copy
      in the entries read whole), and a slot that cannot be read now is listed as gone since the frame was named. The
      line counts the entries judged, the number asked beside it. With no common offset an entry read short is
      listed; with no entry read the check is reported not run
  recorded, not failed: the frames named, the entries, the shared ones (the interpreter is one, shared by every
      script function), and how many frames lie between a named frame and the next known:"process_event" toward
      the root (a native entry entered through ProcessEvent sits one below it, UFunction::Invoke between; a thunk
      reached from the interpreter has none)
With no frame named, both A1 checks are reported not run, never passed.
--self-test runs the pure pieces against hand-made replies, then both --stacks runs against a scripted DLL
(ScriptedDll), whole and with each fault it scripts, every check of the runs failing on a fault it exists to catch, and
the step-2 run as far as its parameter budget (the scripted DLL scripts no snapshot's values): no pipe, no game.

Against a DLL older than the item, its checks fail: that run is the item's red. Every recording is stopped in a
`finally` and the trace released. Exit 0 when every check holds; 1 otherwise; 2 when the pipe or the game is not
usable. Output: out/livefuncs-snap/<label>.json (<label>-stacks.json with --stacks) and a summary.
"""
from __future__ import annotations

import argparse
import base64
import contextlib
import ctypes
import ctypes.wintypes as w
import io
import json
import math
import os
import pathlib
import re
import struct
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient, PipeError  # noqa: E402

OUT_DIR = HERE.parents[1] / "out" / "livefuncs-snap"
HOST_PID = HERE.parents[1] / "out" / "host.pid"
FIXTURE_CLASS = "DumperTest58Actor"
# The probes a plain recording must see called. SnapLate_Call is not here: it runs only when the rig calls
# SnapLate_Begin.
PROBES = ("SnapNest_Outer", "SnapProbe_Call", "SnapProbe_RetOnly", "SnapProbe_ConstRefOnly", "SnapProbe_PerFrame")
REC = struct.Struct("<QQQQII")       # Linie::TraceRecord, 40 bytes
RET_BIT = 1 << 63
F_ROOT, F_TAKEN, F_LONE, F_EXCLUDED, F_BUDGET = 1, 2, 4, 8, 16
MARK_EXACT, MARK_NOW, MARK_GONE, MARK_MISSING, MARK_HEADER, MARK_RAW = range(6)
SNAP_NULL_PARAMS = 1
# SnapProbe_Call's parameters as the fixture declares them, by their case-folded name.
CANON = {n.lower(): n for n in ("Round", "F", "D", "bFlag", "Kind", "Tag", "Label", "Values", "Who", "Soft", "V", "S",
                                "OutTwice", "InOut", "ReturnValue")}
# [SNAPRIG-STEP2-RATE] The full run's parameter budget on SnapProbe_PerFrame, chosen by S5's rule (per_frame_budget):
# 30 a second while it fits, the budget the run always sent. The total is left to the DLL, which uses Linie's 10,000
# (TraceConfig's snapTotalPerSec) and so never starves a choice here. The check by name, for --self-test's controls.
STEP2_PER_RING, SNAP_TOTAL_DEFAULT = 30, 10000
STEP2_BUDGET = "the budget: the per-frame probe's lone calls over"


def say(s: str) -> None:
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def data_of(reply: dict) -> dict:
    return reply.get("data", reply)


def ok_of(reply: dict) -> bool:
    return bool(reply.get("ok", True)) and "error" not in reply


class Checks:
    def __init__(self) -> None:
        self.items: list[tuple[str, bool, str]] = []
        self.records: list[tuple[str, bool | None, str]] = []
        self.skipped: list[tuple[str, str]] = []

    def __call__(self, name: str, cond: bool, got: str = "") -> bool:
        self.items.append((name, bool(cond), got))
        say(f"  {'ok  ' if cond else 'FAIL'}  {name}" + (f"   ({got})" if got else ""))
        return bool(cond)

    def not_run(self, name: str, why: str) -> None:
        self.skipped.append((name, why))
        say(f"  --    {name}   (not run: {why})")

    def record(self, name: str, got: str, as_expected: bool | None = None) -> None:
        """A fact the run reports and never fails on (the ledger's "recorded, not failed"). `as_expected` False marks
        the line, so a surprise is seen without turning a deferred item into a red run."""
        self.records.append((name, as_expected, got))
        say(f"  {'rec!' if as_expected is False else 'rec '}  {name}   ({got})  [recorded, not failed]")

    @property
    def failed(self) -> int:
        return sum(1 for _, ok, _ in self.items if not ok)


def plain_table(c: PipeClient, seconds: float, before_stop=None, **get) -> dict:
    """A recording without the trace, then the table."""
    start = c.request("pe_profile_start")
    if not ok_of(start):
        raise PipeError(f"pe_profile_start refused: {start}")
    try:
        time.sleep(seconds)
        if before_stop:
            before_stop()
    finally:
        c.request("pe_profile_stop")
    return data_of(c.request("pe_profile_get", limit=32768, include_unloaded=True, **get))


def fixture_rows(table: dict) -> dict[str, dict]:
    """The fixture class's rows by function name (the busiest when a name repeats)."""
    rows: dict[str, dict] = {}
    for f in table.get("functions", []):
        if f.get("class_name") != FIXTURE_CLASS:
            continue
        name = f.get("func_name", "")
        if name not in rows or f.get("count", 0) > rows[name].get("count", 0):
            rows[name] = f
    return rows


def fixture_check(c: PipeClient, check: Checks, seconds: float) -> dict:
    table = plain_table(c, seconds)
    rows = fixture_rows(table)
    check("the table recorded calls", table.get("total_calls", 0) > 0, f"{table.get('total_calls', 0):,} calls")
    for p in PROBES:
        n = rows.get(p, {}).get("count", 0)
        check(f"{FIXTURE_CLASS}::{p} was called", n > 0, f"{n} calls" if n else "absent: a package without the probes")
    return {"total_calls": table.get("total_calls", 0), "window_ms": table.get("window_ms", 0),
            "probes": {p: rows.get(p) for p in PROBES}}


def invoke_late(c: PipeClient, value: int) -> dict:
    return c.request("invoke_function", class_name=FIXTURE_CLASS, func_name="SnapLate_Begin",
                     params_hex=struct.pack("<i", value).hex(), parms_size=4)


def item(row: dict) -> dict:
    """A by-name item for the Start, from a table row (the strings it showed, its key)."""
    return {"class": row["class_name"], "func": row["func_name"], "keys": [row["fname_key"]],
            "parms_size": row.get("parms_size", 0)}


def ring_cap(parms_size: int) -> int:
    """Linie::RingCapFor, as the UI computes it."""
    if parms_size == 0:
        return 256
    n = min(parms_size, 2048)
    return (n + 7) & ~7


def k_for(snap_bytes: int, caps: list[int]) -> int:
    """The calls each ring keeps (the UI's formula): (bytes - 64n) / sum(24 + round8(cap))."""
    per = sum(24 + ((cap + 7) & ~7) for cap in caps)
    return (snap_bytes - 64 * len(caps)) // per if snap_bytes > 64 * len(caps) else 0


def read_ring(c: PipeClient) -> list[tuple]:
    head = c.request("pe_trace_get", **{"from": 0, "max": 1})
    frm, written = head.get("first_valid", 0), head.get("written", 0)
    recs: list[tuple] = []
    while frm < written:
        page = c.request("pe_trace_get", **{"from": frm, "max": 262144})
        recs.extend(REC.iter_unpack(base64.b64decode(page.get("data", ""))))
        nxt = page.get("next", frm)
        if nxt <= frm:
            break
        frm = nxt
    return recs


def parents(recs: list[tuple]) -> dict[int, int | None]:
    """Each entry seq's parent entry seq (None for a root), per thread."""
    parent: dict[int, int | None] = {}
    stacks: dict[int, list[int]] = {}
    for seq_kind, _t, a, _b, tid, _f in recs:
        st = stacks.setdefault(tid, [])
        if seq_kind & RET_BIT:
            if a in parent:
                while st and st[-1] != a:
                    st.pop()
                if st:
                    st.pop()
            else:
                st.clear()
            continue
        parent[seq_kind] = st[-1] if st else None
        st.append(seq_kind)
    return parent


def trace_names(c: PipeClient, gen: int) -> dict[int, dict]:
    out: dict[int, dict] = {}
    off = 0
    while True:
        d = data_of(c.request("pe_trace_names", kind="funcs", gen=gen, offset=off, limit=20000))
        items = d.get("items", [])
        for it in items:
            out[int(it["addr"], 16)] = it
        off += len(items)
        if not items or off >= d.get("total", 0):
            break
    return out


def snap_layouts(c: PipeClient, gen: int) -> tuple[dict, list[dict], list[dict]]:
    head: dict = {}
    arms: list[dict] = []
    layouts: list[dict] = []
    off = 0
    while True:
        d = data_of(c.request("pe_snap_layouts", gen=gen, offset=off))
        if "error" in d or d.get("stale"):
            return d, arms, layouts
        head = d
        base = len(layouts)
        layouts.extend(d.get("layouts", []))
        for a in d.get("arms", []):
            if "layout" in a:
                a["layout"] += base
            arms.append(a)
        off = d.get("next", off)
        if not d.get("arms") or off >= d.get("total", 0):
            break
    return head, arms, layouts


def snap_slots(c: PipeClient, gen: int, ring: int) -> tuple[list[dict], int]:
    items: list[dict] = []
    orphans = 0
    frm = 0
    while True:
        d = data_of(c.request("pe_snap_get", gen=gen, ring=ring, **{"from": frm, "max": 4096}))
        got = d.get("items", [])
        items.extend(got)
        orphans += d.get("orphans", 0)
        nxt = d.get("next", frm)
        if not got or nxt <= frm:
            break
        frm = nxt
    return items, orphans


def module_range(pid: int) -> tuple[int, int] | None:
    """The game exe's [base, base + size), read with psapi (the first module is the exe)."""
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    psapi = ctypes.WinDLL("psapi", use_last_error=True)

    class MODULEINFO(ctypes.Structure):
        _fields_ = [("lpBaseOfDll", ctypes.c_void_p), ("SizeOfImage", w.DWORD), ("EntryPoint", ctypes.c_void_p)]

    k32.OpenProcess.restype = w.HANDLE
    psapi.EnumProcessModulesEx.argtypes = [w.HANDLE, ctypes.POINTER(w.HMODULE), w.DWORD, ctypes.POINTER(w.DWORD),
                                           w.DWORD]
    psapi.GetModuleInformation.argtypes = [w.HANDLE, w.HMODULE, ctypes.c_void_p, w.DWORD]   # 64-bit handles
    h = k32.OpenProcess(0x0410, False, pid)   # QUERY_INFORMATION | VM_READ
    if not h:
        return None
    try:
        mods = (w.HMODULE * 1)()
        need = w.DWORD()
        if not psapi.EnumProcessModulesEx(h, mods, ctypes.sizeof(mods), ctypes.byref(need), 0x03):
            return None
        mi = MODULEINFO()
        if not psapi.GetModuleInformation(h, mods[0], ctypes.byref(mi), ctypes.sizeof(mi)):
            return None
        return mi.lpBaseOfDll, mi.lpBaseOfDll + mi.SizeOfImage
    finally:
        k32.CloseHandle(h)


def num(v: list) -> float | None:
    try:
        return float(v[0])
    except (TypeError, ValueError, IndexError):
        return None


def entry_expect(vals: list, names: list[str]) -> list[str]:
    """What is wrong with one SnapProbe_Call entry copy, from its own Round: [] when it decodes to the fixture's values."""
    v = dict(zip(names, vals))
    bad: list[str] = []
    r = num(v.get("Round", [None]))
    if r is None:
        return ["Round unreadable"]
    r = int(r)

    def want(name: str, ok: bool) -> None:
        if not ok:
            bad.append(f"{name}={v.get(name)}")
    want("F", num(v.get("F", [None])) == r + 0.5)
    want("D", num(v.get("D", [None])) == r * 0.25)
    want("bFlag", v.get("bFlag", [""])[0] == ("true" if r % 2 else "false"))
    want("Kind", ["::Alpha", "::Beta", "::Gamma"][r % 3] in v.get("Kind", [""])[0])
    want("Tag", v.get("Tag", [""])[0] == f"SnapTag_{r % 3}")
    lab = v.get("Label", ["", -1])
    want("Label", lab[1] == MARK_HEADER and lab[0].startswith(f"Num={len('Label' + str(r)) + 1} "))
    vs = v.get("Values", ["", -1])
    want("Values", vs[1] == MARK_HEADER and vs[0].startswith("Num=3 "))
    who = v.get("Who", ["", -1])
    want("Who", who[1] == MARK_NOW and "DumperTest58Anchor" in who[0])
    soft = v.get("Soft", ["", -1])
    want("Soft", soft[1] == MARK_HEADER and ":<sub-path, " in soft[0])   # the actor's sub-path: its text not copied
    vec = v.get("V", ["", -1, []])
    sub = vec[2] if len(vec) > 2 else []
    want("V", [num(x) for x in sub] == [r, -r, 0.5])
    s = v.get("S", ["", -1, []])
    ss = s[2] if len(s) > 2 else []
    w_ = ss[3][2] if len(ss) > 3 and len(ss[3]) > 2 else []
    want("S", len(ss) == 4 and num(ss[0]) == r and ss[1][0] == ("true" if r % 2 else "false") and
         ss[2][0] == ("false" if r % 2 else "true") and [num(x) for x in w_] == [r, -r, 0.5])
    want("OutTwice", num(v.get("OutTwice", [None])) == -1)
    want("InOut", num(v.get("InOut", [None])) == 100)
    rv = v.get("ReturnValue", ["", -1])
    want("ReturnValue", rv[1] == MARK_MISSING)
    return bad


def run_game(c: PipeClient, check: Checks, out: dict, args) -> None:
    """A real game (`--choose`): a plain recording finds the functions whose class or name holds one of the given
    substrings, with parameters and a key; a snapshots-only recording chooses them by name, plus the busiest per-frame
    function with parameters. Whatever the game does in between (an inventory opened and closed, `I` on Avowed) is the
    test: a widget's functions unload when it closes, and their calls must still decode -- their layouts were read
    while they were alive (T10)."""
    say(f"\nplain recording ({args.plain_s:.0f} s) to find the functions to choose:")
    table = plain_table(c, args.plain_s)
    rows = [f for f in table.get("functions", []) if isinstance(f.get("fname_key"), list)]
    pats = [p.lower() for p in args.choose]
    picked: dict[str, dict] = {}
    # The busiest per-frame function with parameters first, so the cap below never leaves it out: it is what the
    # budget is checked on.
    per_frame = sorted((f for f in rows if f.get("per_frame") and f.get("num_parms", 0) > 0),
                       key=lambda f: -f.get("count", 0))
    if per_frame:
        picked[f"{per_frame[0]['class_name']}::{per_frame[0]['func_name']}"] = per_frame[0]
    for f in rows:
        name = f"{f.get('class_name')}::{f.get('func_name')}"
        if f.get("num_parms", 0) > 0 and any(p in name.lower() for p in pats):
            picked.setdefault(name, f)
    chosen = list(picked.values())[: args.max_choices]
    window_s = table.get("window_ms", 0) / 1000.0
    out["per_frame_choice"] = ({"name": f"{per_frame[0]['class_name']}::{per_frame[0]['func_name']}",
                                "calls_per_s": per_frame[0].get("count", 0) / window_s if window_s else 0}
                               if per_frame else None)
    out["chosen"] = [f"{f['class_name']}::{f['func_name']}" for f in chosen]
    check("functions to choose were found", len(chosen) > 0, ", ".join(out["chosen"][:12]))
    if not chosen:
        return

    say(f"\nsnapshots-only recording ({args.record_s:.0f} s), {len(chosen)} chosen:")
    trace = {"bytes": 64 << 20, "snapshots": {"funcs": [item(f) for f in chosen], "bytes": 32 << 20,
                                               "per_ring_per_s": args.per_ring}}
    t0 = time.perf_counter()
    start = c.request("pe_profile_start", trace=trace)
    out["start_s"] = time.perf_counter() - t0
    if not check("the Start is accepted", ok_of(start), str(start.get("error", ""))[:120]):
        return
    st = data_of(start).get("trace", {})
    check("snapshots-only: scoped, no ticks, every choice named", st.get("snap_only") is True and
          st.get("names", {}).get("chosen") == len(chosen), json.dumps(st.get("names"))[:160])
    time.sleep(args.record_s)
    t0 = time.perf_counter()
    stop = data_of(c.request("pe_profile_stop"))
    stop_s = time.perf_counter() - t0
    out["stop"] = stop
    check("Stop returns within about 2.5 s", stop_s < 2.5, f"{stop_s:.2f} s")
    gen = stop.get("trace", {}).get("gen", 0)
    not_called = [n["func"] for n in stop.get("names", []) if n.get("not_called")]
    say(f"     followed names never called: {len(not_called)} {not_called[:8]}")
    # The cost, as the game's own call rate: the same table recorded with the snapshots running, against the plain one.
    after = data_of(c.request("pe_profile_get", limit=1))
    rate = lambda t: t.get("total_calls", 0) / (t.get("window_ms", 0) / 1000.0) if t.get("window_ms") else 0
    out["rates"] = {"plain": rate(table), "with_snapshots": rate(after)}
    say(f"     calls/s: plain {out['rates']['plain']:,.0f}, with snapshots {out['rates']['with_snapshots']:,.0f}")
    if not stop.get("trace", {}).get("allocated"):
        check("the trace kept calls", False, "empty: nothing chosen was called")
        return

    fnames = trace_names(c, gen)
    head, arms, layouts = snap_layouts(c, gen)
    states: dict[str, int] = {}
    for a in arms:
        states[a.get("state", "?")] = states.get(a.get("state", "?"), 0) + 1
    out["arm_states"] = states
    out["read_ms"] = sorted(a.get("read_ms", 0) for a in arms if "read_ms" in a)
    say(f"     arms: {len(arms)} {states}; read after (ms) {out['read_ms'][:10]}")
    check("every arm's layout was read", arms != [] and all(a.get("state") in ("read", "doubtful") for a in arms),
          json.dumps(states))
    # A name loaded twice in the window (a widget closed, collected and opened again) is two arms, each with its own
    # address and layout: the register's [LIVEFUNCS-STEP2] row 1.
    per_name: dict[str, list[dict]] = {}
    for a in arms:
        per_name.setdefault(f"{a.get('class_name')}::{a.get('func_name')}", []).append(a)
    reloaded = {n: v for n, v in per_name.items() if len(v) > 1}
    out["reloaded"] = {n: [x["addr"] for x in v] for n, v in reloaded.items()}
    say(f"     names armed more than once: {len(reloaded)} {list(reloaded)[:6]}")
    decoded = undecoded = gone_decoded = 0
    arm_decoded: dict[int, int] = {}
    gone_funcs = {a for a, it in fnames.items() if it.get("unloaded")}
    arm_addr = {a["index"]: int(a["addr"], 16) for a in arms}
    rings = {a["ring"] for a in arms}
    for r in sorted(rings):
        slots, _ = snap_slots(c, gen, r)
        for s in slots:
            if s.get("values"):
                decoded += 1
                arm_decoded[s.get("arm")] = arm_decoded.get(s.get("arm"), 0) + 1
                if arm_addr.get(s.get("arm")) in gone_funcs:
                    gone_decoded += 1
            else:
                undecoded += 1
    out["slots"] = {"decoded": decoded, "raw": undecoded, "unloaded_decoded": gone_decoded,
                    "unloaded_funcs": len(gone_funcs)}
    say(f"     slots: {decoded} decoded, {undecoded} raw; {len(gone_funcs)} functions unloaded by now, "
        f"{gone_decoded} of their slots decoded")
    check("the chosen calls' parameters decode", decoded > 0 and undecoded == 0, f"{decoded} / {undecoded}")
    if gone_funcs:
        check("calls of functions unloaded before Stop still decode (layouts read while alive)", gone_decoded > 0,
              str(gone_decoded))
    else:
        check.not_run("calls of functions unloaded before Stop still decode", "nothing chosen unloaded by Stop")
    if reloaded:
        both = [n for n, v in reloaded.items()
                if len({x["addr"] for x in v}) > 1 and all(arm_decoded.get(x["index"], 0) > 0 for x in v)]
        check("a name reloaded at a new address: each arm's own slots decode", both != [],
              f"{len(both)} of {len(reloaded)}: {both[:4]}")
    else:
        check.not_run("a name reloaded at a new address is a second arm", "nothing reloaded inside the window")
    snap = stop.get("trace", {}).get("snap", {})
    out["budget"] = {"skipped": snap.get("skipped_budget"), "dropped": snap.get("dropped_budget")}
    say(f"     budget: skipped {snap.get('skipped_budget')}, dropped {snap.get('dropped_budget')}")
    pf = out.get("per_frame_choice")
    ring0 = next((r for r in stop.get("trace", {}).get("snap_rings", []) if r.get("ring") == 0), {})
    if pf and pf["calls_per_s"] > args.per_ring * 1.5:
        kept_per_s = ring0.get("written", 0) / max(1.0, args.record_s)
        say(f"     {pf['name']}: about {pf['calls_per_s']:.0f} calls/s; its ring wrote {ring0.get('written')} "
            f"slots (~{kept_per_s:.0f}/s), dropped {ring0.get('dropped_budget')}")
        check("the budget held on the per-frame choice: lone calls over it dropped, about the budget kept",
              ring0.get("dropped_budget", 0) > 0 and kept_per_s <= args.per_ring * 2.5,
              f"{kept_per_s:.0f}/s kept against {args.per_ring}/s")
    else:
        check.not_run("the budget on the per-frame choice", "no per-frame choice busier than the budget")
    c.request("pe_trace_release")


def build_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--label", default="dumpertest58")
    ap.add_argument("--fixture-check", action="store_true", help="only check that the package carries the probes")
    ap.add_argument("--plain-s", type=float, default=3.0)
    ap.add_argument("--record-s", type=float, default=8.0)
    ap.add_argument("--choose", nargs="*", default=None,
                    help="a real game: choose the functions whose class or name holds one of these substrings")
    ap.add_argument("--max-choices", type=int, default=64)
    ap.add_argument("--per-ring", type=int, default=1000, help="the per-function budget a second (game mode)")
    ap.add_argument("--stacks", action="store_true",
                    help="step 3: native stack snapshots (S0-S7) instead of the step-2 checks; with --choose, the "
                         "stack cost on a real game")
    ap.add_argument("--stack-per-ring", type=int, default=None,
                    help="--stacks: the per-function stack budget a second (on the fixture, unless given, chosen from "
                         f"the plain recording's rates so S5's budget bites: {FIXTURE_STACK_PER_RING} when it fits; "
                         "with --choose, the DLL's own default unless given)")
    ap.add_argument("--stack-total", type=int, default=None,
                    help=f"--stacks: the stack budget a second, every stack choice together ({FIXTURE_STACK_TOTAL} on "
                         "the fixture; with --choose, the DLL's own default unless given)")
    ap.add_argument("--pdb", nargs="?", const="", default=None, metavar="DIR",
                    help="--stacks on the fixture: name the stacks' function starts against the game's PDB through "
                         "dbghelp, the PDB looked for in DIR, else beside the exe the game runs from; without one the "
                         "PDB checks are not run")
    ap.add_argument("--names", action="store_true",
                    help="--stacks --choose: check S3-A1's names on the stacks: each named entry's ufunc is a Function "
                         "of that name in that class (get_object), and its fn is stored in it at one offset common to "
                         f"all (read_mem); the {NAMES_MAX} most frequent entries are asked")
    ap.add_argument("--stack-depth", type=int, default=None, metavar="N",
                    help=f"--stacks --choose: the stacks' depth, 1..{STACK_MAX_DEPTH} ({STACK_DEPTH} unless given: the "
                         "fixture run's checks are written for that depth)")
    ap.add_argument("--self-test", action="store_true",
                    help="run the --stacks helpers against hand-made replies and the --stacks runs (and the step-2 "
                         "run's budget) against a scripted DLL; needs no pipe and no game")
    return ap


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    """The command line as main() reads it, an option that cannot go with the others refused (argparse's exit 2) before
    anything runs. The dry run parses through here too, so the self-test meets the refusal where a live run does.
    --self-test reads no other option, so nothing is refused beside it."""
    ap = build_parser()
    args = ap.parse_args(argv)
    bad = None if args.self_test else pdb_option_problem(args) or game_option_problem(args)
    if bad:
        ap.error(bad)
    return args


def main(argv: list[str] | None = None, connect=None) -> int:
    """`argv` and `connect` (returns a connected client) let the self-test drive main() itself, with no pipe."""
    args = parse_args(argv)
    if args.self_test:
        return self_test()

    check = Checks()
    out: dict = {"label": args.label}
    try:
        c = connect() if connect else PipeClient().connect()
    except PipeError as e:
        say(f"pipe not usable: {e}")
        return 2
    try:
        out["build"] = c.assert_build()
        say(f"DLL build {out['build']}")
        if run_selected(c, check, out, args) == 2:
            return 2
    except PipeError as e:
        check("the run reached its end", False, str(e))
    finally:
        try:
            c.request("pe_profile_stop")
            c.request("pe_trace_release")
        except PipeError:
            pass
        c.close()

    out["checks"] = [{"name": n, "ok": ok, "got": g} for n, ok, g in check.items]
    if check.records:
        out["recorded"] = [{"name": n, "as_expected": e, "got": g} for n, e, g in check.records]
    if check.skipped:
        out["not_run"] = [{"name": n, "why": y} for n, y in check.skipped]
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    suffix = "-fixture" if args.fixture_check else "-stacks" if args.stacks else ""
    path = OUT_DIR / f"{args.label}{suffix}.json"
    path.write_text(json.dumps(out, indent=1, default=str), encoding="utf-8")
    rec = f", {len(check.records)} recorded (not failed)" if check.records else ""
    rec += f", {len(check.skipped)} not run" if check.skipped else ""
    say(f"\n{len(check.items) - check.failed}/{len(check.items)} checks hold{rec}; written to {path}")
    return 1 if check.failed else 0


def run_selected(c, check: Checks, out: dict, args, pid: int | None = None, sleep=time.sleep,
                 clock=time.perf_counter, symbols=None) -> int | None:
    """The run the options pick once the DLL answers: a game's (--choose), or the fixture check and then the step-2 or
    the step-3 checks. Returns 2 when the fixture recorded no call (nothing to check), else None. --self-test drives
    this same function, so the checks it counts are the ones a live run prints; `pid`, `sleep`, `clock` and `symbols`
    as run_stacks takes them."""
    if args.choose is not None:
        if args.stacks:
            run_game_stacks(c, check, out, args, sleep=sleep, clock=clock)
        else:
            run_game(c, check, out, args)
        return None
    say("fixture:")
    out["fixture"] = fixture_check(c, check, args.plain_s)
    if out["fixture"]["total_calls"] == 0:
        say("no calls recorded: is the game running, scanned, and the hook up?")
        return 2
    if not args.fixture_check:
        if args.stacks:
            run_stacks(c, check, out, args, out["fixture"]["probes"], pid=pid, sleep=sleep, clock=clock,
                       symbols=symbols)
        else:
            run_full(c, check, out, args, pid=pid, sleep=sleep, clock=clock)
    return None


def run_full(c, check: Checks, out: dict, args, pid: int | None = None, sleep=time.sleep,
             clock=time.perf_counter) -> None:
    """The step-2 checks on DumperTest58 (the module docstring). `pid`, `sleep` and `clock` as run_stacks takes them,
    so --self-test can drive it against a scripted DLL."""
    # ---- F1: the rows' name keys and per_frame. SnapLate_Begin is invoked once so SnapLate_Call has a row (a key).
    say("\nF1 -- name keys and per_frame:")
    t1 = plain_table(c, args.plain_s, before_stop=lambda: invoke_late(c, 1))
    t2 = data_of(c.request("pe_profile_get", limit=32768, include_unloaded=True))
    t3 = data_of(c.request("pe_profile_get", limit=32768, include_unloaded=True, skip_per_frame=True))
    funcs = t1.get("functions", [])
    keyed = [f for f in funcs if isinstance(f.get("fname_key"), list) and len(f["fname_key"]) == 4]
    check("F1 every row carries a 4-int fname_key", funcs != [] and len(keyed) == len(funcs),
          f"{len(keyed)}/{len(funcs)}")
    rows, rows2, rows3 = fixture_rows(t1), fixture_rows(t2), fixture_rows(t3)
    call = rows.get("SnapProbe_Call", {})
    check("F1 a second fetch gives the same key", call.get("fname_key") is not None and
          call.get("fname_key") == rows2.get("SnapProbe_Call", {}).get("fname_key"), str(call.get("fname_key")))
    check("F1 SnapProbe_PerFrame is per_frame", rows.get("SnapProbe_PerFrame", {}).get("per_frame") is True)
    check("F1 SnapProbe_Call is not per_frame, with or without skip_per_frame",
          "SnapProbe_Call" in rows and "SnapProbe_Call" in rows3 and
          not rows["SnapProbe_Call"].get("per_frame") and not rows3["SnapProbe_Call"].get("per_frame"))
    need = ("SnapNest_Outer", "SnapProbe_Call", "SnapProbe_PerFrame", "SnapProbe_RetOnly", "SnapProbe_ConstRefOnly",
            "SnapLate_Call")
    missing = [n for n in need if "fname_key" not in rows.get(n, {})]
    if missing:
        check("the rows the rest of the run needs carry keys", False, ", ".join(missing))
        return
    out["rows"] = {n: rows[n] for n in need}

    # ---- F2 / F3: refusals, and what they leave behind.
    say("\nF2 / F3 -- refusals:")
    bad = item(rows["SnapNest_Outer"])
    bad["class"] += "X"
    r = c.request("pe_profile_start", trace={"bytes": 32 << 20, "ticked_names": [bad]})
    c.request("pe_profile_stop")
    after = c.request("pe_trace_get", **{"from": 0, "max": 1})   # its own `data` is the records: not data_of
    check("F2 a key whose class string was altered is refused, and alone refuses the Start", not ok_of(r),
          str(r.get("error", ""))[:90])
    check("F2 ...after which no trace is allocated", after.get("allocated") is False, str(after.get("allocated")))
    many = [dict(item(rows["SnapProbe_Call"]), parms_size=2048) for _ in range(512)]
    r = c.request("pe_profile_start", trace={"bytes": 32 << 20, "snapshots": {"funcs": many, "bytes": 8 << 20}})
    c.request("pe_profile_stop")
    after = c.request("pe_trace_get", **{"from": 0, "max": 1})   # its own `data` is the records: not data_of
    check("F3 512 choices of 2,048 B into 8 MB are refused (K = %d < 8), naming the count" %
          k_for(8 << 20, [2048] * 512), not ok_of(r) and "512" in str(r.get("error", "")), str(r.get("error", ""))[:90])
    check("F3 ...and nothing stays allocated", after.get("allocated") is False and "snap" not in after)
    r = c.request("pe_profile_start", trace={"bytes": 32 << 20, "snapshots": {"funcs": [item(rows["SnapProbe_Call"])],
                                                                            "bytes": 4 << 20}})
    c.request("pe_profile_stop")
    check("F3 a 4 MB snapshot buffer is refused", not ok_of(r), str(r.get("error", ""))[:90])

    # ---- F4: a choice never called, alone: the trace is empty and released, and the name still reported.
    say("\nF4 -- a choice never called:")
    r = c.request("pe_profile_start", trace={"bytes": 32 << 20, "snapshots": {"funcs": [item(rows["SnapLate_Call"])],
                                                                            "bytes": 8 << 20}})
    sleep(1.0)
    stop = data_of(c.request("pe_profile_stop"))
    names = {n.get("func"): n for n in stop.get("names", [])}
    check("F2 a name never called this recording is accepted", ok_of(r), str(r.get("error", ""))[:90])
    check("F4 ...and listed as not_called in the Stop reply, the empty trace released",
          names.get("SnapLate_Call", {}).get("not_called") is True and
          stop.get("trace", {}).get("allocated") is False, json.dumps(stop.get("names"))[:120])
    c.request("pe_trace_release")

    # ---- The main recording: SnapNest_Outer ticked by name; the probes chosen.
    say("\nmain recording:")
    chosen = ["SnapProbe_Call", "SnapProbe_PerFrame", "SnapProbe_RetOnly", "SnapProbe_ConstRefOnly", "SnapLate_Call"]
    snap_bytes = 32 << 20
    # The parameter budget by S5's rule, from F1's plain recording: the check below needs it to bite on
    # SnapProbe_PerFrame, whose rate the fixture's frame rate decides, and to cut no other choice.
    win = t1.get("window_ms", 0) / 1000.0
    budget = per_frame_budget({n: rows[n].get("count", 0) / win if win else 0.0 for n in chosen}, None,
                              SNAP_TOTAL_DEFAULT, STEP2_PER_RING)
    out["param_budget"] = budget
    say(f"     the parameter budget: {budget['why']}" + ("" if budget["runs"] else "; its check will not run"))
    trace = {"bytes": 64 << 20, "ticked_names": [item(rows["SnapNest_Outer"])],
             "snapshots": {"funcs": [item(rows[n]) for n in chosen], "bytes": snap_bytes,
                           "per_ring_per_s": budget["per"]}}
    opened = clock()   # the rings open inside the Start: the span is weighed from before it, its longest
    start = c.request("pe_profile_start", trace=trace)
    if not ok_of(start):
        check("the main Start is accepted", False, str(start.get("error", ""))[:120])
        return
    st = data_of(start).get("trace", {})
    out["start"] = st
    caps = [ring_cap(rows[n].get("parms_size", 0)) for n in chosen]
    k = k_for(snap_bytes, caps)
    check("F2 the trace is scoped by one ticked name", st.get("scoped") is True and st.get("ticked_names") == 1,
          f"scoped={st.get('scoped')} ticked_names={st.get('ticked_names')}")
    snap = st.get("snap", {})
    check("F3 the snapshot buffer: one ring per choice, K as the UI computes it",
          snap.get("allocated") is True and snap.get("rings") == len(chosen) and snap.get("slots_per_ring") == k,
          f"rings={snap.get('rings')} K={snap.get('slots_per_ring')} want {k}")
    sleep(args.record_s)
    invoke_late(c, 4242)
    t0 = clock()
    stop = data_of(c.request("pe_profile_stop"))
    stop_s = clock() - t0
    out["stop"] = stop
    check("F4 Stop returns within about 2.5 s", stop_s < 2.5, f"{stop_s:.2f} s")
    gen = stop.get("trace", {}).get("gen", 0)
    main_reply = c.request("pe_profile_get", limit=32768, include_unloaded=True)   # the main recording's own rates

    # ---- K1: roots and the in-scope calls' snapshots.
    say("\nK1 -- the hint and the parameter block reach the trace:")
    recs = read_ring(c)
    fnames = trace_names(c, gen)
    by_name: dict[str, int] = {}
    for a, it in fnames.items():
        if it.get("class_name") == FIXTURE_CLASS:
            by_name[it.get("func_name", "")] = a
    outer, call_a = by_name.get("SnapNest_Outer"), by_name.get("SnapProbe_Call")
    parent = parents(recs)
    entries = [x for x in recs if not x[0] & RET_BIT]
    func_of = {x[0]: x[2] for x in entries}
    roots = [x for x in entries if x[5] & F_ROOT]
    outer_calls = [x for x in entries if x[2] == outer]
    check("K1 roots == SnapNest_Outer calls, every root SnapNest_Outer", roots != [] and len(roots) == len(outer_calls)
          and all(x[2] == outer for x in roots), f"{len(roots)} roots, {len(outer_calls)} Outer")
    in_scope = [x for x in entries if x[2] == call_a and parent.get(x[0]) is not None
                and func_of.get(parent[x[0]]) == outer]
    lone = [x for x in entries if x[2] == call_a and x[5] & F_LONE]
    check("K1 one in-scope SnapProbe_Call per root, every one with its snapshot taken",
          in_scope != [] and abs(len(in_scope) - len(roots)) <= 1 and all(x[5] & F_TAKEN for x in in_scope),
          f"{len(in_scope)} in scope")
    check("K1 the lone SnapProbe_Call calls are recorded, flagged lone and taken",
          lone != [] and abs(len(lone) - len(in_scope)) <= 1 and all(x[5] & F_TAKEN for x in lone),
          f"{len(lone)} lone")

    # ---- F4 / F5: the layouts and the slots.
    say("\nF4 / F5 -- layouts and slots:")
    head, arms, layouts = snap_layouts(c, gen)
    out["arms"] = arms
    arm_by = {}
    for a in arms:
        arm_by.setdefault(a.get("func_name"), []).append(a)
    late = arm_by.get("SnapLate_Call", [])
    check("F4 the late choice's arm was read (by Stop's last pass)", late != [] and late[0].get("state") == "read",
          json.dumps(late)[:120])
    if late:
        say(f"     SnapLate_Call: armed then read after {late[0].get('read_ms')} ms")
    call_arms = arm_by.get("SnapProbe_Call", [])
    lay = layouts[call_arms[0]["layout"]] if call_arms and "layout" in call_arms[0] else {}
    # FNames compare without case and the pool keeps the casing it saw FIRST: on DumperTest58 "Round" renders "round"
    # (measured, 2026-10-07). The rig matches the fixture's names the same way.
    names_l = [CANON.get(p["name"].lower(), p["name"]) for p in lay.get("params", [])]
    kinds = {n: p.get("kind") for n, p in zip(names_l, lay.get("params", []))}
    out["call_kinds"] = kinds
    # What UHT emits, measured 2026-10-07: a const TArray& carries CPF_OutParm | CPF_ConstParm (const_ref); a const
    # FString& carries neither and is a plain input.
    check("F5 kinds as UHT emits them: Values const_ref, Label in, OutTwice out, InOut in_out, ReturnValue return",
          kinds.get("Values") == "const_ref" and kinds.get("Label") == "in" and kinds.get("OutTwice") == "out" and
          kinds.get("InOut") == "in_out" and kinds.get("ReturnValue") == "return", json.dumps(kinds)[:160])
    ring_of = {n: chosen.index(n) for n in chosen}
    slots, orphans = snap_slots(c, gen, ring_of["SnapProbe_Call"])
    ent = [s for s in slots if s.get("phase") == "entry"]
    aft = [s for s in slots if s.get("phase") == "return"]
    wrong = [(s.get("index"), entry_expect(s.get("values", []), names_l)) for s in ent]
    wrong = [w_ for w_ in wrong if w_[1]]
    check("F5 every SnapProbe_Call entry decodes to its Round's values", ent != [] and not wrong,
          f"{len(ent)} entries; first wrong: {wrong[:1]}")
    bad_after = []
    for s in aft:
        v = dict(zip(names_l, s.get("values", [])))
        r = None
        ent_of = next((e for e in ent if e.get("entry_seq") == s.get("entry_seq")), None)
        if ent_of:
            r = num(dict(zip(names_l, ent_of.get("values", []))).get("Round", [None]))
        if r is None:
            continue
        if not (num(v.get("OutTwice", [None])) == 2 * r and num(v.get("InOut", [None])) == 100 + r and
                num(v.get("ReturnValue", [None])) == 3 * r and v.get("Round", ["x", -1]) == ["", MARK_MISSING]):
            bad_after.append(s.get("index"))
    check("F5 the after copy: OutTwice=2R, InOut=100+R, ReturnValue=3R, the In parameters blank",
          aft != [] and not bad_after, f"{len(aft)} after copies; wrong: {bad_after[:3]}")
    check("F5 no orphans", orphans == 0, str(orphans))
    check("K1 no slot without its parameter block",
          all(not (s.get("flags", 0) & SNAP_NULL_PARAMS) for s in slots), "")
    ret_slots, _ = snap_slots(c, gen, ring_of["SnapProbe_RetOnly"])
    check("F5 SnapProbe_RetOnly has an after copy (FUNC_HasOutParms covers a lone return)",
          any(s.get("phase") == "return" for s in ret_slots), f"{len(ret_slots)} slots")
    cr_slots, _ = snap_slots(c, gen, ring_of["SnapProbe_ConstRefOnly"])
    out["const_ref_only_after"] = any(s.get("phase") == "return" for s in cr_slots)
    say(f"     fact: SnapProbe_ConstRefOnly takes an after copy: {out['const_ref_only_after']}")
    pf = stop.get("trace", {}).get("snap_rings", [])
    pf_ring = next((x for x in pf if x.get("ring") == ring_of["SnapProbe_PerFrame"]), {})
    out["per_frame_ring"] = pf_ring
    per = budget_echo(budget["per"])
    main_pf, main_what = main_table_rate(main_reply)
    ring_pf = ring_rate(pf_ring, t0 + stop_s - opened)
    out["param_budget"].update(main_rate=main_pf, main_rate_ring=ring_pf)
    check(f"the budget: {MAIN_TABLE}", main_pf is not None, main_what)
    # A reply that cannot give the rate fails just above, and the budget check then runs as it did before the main
    # rate was read: standing it down on a rate nobody measured would hide that reply's defect. One that gives a
    # plausible, wrong rate stands it down only where the probe's own parameter ring agrees (main_rate).
    why = budget["why"] if not budget["runs"] else None if main_pf is None else \
        main_rate_problem(main_pf, per, budget["rates"].get("SnapProbe_PerFrame", 0.0), ring_pf)
    if why is None:
        # The first `per` calls of each second are kept. The bound is the one the run always had, now on the budget
        # sent: one call a second over it, and three seconds over --record-s, which the Start, invoke_late and the
        # Stop (up to 2.5 s, F4) keep the ring open beyond.
        check(f"{STEP2_BUDGET} {per}/s are dropped, the first ones kept",
              int_or(pf_ring.get("dropped_budget"), 0) > 0 and
              0 < int_or(pf_ring.get("written"), 0) <= (per + 1) * (args.record_s + 3),
              f"written {pf_ring.get('written')}, dropped {pf_ring.get('dropped_budget')}")
    else:
        # A budget that cannot bite may still drop calls, but not steadily enough to hold the ring to it: a failure
        # would blame the DLL for the run.
        check.not_run(f"{STEP2_BUDGET} the budget a second are dropped, the first ones kept", why)

    # ---- F6: code_addr.
    say("\nF6 -- code_addr:")
    pid = host_pid() if pid is None else pid
    rng = module_range(pid) if pid else None
    ca = fnames.get(outer, {}).get("code_addr") if outer else None
    if rng and ca:
        check("F6 SnapNest_Outer's code_addr lies inside the game's module", rng[0] <= int(ca, 16) < rng[1],
              f"{ca} in [{rng[0]:#x}, {rng[1]:#x})")
    else:
        check("F6 SnapNest_Outer carries a code_addr", bool(ca), str(ca))
    check.not_run("F6 a script function's code_addr is \"\"", "the fixture is C++ only")

    # ---- release.
    c.request("pe_trace_release")
    after = c.request("pe_trace_get", **{"from": 0, "max": 1})   # its own `data` is the records: not data_of
    check("the release frees everything", after.get("allocated") is False and "snap" not in after)


# ======================================================================================================================
# [LIVEFUNCS-STEP3] Native stack snapshots: --stacks. The checks are the ledger's S0-S7 (docs/live-funcs-step3-items.md,
# "8. Live checks"); the wire is the design's frozen section 3. Every pure piece below is run by --self-test.
# ======================================================================================================================

F_STACK_TAKEN, F_STACK_BUDGET = 32, 64
STK_PARTIAL, STK_FAULT, STK_MORE, STK_BADSP, STK_LOWSTACK, STK_NOCAPTURER = 1, 2, 4, 8, 16, 0x8000
# A slot with one of these took no walk, or kept the caller alone; More (deeper than the depth) is a whole stack cut.
STK_BAD = STK_PARTIAL | STK_FAULT | STK_BADSP | STK_LOWSTACK | STK_NOCAPTURER
STACK_DEPTH = 16          # the Start asks for Linie's default depth: the depth the ledger's checks are written for
STACK_MAX_DEPTH = 62      # Linie clamps a depth to 1..this
BUDGET_MAX = 0xFFFFFF     # a budget word holds its count in 24 bits: the DLL clamps to 1..this and echoes what it used
FIXTURE_EXE = "DumperTest58-Win64-Shipping.exe"   # frame 0's module when the process cannot be asked (no out/host.pid)
KNOWN_PE = "process_event"
# S3's known checks by name, so --self-test's controls name the check each fault must fail.
S3_OWN_IN = "S3 every in-scope stack holds an own frame (the hook under SnapNest_Outer's ProcessEvent)"
S3_KNOWN_IN = 'S3 every in-scope stack holds known:"process_event" before its own frame'
S3_KNOWN_LONE = 'S3 no lone stack holds known:"process_event"'
S3_KNOWN_ONE = "S3 every known frame names one function, and no stack holds two"
S5_WINDOW = "S5 SnapProbe_PerFrame's stack ring keeps about"
S5_PARAMS = "S5 the parameter counters are untouched by the stack budget (snap skipped and dropped 0)"
# The main recording's own SnapProbe_PerFrame rate is read from the table fetched after its Stop. The check that the
# reply carries it, by name: each run puts its own prefix before it.
MAIN_TABLE = "the main recording's table carries SnapProbe_PerFrame over a window"
# The Start's stack list, in order: the DLL numbers stack rings by the accepted items' order, and S1's join checks
# that every slot of ring s belongs to the s-th name.
STACK_CHOICES = ("SnapProbe_Call", "SnapProbe_PerFrame")
STACKS_ONLY_S = 3.0       # the stacks-only recording: SnapProbe_Call runs about four times a second
# The fixture run's budgets when none is given (the ledger's 8.1). 30/s a function is kept while it sits clearly below
# SnapProbe_PerFrame's measured rate; a fixture at about 30 fps calls it about 30 times a second, and then a lower
# budget is chosen (per_frame_budget). A game run (8.3) sends none unless given: it measures the DLL's own defaults.
FIXTURE_STACK_PER_RING, FIXTURE_STACK_TOTAL = 30, 200
# "Clearly" above and below: a frame rate wobbles from second to second, and stacks slow the game a little, so the
# budget keeps this factor from SnapProbe_PerFrame's rate (or it may not bite in every second) and from every other
# choice's (or the budget may cut the calls the other checks count), and the total keeps this factor of their rates
# free. The step-2 run's parameter budget keeps the same margins.
BUDGET_MARGIN = 1.5
# --names (the ledger's S3-A1 on a real game): its checks by name, so --self-test's controls name the check each fault
# must fail.
NAMES_IS = "A1 every named entry's ufunc is a Function of that name, in that class"
NAMES_AT = "A1 every named entry's fn is stored inside its UFunction, at one offset common to all"
NAMES_MAX = 64            # entries asked about, two requests each, the most frequent first; the rest are counted
# The object classes a ufunc may have: a delegate's signature is a UFunction subclass that ProcessEvent can enter too.
NAME_FUNC_CLASSES = ("Function", "DelegateFunction", "SparseDelegateFunction")
# The bytes of a UFunction searched for its fn: the window the DLL searches for UFunction::Func in (Aura's
# EnsureUFunctionFuncOffset reads 8 bytes at +0x80..+0x158), so every offset the DLL can have read the name through lies
# inside, and no more is asked.
NAMES_READ = 0x160
# read_mem is one copy under SEH, all or nothing, and a UFunction (0xC8 bytes on UE 4.18, about 0xE0 on UE5) is smaller
# than the window: where it ends a block whose next page cannot be read, the whole read fails. These smaller reads are
# tried in turn, down to the smallest UFunction, so the bytes the object does have are still searched; they jump past
# some sizes, so a slot beyond the one that succeeded is then read alone.
NAMES_READ_RETRY = (0x100, 0xE0, 0xC8)


def stack_item(row: dict) -> dict:
    """A by-name stack choice: the strings the table showed and the key. A stack has no parameter block, no size."""
    return {"class": row["class_name"], "func": row["func_name"], "keys": [row["fname_key"]]}


def stack_ring_cap(depth: int) -> int:
    """Linie::StackRingCap: eight bytes a frame, the depth clamped as the DLL clamps it."""
    return min(max(int(depth), 1), STACK_MAX_DEPTH) * 8


def k_with_stacks(snap_bytes: int, param_caps: list[int], stack_rings: int, depth: int) -> int:
    """K with the stack terms (the design's 2.2). A stack ring is one more ring of the same allocation, a slot being a
    24-byte header and 8 x depth bytes, so step 2's formula holds with the stack rings' caps appended."""
    caps = list(param_caps) + [stack_ring_cap(depth)] * stack_rings
    return k_for(snap_bytes, caps) if caps else 0


def budget_echo(v: int) -> int:
    """The budget the DLL uses, and echoes, for one the rig sent."""
    return min(max(int(v), 1), BUDGET_MAX)


def stack_echo_state(trace: dict, asked: int) -> str:
    """What a Start reply says about its stack choices (the review's M3): "old" when names.stacks is absent (a DLL
    without step 3 ignores the stacks object), "refused" when it names fewer than were asked, "ok". trace.stack alone
    cannot tell the first two apart: the DLL sends it only when a stack ring exists."""
    names = trace.get("names")
    if not isinstance(names, dict) or type(names.get("stacks")) is not int:
        return "old"
    return "refused" if names["stacks"] < asked else "ok"


def parse_site(s: dict) -> dict:
    """One `sites` entry with its hex strings as integers. An absent field is None; an absent module, known, class or
    func is "". ufunc / class / func / shared are S3-A1's: the UFunction whose native entry fn is, and how many
    functions enter there when more than one."""
    def hx(k: str) -> int | None:
        v = s.get(k)
        try:
            return int(v, 16) if isinstance(v, str) and v else None
        except ValueError:
            return None

    def num_(k: str) -> int | None:
        return s.get(k) if type(s.get(k)) is int else None
    return {"addr": hx("addr"), "module": s.get("module") or "", "module_base": hx("module_base"), "rva": num_("rva"),
            "fn": hx("fn"), "fn_rva": num_("fn_rva"), "unwind": s.get("unwind") is True, "own": s.get("own") is True,
            "known": s.get("known") or "", "ufunc": hx("ufunc"), "class": str(s.get("class") or ""),
            "func": str(s.get("func") or ""), "shared": num_("shared")}


def resolve_stack_page(d: dict) -> tuple[list[dict], list[tuple]]:
    """One kind:"stack" page's slots, each frame index replaced by its site. The indices point into THIS page's
    `sites` (the DLL de-duplicates per page), so they mean nothing on another page. Also returns every
    (slot index, frame index) that names no site: kept, because a dropped frame would shorten a stack silently."""
    sites = [parse_site(s if isinstance(s, dict) else {}) for s in d.get("sites") or []]
    slots: list[dict] = []
    bad: list[tuple] = []
    for it in d.get("items") or []:
        raw = it.get("frames")
        frames: list[dict] = []
        for f in raw if isinstance(raw, list) else []:
            if type(f) is int and 0 <= f < len(sites):
                frames.append(sites[f])
            else:
                bad.append((it.get("index"), f))
        if not isinstance(raw, list):
            bad.append((it.get("index"), "no frames"))
        slots.append({"index": it.get("index"), "entry_seq": it.get("entry_seq"),
                      "flags": int_or(it.get("flags"), 0), "ticks": int_or(it.get("ticks"), 0), "frames": frames})
    return slots, bad


def page_stack_ring(fetch, ring: int) -> dict:
    """Every slot of one stack ring, read page by page through `fetch(from)`, which returns one reply's data. The read
    ends when a page does not move `next` past where it began, or reaches the ring's end -- never on an empty page
    (the review's M1): orphans and out-of-turn slots are skipped without being listed, so a page can be empty in the
    middle of a ring that has more. The ring's window comes from the first reply's `rings`."""
    out: dict = {"slots": [], "bad_refs": [], "orphans": 0, "ring": {}, "problems": [], "pages": 0}
    frm, end = 0, None
    while True:
        d = fetch(frm)
        out["pages"] += 1
        if d.get("stale"):
            out["problems"].append("stale: the trace was replaced while it was read")
            break
        if d.get("items") and d.get("kind") != "stack":
            out["problems"].append(f"items without kind \"stack\" (kind {d.get('kind')!r}): the DLL ignored it")
            break
        if end is None:
            out["ring"] = next((r for r in d.get("rings") or [] if isinstance(r, dict) and r.get("ring") == ring), {})
            if not out["ring"]:
                out["problems"].append(f"the reply's rings do not list ring {ring}")
            end = int_or(out["ring"].get("written"), 0)
        slots, bad = resolve_stack_page(d)
        out["slots"].extend(slots)
        out["bad_refs"].extend(bad)
        out["orphans"] += int_or(d.get("orphans"), 0)
        nxt = d.get("next", frm)
        if type(nxt) is not int or nxt <= frm or nxt >= end:
            break
        frm = nxt
    return out


def stack_flag_problems(entries: dict[int, tuple], func: int | None, exact: int | None = None) -> list[int]:
    """The entries of a stack-chosen function that do not say what became of its stack: each carries 32 (taken) or
    64 (over the budget), never both. `exact` is the whole flag word when it is known (a lone stack-only call)."""
    bad: list[int] = []
    for seq, e in entries.items():
        if e[2] != func:
            continue
        f = e[5]
        if (f & (F_STACK_TAKEN | F_STACK_BUDGET)) in (0, F_STACK_TAKEN | F_STACK_BUDGET) or \
                (exact is not None and f != exact):
            bad.append(seq)
    return bad


S1_PROBLEMS = ("no_slot", "two_slots", "stray", "bad_flags", "short")


def s1_join(entries: dict[int, tuple], ring_func: dict[int, int | None], slots_by_ring: dict[int, list[dict]]) -> dict:
    """S1's join of stack slots to trace entries by entry_seq. `entries` maps an entry's seq to its record and
    `ring_func` a stack ring to the function its choice names. Each entry of that function flagged 32 must have one
    slot in its ring, and each slot must belong to such an entry; a slot must not be cut short or flagged as bad."""
    res: dict = {"taken": 0, "slots": 0}
    for k in S1_PROBLEMS:
        res[k] = []
    for ring, func in ring_func.items():
        slots = slots_by_ring.get(ring, [])
        res["slots"] += len(slots)
        per_seq: dict = {}
        for s in slots:
            seq = s.get("entry_seq")
            per_seq[seq] = per_seq.get(seq, 0) + 1
            e = entries.get(seq)
            if e is None or e[2] != func or not e[5] & F_STACK_TAKEN:
                res["stray"].append((ring, s.get("index")))
            if s.get("flags", 0) & STK_BAD:
                res["bad_flags"].append((ring, s.get("index"), s.get("flags")))
            if len(s.get("frames", [])) < 3:
                res["short"].append((ring, s.get("index"), len(s.get("frames", []))))
        for seq, e in entries.items():
            if e[2] == func and e[5] & F_STACK_TAKEN:
                res["taken"] += 1
                n = per_seq.get(seq, 0)
                if n == 0:
                    res["no_slot"].append(seq)
                elif n > 1:
                    res["two_slots"].append(seq)
    return res


def ce_text(site: dict) -> str:
    """A frame as CE's module form, the Copy text of view D: `"module"+RVA` survives a relaunch, an address does not."""
    return f'"{site["module"]}"+{site["rva"]:X}'


def ce_text_addr(text: str, bases: dict[str, int]) -> int | None:
    """Where CE puts `"module"+RVA`, given each module's base. Module names compare without case, as Windows compares
    them (the review's L11)."""
    m = re.fullmatch(r'"([^"]+)"\+([0-9A-Fa-f]+)', text)
    if not m:
        return None
    base = {k.lower(): v for k, v in bases.items()}.get(m.group(1).lower())
    return None if base is None else base + int(m.group(2), 16)


def s2_frame0_problems(site: dict, game_module: str, exe: tuple[int, int] | None) -> list[str]:
    """What is wrong with a slot's frame 0, the game's return address into the hook. It must be in the game's exe by
    name (ignoring case), by address (the image psapi reads, when the pid is known), and through the CE text, added back
    on psapi's base: a detector apart from the DLL's own module_base."""
    bad: list[str] = []
    if site["module"].lower() != game_module.lower():
        bad.append(f"module {site['module']!r}")
    if site["addr"] is None or site["module_base"] is None or site["rva"] is None:
        return bad + ["no addr / module_base / rva"]
    if site["module_base"] + site["rva"] != site["addr"]:
        bad.append("module_base + rva != addr")
    base = site["module_base"]
    if exe:
        if not exe[0] <= site["addr"] < exe[1]:
            bad.append("addr outside the exe's image")
        if site["rva"] >= exe[1] - exe[0]:
            bad.append("rva past the image's size")
        base = exe[0]
    if ce_text_addr(ce_text(site), {game_module: base}) != site["addr"]:
        bad.append(f"{ce_text(site)} does not add back to {site['addr']:#x}")
    return bad


def site_problems(site: dict) -> list[str]:
    """A site at odds with itself. Its module and RVA add up to its address; its fn is the start of the function that
    holds addr - 1, so it lies below addr and adds up from module_base + fn_rva; fn comes with unwind data or not at
    all."""
    if site["addr"] is None:
        return ["no addr"]
    bad: list[str] = []
    if site["module"]:
        if site["module_base"] is None or site["rva"] is None:
            bad.append("a module without module_base / rva")
        elif site["module_base"] + site["rva"] != site["addr"]:
            bad.append("module_base + rva != addr")
    if site["unwind"]:
        if site["fn"] is None:
            bad.append("unwind without fn")
        else:
            if not site["fn"] < site["addr"]:
                bad.append("fn not below addr")
            if site["module_base"] is not None and site["fn_rva"] is not None and \
                    site["module_base"] + site["fn_rva"] != site["fn"]:
                bad.append("module_base + fn_rva != fn")
    elif site["fn"] is not None:
        bad.append("fn without unwind")
    return bad


def nesting(frames: list[dict]) -> tuple[int | None, int | None]:
    """The index of the first frame labelled known:"process_event" and of the first `own` frame (None when absent)."""
    known = next((i for i, f in enumerate(frames) if f["known"] == KNOWN_PE), None)
    own = next((i for i, f in enumerate(frames) if f["own"]), None)
    return known, own


def known_before_own(n: tuple[int | None, int | None]) -> bool:
    return n[0] is not None and n[1] is not None and n[0] < n[1]


def outer_frame(frames: list[dict], code_addr: int, bound: int | None) -> int | None:
    """The first frame before index `bound` whose function start is `code_addr` (pe_trace_names' native entry)."""
    for i, f in enumerate(frames[: len(frames) if bound is None else bound]):
        if f["fn"] is not None and f["fn"] == code_addr:
            return i
    return None


def others_room(rates: dict[str, float]) -> float:
    """What a total must leave the choices other than SnapProbe_PerFrame: BUDGET_MARGIN times their rates, summed."""
    return sum(r for n, r in rates.items() if n != "SnapProbe_PerFrame") * BUDGET_MARGIN


def total_starves(rates: dict[str, float], per: int, total: int) -> bool:
    """Whether one total, admitted in call order each second, can starve the other choices at these rates: beside
    what SnapProbe_PerFrame may keep of it (its per-function budget, or its rate when lower) it does not leave them
    their room. It says what may happen; whether it did is the main recording's to show."""
    return budget_echo(total) < min(budget_echo(per), rates.get("SnapProbe_PerFrame", 0.0)) + others_room(rates)


def per_frame_budget(rates: dict[str, float], given: int | None, total: int,
                     default: int = FIXTURE_STACK_PER_RING) -> dict:
    """A fixture run's per-function budget, and whether the check that SnapProbe_PerFrame is held to it can run
    (`runs`), from the plain recording's calls a second of each choice the budget applies to (by name): the stack
    choices for S5, the parameter choices for the step-2 run. The check needs the budget to bite on SnapProbe_PerFrame,
    so it sits BUDGET_MARGIN below that rate, and must not cut the other choices, so it sits BUDGET_MARGIN above theirs.
    What the ring is held to is the lower of the budget and the total, so that is what must bite. The total is one
    total for every choice, admitted in call order each second, and SnapProbe_PerFrame, called every frame, may spend
    it before the others are called: beside what SnapProbe_PerFrame may keep it must leave them BUDGET_MARGIN times
    their rates, or it may starve them (`starves`, total_starves at the plain rates: a prediction, printed), and
    SnapProbe_PerFrame's share of the total is then no budget it can be held to. A given budget is sent as given. Else
    `default` when it fits; else the whole number between the bounds as far from both as it can be (their geometric
    mean); else `default`, the check not run."""
    pf = rates.get("SnapProbe_PerFrame", 0.0)
    others = [r for n, r in rates.items() if n != "SnapProbe_PerFrame"]
    top, room = max(others, default=0.0), others_room(rates)
    lo, hi = top * BUDGET_MARGIN, min(pf / BUDGET_MARGIN, budget_echo(total) - room)
    seen = ", ".join(f"{n} {r:.1f}/s" for n, r in rates.items())
    out: dict = {"rates": dict(rates), "given": given is not None, "bounds": [round(lo, 2), round(hi, 2)]}

    def bites(per: int) -> bool:
        return min(budget_echo(per), budget_echo(total)) * BUDGET_MARGIN <= pf

    def starves(per: int) -> bool:
        return total_starves(rates, per, total)

    def starving(per: int) -> str:
        return (f"; the total {budget_echo(total)}/s is under the {min(budget_echo(per), pf) + room:.1f}/s that "
                f"SnapProbe_PerFrame may keep plus {BUDGET_MARGIN:g}x the other choices' rates: one total, admitted in "
                f"call order each second, which SnapProbe_PerFrame may spend before the others are called and so "
                f"starve them, and its share of the total is no budget it can be held to")
    if given is not None:
        why = f"{given}/s as given ({seen})"
        if not bites(given):
            why += f": SnapProbe_PerFrame is not {BUDGET_MARGIN:g}x above it, so it cannot bite"
        if starves(given):
            why += starving(given)
        return dict(out, per=given, runs=bites(given) and not starves(given), starves=starves(given), why=why)
    if lo <= default <= hi:
        per = default
    else:
        per = int(math.sqrt(lo * hi)) if lo > 0 and hi > 0 else int(hi)
        if per < lo:
            per = math.ceil(lo)
        per = max(per, 1)
        if not lo <= per <= hi:
            return dict(out, per=default, runs=False, starves=starves(default),
                        why=f"no budget sits {BUDGET_MARGIN:g}x below SnapProbe_PerFrame and {BUDGET_MARGIN:g}x above "
                            f"every other choice with their room left in the total ({seen}); {default}/s sent" +
                            (starving(default) if starves(default) else ""))
    # At or under `hi` the budget bites by construction, and leaves the other choices their room in the total.
    return dict(out, per=per, runs=True, starves=False, why=f"{per}/s, chosen between {lo:.1f} and {hi:.1f} ({seen})" +
                ("" if per == default else f": {default}/s does not fit"))


def main_rate_problem(main_pf: float, held_to: int, plain_pf: float, ring_pf: float | None = None) -> str | None:
    """Why the check that SnapProbe_PerFrame is held to `held_to` a second cannot run on the main recording, or None:
    the budget was chosen on the plain recording's `plain_pf`, but whether it bit is the main recording's own rate,
    which the trace may have slowed or the frame rate moved. That rate is its table's `main_pf`, or `ring_pf`, what
    the probe's own ring shows (ring_rate), where that is higher (main_rate)."""
    seen = main_pf if ring_pf is None else main_rate(main_pf, ring_pf)
    if seen >= held_to * BUDGET_MARGIN:
        return None
    ring = "" if ring_pf is None else f" (its table {main_pf:.1f}/s, its ring at least {ring_pf:.1f}/s)"
    return (f"the main recording ran SnapProbe_PerFrame at {seen:.1f}/s{ring}, under {BUDGET_MARGIN:g}x the "
            f"{held_to}/s it is held to, so the budget may not bite (chosen on the plain recording's {plain_pf:.1f}/s)")


def starved_in_scope(in_entries: list[tuple], kept: int, rates: dict[str, float], main_pf: float | None, per: int,
                     total: int) -> str | None:
    """Why S3's in-scope checks cannot run, or None. `in_entries` are the main recording's in-scope SnapProbe_Call
    entries, `kept` the in-scope stacks it kept. They stand down only where the recording shows the stack budget
    refused them all -- none kept, every entry flagged 64 -- and a total explains it: one total, admitted in call order
    each second, that starves the others at SnapProbe_PerFrame's plain rate or at the main recording's own. Refusals
    no total explains are the DLL's, and the checks run on them; where some were kept, they run over those."""
    if kept or not in_entries or not all(e[5] & F_STACK_BUDGET for e in in_entries):
        return None
    at = [("the plain recording's", rates.get("SnapProbe_PerFrame", 0.0))] + \
        ([("the main recording's", main_pf)] if main_pf is not None else [])
    for which, pf in at:
        if total_starves(dict(rates, SnapProbe_PerFrame=pf), per, total):
            return (f"the stack budget refused all {len(in_entries)} in-scope SnapProbe_Call stacks (each entry flagged "
                    f"64): one total of {budget_echo(total)}/s, admitted in call order each second, starves them at "
                    f"{which} {pf:.1f}/s SnapProbe_PerFrame")
    return None


def main_table_rate(reply: dict) -> tuple[float | None, str]:
    """SnapProbe_PerFrame's calls a second in the main recording's table (the pe_profile_get reply after its Stop),
    with what it was measured from; or None with what is wrong with the reply: an error, no window, or no row for the
    probe. A reply that cannot give the rate is not a slow probe, so it never stands a check down."""
    if not ok_of(reply):
        return None, f"pe_profile_get failed: {str(reply.get('error', ''))[:90]}"
    d = data_of(reply)
    win = d.get("window_ms")
    if type(win) not in (int, float) or win <= 0:
        return None, f"window_ms {win!r}"
    row = fixture_rows(d).get("SnapProbe_PerFrame")
    if row is None or type(row.get("count")) is not int:
        return None, f"no SnapProbe_PerFrame row with a count over its {win} ms"
    return row["count"] / (win / 1000.0), f"{row['count']} calls over {win} ms"


def ring_rate(ring: dict, span_s: float) -> float:
    """The least SnapProbe_PerFrame's calls a second can have been in a traced recording, from its own ring (a reply's
    `written` and `dropped_budget`): Linie writes every lone call of a chosen function to that function's ring or
    counts it in the ring's dropped_budget, and the trace was open `span_s` at most. A table can be wrong and still
    plausible, a count too low or a window too long; this is never above the truth on a DLL that counts its ring."""
    calls = int_or(ring.get("written"), 0) + int_or(ring.get("dropped_budget"), 0)
    return calls / span_s if span_s > 0 else 0.0


def main_rate(main_pf: float | None, ring_pf: float) -> float | None:
    """The main recording's SnapProbe_PerFrame rate as the checks weigh it: its table's, or its ring's where that is
    higher, since a table that undercounts must not stand down a check the ring shows can run. None where the table
    gives none: that reply fails a check of its own and stands nothing down."""
    return None if main_pf is None else max(main_pf, ring_pf)


def budget_window(per_s: int, span_lo: float, span_hi: float) -> tuple[float, float]:
    """The slots a ring keeps when its function is called more often than its budget. The DLL admits the first per_s
    calls of each second, so a recording of L seconds keeps per_s x L, give or take one second's worth: the windows
    at either end are partly covered. The span is known only between two bounds (the Start's reply, the Stop)."""
    return per_s * (span_lo - 1), per_s * (span_hi + 1)


def reweigh_total(mean_us: float | None) -> int | None:
    """D3's rule once a capture's mean cost is measured: floor(2000 us / mean us), rounded down to a multiple of 50."""
    if not mean_us or mean_us <= 0:
        return None
    return int(2000 // mean_us) // 50 * 50


def stack_cost(stack: dict, qpc_freq: int, span_s: float) -> dict:
    """S6's figures from a reply's trace.stack: mean and max microseconds a capture, captures a second."""
    caps = stack.get("captures") or 0
    spent, mx = stack.get("spent_ticks") or 0, stack.get("max_ticks") or 0
    mean_us = spent / caps / qpc_freq * 1e6 if caps and qpc_freq else None
    return {"captures": caps, "spent_ticks": spent, "max_ticks": mx, "mean_us": mean_us,
            "max_us": mx / qpc_freq * 1e6 if qpc_freq else None,
            "captures_per_s": caps / span_s if span_s > 0 else None, "reweighed_total": reweigh_total(mean_us)}


def slot_ticks_agree(slots: list[dict], stack: dict) -> dict:
    """S6's second detector: the slots' own ticks against the totals. With no slot overwritten and none orphaned,
    the slots' sum is spent_ticks exactly, and none is above max_ticks."""
    ticks = [s.get("ticks", 0) or 0 for s in slots]
    return {"slots": len(ticks), "sum": sum(ticks), "max": max(ticks, default=0),
            "agree": sum(ticks) == stack.get("spent_ticks") and max(ticks, default=0) <= (stack.get("max_ticks") or 0)}


def fmt(v, spec: str = ".2f") -> str:
    return "-" if v is None else format(v, spec)


def int_or(v, default: int) -> int:
    """A reply's count, or `default` when it is absent or not an integer: a malformed reply fails its check, it does
    not stop the run with a TypeError."""
    return v if type(v) is int else default


def host_pid() -> int:
    try:
        return int(HOST_PID.read_text().strip()) if HOST_PID.exists() else 0
    except (OSError, ValueError):
        return 0


def exe_name(pid: int) -> str | None:
    """The game's exe file name as its process reports it (psapi), for frame 0's module."""
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    psapi = ctypes.WinDLL("psapi", use_last_error=True)
    k32.OpenProcess.restype = w.HANDLE
    k32.CloseHandle.argtypes = [w.HANDLE]
    psapi.GetModuleBaseNameW.argtypes = [w.HANDLE, w.HMODULE, w.LPWSTR, w.DWORD]
    h = k32.OpenProcess(0x0410, False, pid)   # QUERY_INFORMATION | VM_READ
    if not h:
        return None
    try:
        buf = ctypes.create_unicode_buffer(32768)
        return buf.value if psapi.GetModuleBaseNameW(h, None, buf, len(buf)) else None   # NULL module: the exe
    finally:
        k32.CloseHandle(h)


def machine() -> dict:
    """The machine a timing belongs to: the CPU first, and whether it runs on a battery (docs/working-lessons.md,
    1.al)."""
    out: dict = {"cpu": "", "logical_cpus": os.cpu_count(), "battery": None, "on_ac": None}
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"HARDWARE\DESCRIPTION\System\CentralProcessor\0") as k:
            out["cpu"] = str(winreg.QueryValueEx(k, "ProcessorNameString")[0]).strip()
    except OSError:
        pass

    class SYSTEM_POWER_STATUS(ctypes.Structure):
        _fields_ = [("ACLineStatus", ctypes.c_ubyte), ("BatteryFlag", ctypes.c_ubyte),
                    ("BatteryLifePercent", ctypes.c_ubyte), ("SystemStatusFlag", ctypes.c_ubyte),
                    ("BatteryLifeTime", w.DWORD), ("BatteryFullLifeTime", w.DWORD)]
    ps = SYSTEM_POWER_STATUS()
    if ctypes.WinDLL("kernel32").GetSystemPowerStatus(ctypes.byref(ps)):
        out["battery"] = ps.BatteryFlag not in (128, 255)   # 128: no system battery; 255: unknown
        out["on_ac"] = ps.ACLineStatus == 1
    return out


def calls_per_s(table: dict) -> float:
    return table.get("total_calls", 0) / (table.get("window_ms", 0) / 1000.0) if table.get("window_ms") else 0.0


def read_stack_rings(c, gen: int, rings: int) -> dict[int, dict]:
    def fetch(ring: int):
        return lambda frm: data_of(c.request("pe_snap_get", kind="stack", gen=gen, ring=ring,
                                             **{"from": frm, "max": 4096}))
    return {s: page_stack_ring(fetch(s), s) for s in range(rings)}


def run_stacks(c, check: Checks, out: dict, args, rows: dict, pid: int | None = None, sleep=time.sleep,
               clock=time.perf_counter, symbols=None) -> None:
    """--stacks on DumperTest58: S0-S7 (the module docstring). `rows` are the fixture check's rows, by name. `pid`
    (out/host.pid when None) names the game process for frame 0's module; --self-test passes 0, a scripted DLL as
    `c`, and a fake clock that only its sleep moves. `symbols` opens --pdb's session as open_game_pdb does
    (open_game_pdb when None); --self-test passes the scripted game's."""
    need = ("SnapNest_Outer",) + STACK_CHOICES
    missing = [n for n in need if not isinstance((rows.get(n) or {}).get("fname_key"), list)]
    if missing:
        check("the rows the stack run needs carry keys", False, ", ".join(missing))
        return
    pid = host_pid() if pid is None else pid
    exe = module_range(pid) if pid else None
    named = exe_name(pid) if pid else None
    game_module = named or FIXTURE_EXE
    out["machine"] = machine()
    out["game_module"] = {"pid": pid, "name": game_module, "from_process": bool(named),
                          "image": [hex(x) for x in exe] if exe else None}
    # The per-function budget from the fixture check's plain recording: S5 holds only where it bites (the fixture's
    # frame rate decides SnapProbe_PerFrame's), and no other stack choice may be cut by it or starved of the total.
    win = (out.get("fixture") or {}).get("window_ms", 0) / 1000.0
    rates = {n: (rows.get(n) or {}).get("count", 0) / win if win else 0.0 for n in STACK_CHOICES}
    total_sent = FIXTURE_STACK_TOTAL if args.stack_total is None else args.stack_total
    budget = per_frame_budget(rates, args.stack_per_ring, total_sent)
    out["stack_budget"] = budget
    per_sent = budget["per"]
    per, total = budget_echo(per_sent), budget_echo(total_sent)
    say(f"\ngame module {game_module} ({'pid %d' % pid if named else 'the fixture name: no usable out/host.pid'}); "
        f"CPU {out['machine']['cpu'] or '?'}; stack budgets {per}/s a function, {total}/s in all, depth {STACK_DEPTH}")
    say(f"     the per-function budget: {budget['why']}" + ("" if budget["runs"] else "; S5's window will not run"))
    snap_bytes = 32 << 20
    stacks = {"funcs": [stack_item(rows[n]) for n in STACK_CHOICES], "depth": STACK_DEPTH,
              "per_ring_per_s": per_sent, "total_per_s": total_sent}

    # ---- S0: the main Start. SnapProbe_Call's parameter budget is far above its four calls a second, so a nonzero
    # parameter counter at S5 can only be a stack refusal counted in the wrong place.
    say("\nS0 -- the main Start echoes the stack choices:")
    trace = {"bytes": 64 << 20, "ticked_names": [item(rows["SnapNest_Outer"])],
             "snapshots": {"bytes": snap_bytes, "per_ring_per_s": 1000, "funcs": [item(rows["SnapProbe_Call"])],
                           "stacks": stacks}}
    t0 = clock()
    start = c.request("pe_profile_start", trace=trace)
    t1 = clock()
    if not check("S0 the main Start is accepted", ok_of(start), str(start.get("error", ""))[:120]):
        return
    st = data_of(start).get("trace", {})
    out["stack_start"] = st
    state = stack_echo_state(st, len(STACK_CHOICES))
    if state == "old" or not isinstance(st.get("stack"), dict):
        check("S0 the reply carries names.stacks and trace.stack (a DLL with step 3)", False,
              f"names.stacks {'absent' if state == 'old' else st['names']['stacks']}, trace.stack "
              f"{'present' if isinstance(st.get('stack'), dict) else 'absent'}: S1-S6 cannot run")
        return
    names, sk, snap = st.get("names", {}), st["stack"], st.get("snap", {})
    k = k_with_stacks(snap_bytes, [ring_cap(rows["SnapProbe_Call"].get("parms_size", 0))], len(STACK_CHOICES),
                      STACK_DEPTH)
    check("S0 names: both stack choices, the tick and the parameter choice, nothing refused",
          names.get("stacks") == len(STACK_CHOICES) and names.get("ticks") == 1 and names.get("chosen") == 1 and
          not names.get("refused"), json.dumps(names)[:160])
    check(f"S0 trace.stack: {len(STACK_CHOICES)} rings, depth {STACK_DEPTH}, the budgets as sent ({per}/s, {total}/s)",
          sk.get("rings") == len(STACK_CHOICES) and sk.get("depth") == STACK_DEPTH and
          sk.get("per_ring_per_s") == per and sk.get("total_per_s") == total, json.dumps(sk)[:200])
    check("S0 one parameter ring, and K with the stack terms",
          snap.get("allocated") is True and snap.get("rings") == 1 and snap.get("slots_per_ring") == k,
          f"rings={snap.get('rings')} K={snap.get('slots_per_ring')} want {k}")
    sleep(args.record_s)
    t2 = clock()
    stop = data_of(c.request("pe_profile_stop"))
    span_lo, span_hi = t2 - t1, t2 - t0   # the trace starts inside the Start and stops when the Stop arrives
    out["stack_stop"] = stop
    st2 = stop.get("trace", {})
    gen = st2.get("gen", 0)
    by_func = {n.get("func"): n for n in stop.get("names", [])}
    check("S0 the Stop reply's names: stack true on both stack choices, false on the tick",
          all(by_func.get(n, {}).get("stack") is True for n in STACK_CHOICES) and
          by_func.get("SnapNest_Outer", {}).get("stack") is False,
          json.dumps({n: by_func.get(n, {}).get("stack") for n in need}))
    if not st2.get("allocated"):
        check("the trace kept calls", False, "empty, and released at Stop")
        return
    main_reply = c.request("pe_profile_get", limit=32768, include_unloaded=True)   # the main recording's own rates
    table = data_of(main_reply)
    main_pf, main_what = main_table_rate(main_reply)

    # ---- S1: the slots, joined to the entries by entry_seq.
    say("\nS1 -- the slots and their join:")
    recs = read_ring(c)
    fnames = trace_names(c, gen)
    addr_of = {it.get("func_name"): a for a, it in fnames.items() if it.get("class_name") == FIXTURE_CLASS}
    entries = {x[0]: x for x in recs if not x[0] & RET_BIT}
    ring_func = {s: addr_of.get(n) for s, n in enumerate(STACK_CHOICES)}
    reads = read_stack_rings(c, gen, len(STACK_CHOICES))
    slots_by_ring = {s: r["slots"] for s, r in reads.items()}
    all_slots = [sl for r in reads.values() for sl in r["slots"]]
    out["stack_rings"] = {s: dict(r["ring"], pages=r["pages"]) for s, r in reads.items()}
    say(f"     {len(entries)} entries; stack slots {[len(r['slots']) for r in reads.values()]} in "
        f"{[r['pages'] for r in reads.values()]} pages")
    fl_call = stack_flag_problems(entries, ring_func[0])
    fl_pf = stack_flag_problems(entries, ring_func[1], exact=F_LONE | F_STACK_TAKEN)
    check("S1 every SnapProbe_Call entry says what became of its stack (32 or 64), every SnapProbe_PerFrame one is 4|32",
          None not in ring_func.values() and not fl_call and not fl_pf,
          f"functions {[fmt(a, '#x') for a in ring_func.values()]}; wrong {fl_call[:3]} {fl_pf[:3]}")
    j = s1_join(entries, ring_func, slots_by_ring)
    check("S1 every entry flagged 32 has exactly one slot in its function's ring (by entry_seq), every slot one entry",
          # A slot only an entry flagged 32 may own, and every such entry owns one: some taken means some slots.
          j["taken"] > 0 and not j["no_slot"] and not j["two_slots"] and not j["stray"],
          f"{j['taken']} taken, {j['slots']} slots; without a slot {j['no_slot'][:3]}, with two {j['two_slots'][:3]}, "
          f"stray {j['stray'][:3]}")
    probs = [p for r in reads.values() for p in r["problems"]]
    refs = [b for r in reads.values() for b in r["bad_refs"]]
    orphans = sum(r["orphans"] for r in reads.values())
    check("S1 no orphans, every frame index names a site of its page, every page a stack page",
          orphans == 0 and not refs and not probs, f"orphans {orphans}, bad indices {refs[:3]}, {probs[:2]}")
    check("S1 no slot Partial / Fault / BadSp / LowStack / NoCapturer, every slot 3 frames or more",
          j["slots"] > 0 and not j["bad_flags"] and not j["short"],
          f"flagged {j['bad_flags'][:3]}, short {j['short'][:3]}")
    say(f"     {sum(1 for sl in all_slots if sl['flags'] & STK_MORE)} of {len(all_slots)} slots deeper than "
        f"{STACK_DEPTH} frames (More)")

    # ---- S2: frame 0, and every site's arithmetic.
    say("\nS2 -- frame 0 and the sites:")
    f0 = [(sl["index"], s2_frame0_problems(sl["frames"][0], game_module, exe)) for sl in all_slots if sl["frames"]]
    f0_bad = [x for x in f0 if x[1]]
    check(f"S2 frame 0 of every slot is in {game_module} (ignoring case)"
          f"{', inside its image' if exe else ''}; module_base + rva == addr; the CE text adds back",
          f0 != [] and not f0_bad,
          f"{len(f0)} slots; first wrong {f0_bad[:1]}" + ("" if exe else "; the image not checked: no pid"))
    if f0:
        s0 = next(sl["frames"][0] for sl in all_slots if sl["frames"])
        say(f"     e.g. {ce_text(s0) if s0['rva'] is not None else '?'} = {fmt(s0['addr'], '#x')}")
    uniq = {s["addr"]: s for sl in all_slots for s in sl["frames"]}
    sp = [(fmt(a, "#x"), site_problems(s)) for a, s in uniq.items()]
    sp_bad = [x for x in sp if x[1]]
    check("S2 every site adds up: module_base + rva == addr; fn below addr, module_base + fn_rva == fn; fn only with "
          "unwind", sp != [] and not sp_bad, f"{len(sp)} sites; first wrong {sp_bad[:1]}")
    say(f"     sites outside any module: {sum(1 for s in uniq.values() if not s['module'])}; without unwind data: "
        f"{sum(1 for s in uniq.values() if not s['unwind'])}")

    # ---- S3: nesting. In scope: SnapNest_Outer's ProcessEvent and our hook lie below SnapProbe_Call's frames.
    say("\nS3 -- nesting:")
    parent = parents(recs)
    outer = addr_of.get("SnapNest_Outer")
    in_scope: list[dict] = []
    lone: list[dict] = list(slots_by_ring.get(1, []))   # SnapProbe_PerFrame runs from the actor's Tick: always lone
    for sl in slots_by_ring.get(0, []):
        e = entries.get(sl["entry_seq"])
        if e is None:
            continue
        up = entries.get(parent.get(e[0]))
        if e[5] & F_LONE:
            lone.append(sl)
        elif outer is not None and up is not None and up[2] == outer:
            in_scope.append(sl)
    ns_in = [nesting(sl["frames"]) for sl in in_scope]
    ns_lone = [nesting(sl["frames"]) for sl in lone]
    # The in-scope SnapProbe_Call entries, a stack kept or not: one refused has no slot, only its flag 64.
    in_entries = [e for e in entries.values() if ring_func[0] is not None and e[2] == ring_func[0] and
                  not e[5] & F_LONE and outer is not None and (entries.get(parent.get(e[0])) or (0, 0, None))[2] == outer]
    n_refused = sum(1 for e in in_entries if e[5] & F_STACK_BUDGET)
    ring_pf = ring_rate(reads[1]["ring"], span_hi)
    starved = starved_in_scope(in_entries, len(in_scope), budget["rates"], main_rate(main_pf, ring_pf), per_sent,
                               total_sent)
    say(f"     in-scope SnapProbe_Call: {len(in_entries)} entries, {len(in_scope)} stacks kept, {n_refused} refused by "
        f"the stack budget (64)")
    unexplained = f"; {n_refused} of {len(in_entries)} in-scope entries refused by the stack budget (64), which the " \
                  f"total does not explain" if not in_scope and n_refused else ""
    if starved:
        check.not_run(S3_OWN_IN, starved)
    else:
        check(S3_OWN_IN, in_scope != [] and all(o is not None for _, o in ns_in),
              f"{sum(1 for _, o in ns_in if o is not None)} of {len(in_scope)}{unexplained}")
    check("S3 no lone stack holds an own frame", lone != [] and all(o is None for _, o in ns_lone),
          f"{sum(1 for _, o in ns_lone if o is not None)} of {len(lone)} do")
    # The known halves are checks since S3-M3: DescribeCode follows a chained fragment to its primary function, so a
    # ProcessEvent call site in a hot/cold or shrink-wrapped fragment is still labelled (the review's M4).
    with_known = sum(1 for n in ns_in if known_before_own(n))
    if starved:
        check.not_run(S3_KNOWN_IN, starved)
    else:
        check(S3_KNOWN_IN, in_scope != [] and with_known == len(in_scope),
              f"{with_known} of {len(in_scope)}{unexplained}")
    lone_known = sum(1 for kn, _ in ns_lone if kn is not None)
    check(S3_KNOWN_LONE, lone != [] and lone_known == 0, f"{lone_known} of {len(lone)} do")
    # The DLL labels a frame by comparing its function start with one address, so every label names one function;
    # and the fixture's paths cross at most one ProcessEvent below the hook, so no stack holds two.
    known_fns = {f["fn"] for sl in all_slots for f in sl["frames"] if f["known"] == KNOWN_PE}
    twice = [sl["index"] for sl in all_slots if sum(1 for f in sl["frames"] if f["known"] == KNOWN_PE) > 1]
    check(S3_KNOWN_ONE, len(known_fns) <= 1 and None not in known_fns and not twice,
          f"{len(known_fns)} function(s) {sorted(fmt(a, '#x') for a in known_fns)[:3]}; slots with two {twice[:3]}")
    miss = next(((sl, n) for sl, n in zip(in_scope, ns_in) if not known_before_own(n) and n[1]), None)
    if miss:
        f = miss[0]["frames"][miss[1][1] - 1]   # where ProcessEvent's frame should be: right before the hook's
        say(f"     slot {miss[0]['index']}: the frame before the hook is {fmt(f['addr'], '#x')} fn {fmt(f['fn'], '#x')}"
            f" unwind {f['unwind']} known {f['known'] or '-'} (a chained fragment's start would show here)")

    # ---- S4: SnapNest_Outer's native entry, recorded.
    say("\nS4 -- SnapNest_Outer's native entry on the in-scope stacks:")
    ca = fnames.get(outer, {}).get("code_addr") if outer else None
    code = int(ca, 16) if isinstance(ca, str) and ca else None
    if code is None:
        check.record("S4 SnapNest_Outer's code_addr", f"{ca!r} from pe_trace_names: nothing to match", as_expected=False)
    else:
        hits = [outer_frame(sl["frames"], code, n[0] if n[0] is not None else n[1]) for sl, n in zip(in_scope, ns_in)]
        n_hit = sum(1 for h in hits if h is not None)
        check.record("S4 in-scope stacks with a frame whose fn is SnapNest_Outer's code_addr, before ProcessEvent's",
                     f"{n_hit} of {len(in_scope)}; at frames {sorted({h for h in hits if h is not None})}" +
                     ("" if n_hit else "; none: the tail-call case (the design's 4.3), for \"Step 3 built\""))

    # ---- S5: the stack budget.
    say("\nS5 -- the stack budget:")
    pf_ring = reads[1]["ring"]
    per_s = min(per, total)
    lo, hi = budget_window(per_s, span_lo, span_hi)
    pf_row = fixture_rows(table).get("SnapProbe_PerFrame", {})
    out["stack_budget"].update(main_rate=main_pf, main_rate_ring=ring_pf)
    stack2, snap2 = st2.get("stack", {}), st2.get("snap", {})
    say(f"     SnapProbe_PerFrame: about {fmt(main_pf, '.0f')} calls/s; its stack ring wrote "
        f"{pf_ring.get('written')}, skipped {pf_ring.get('skipped_budget')}, dropped {pf_ring.get('dropped_budget')}")
    check(f"S5 {MAIN_TABLE}", main_pf is not None, main_what)
    # A reply that cannot give the rate fails just above, and the window then runs from the trace ring as it did
    # before the main rate was read: standing it down on a rate nobody measured would hide that reply's defect. One
    # that gives a plausible, wrong rate stands it down only where the probe's own stack ring agrees (main_rate).
    s5_why = budget["why"] if not budget["runs"] else None if main_pf is None else \
        main_rate_problem(main_pf, per_s, budget["rates"].get("SnapProbe_PerFrame", 0.0), ring_pf)
    if s5_why is None:
        check(f"{S5_WINDOW} {per_s}/s over {span_lo:.1f}-{span_hi:.1f} s "
              f"({lo:.0f}..{hi:.0f}) and the budget drops the rest",
              lo <= int_or(pf_ring.get("written"), -1) <= hi and int_or(pf_ring.get("dropped_budget"), 0) > 0 and
              int_or(stack2.get("dropped_budget"), 0) > 0,
              f"written {pf_ring.get('written')}, ring dropped {pf_ring.get('dropped_budget')}, "
              f"stack.dropped_budget {stack2.get('dropped_budget')}")
    else:
        # A budget that cannot bite may still drop calls, but not steadily enough to hold the ring to a window: a
        # failed window would blame the DLL for the run.
        check.not_run(f"{S5_WINDOW} the budget a second, and the budget drops the rest", s5_why)
    # The parameter counters, apart from the window: a stack budget that cannot hold SnapProbe_PerFrame to a window
    # still refuses calls, and a nonzero counter is always a defect here (S0's parameter budget is far above
    # SnapProbe_Call's calls). Whether anything was refused is measured beside the DLL's own counters, from the main
    # table: SnapProbe_PerFrame called more often than its ring wrote. Where nothing was, 0 proves nothing.
    sk_p, dr_p = snap2.get("skipped_budget"), snap2.get("dropped_budget")
    pf_written = int_or(pf_ring.get("written"), 0)
    refused = int_or(pf_row.get("count"), 0) > pf_written or \
        int_or(stack2.get("skipped_budget"), 0) + int_or(stack2.get("dropped_budget"), 0) > 0
    if refused or (sk_p, dr_p) != (0, 0):
        check(S5_PARAMS, (sk_p, dr_p) == (0, 0), f"skipped {sk_p}, dropped {dr_p}; the stack budget "
              f"{'refused calls' if refused else 'refused nothing'} (SnapProbe_PerFrame {pf_row.get('count')} calls, "
              f"{pf_written} written; stack skipped {stack2.get('skipped_budget')}, dropped "
              f"{stack2.get('dropped_budget')})")
    else:
        check.not_run(S5_PARAMS, f"the stack budget refused nothing (SnapProbe_PerFrame {pf_row.get('count')} calls, "
                                 f"{pf_written} written; stack skipped and dropped 0), so the counters at 0 prove "
                                 f"nothing")

    # ---- S6: the cost, recorded.
    say("\nS6 -- the cost (recorded):")
    cost = stack_cost(stack2, st2.get("qpc_freq", 0), span_lo)
    agree = slot_ticks_agree(all_slots, stack2)
    written = sum(int_or(r["ring"].get("written"), 0) for r in reads.values())
    rates = {"plain": calls_per_s(out.get("fixture", {})), "with_stacks": calls_per_s(table)}
    out["stack_cost"] = {"cost": cost, "slot_ticks": agree, "written": written, "calls_per_s": rates,
                         "span_s": [span_lo, span_hi]}
    mc = out["machine"]
    check.record("S6 microseconds a capture, mean / max", f"{fmt(cost['mean_us'])} / {fmt(cost['max_us'])} us over "
                 f"{cost['captures']} captures", as_expected=cost["mean_us"] is not None)
    check.record("S6 captures a second", fmt(cost["captures_per_s"], ".1f"))
    check.record("S6 calls/s without / with stacks", f"{rates['plain']:,.0f} / {rates['with_stacks']:,.0f}")
    check.record("S6 the machine", f"{mc['cpu'] or '?'}, {mc['logical_cpus']} logical CPUs, "
                 f"battery {mc['battery']}, on AC {mc['on_ac']}")
    check.record("S6 D3's re-weighed total: floor(2000 us / mean) down to a multiple of 50",
                 f"{cost['reweighed_total']} against {total} now")
    check.record("S6 the slots' own ticks against the totals (sum == spent_ticks, max <= max_ticks; captures == the "
                 "rings' written)", f"sum {agree['sum']} / spent {cost['spent_ticks']}, max {agree['max']} / "
                 f"{cost['max_ticks']}; captures {cost['captures']} / written {written}",
                 as_expected=agree["agree"] and cost["captures"] == written)

    say("\nS7 -- the release:")
    c.request("pe_trace_release")
    after = c.request("pe_trace_get", **{"from": 0, "max": 1})   # its own `data` is the records: not data_of
    check("S7 the release frees everything (the main trace)",
          after.get("allocated") is False and "snap" not in after and "stack" not in after)

    # ---- --pdb: the stacks are in hand and the DLL holds nothing, so dbghelp's load takes what time it takes.
    if args.pdb is not None:
        run_pdb(check, out, (symbols or open_game_pdb)(pid, exe, args.pdb or None), all_slots, in_scope, game_module,
                code)

    # ---- S0, second half: an altered stack key, alone and beside a good one, in stacks-only Starts.
    say("\nS0 -- an altered stack key alone, then a stacks-only Start:")
    bad = stack_item(rows["SnapProbe_PerFrame"])
    bad["class"] += "X"

    def stacks_only(funcs: list[dict]) -> dict:
        return {"bytes": 32 << 20, "snapshots": {"bytes": snap_bytes, "funcs": [], "stacks": dict(stacks, funcs=funcs)}}
    r = c.request("pe_profile_start", trace=stacks_only([bad]))
    c.request("pe_profile_stop")
    after = c.request("pe_trace_get", **{"from": 0, "max": 1})   # its own `data` is the records: not data_of
    check("S0 an altered stack key alone refuses the Start, and nothing stays allocated",
          not ok_of(r) and after.get("allocated") is False, str(r.get("error", ""))[:90])
    r = c.request("pe_profile_start", trace=stacks_only([stack_item(rows["SnapProbe_Call"]), bad]))
    if not check("S0 a stacks-only Start (no ticks, funcs []) is accepted", ok_of(r), str(r.get("error", ""))[:120]):
        return
    st3 = data_of(r).get("trace", {})
    snap3, sk3, names3 = st3.get("snap", {}), st3.get("stack", {}), st3.get("names", {})
    k1 = k_with_stacks(snap_bytes, [], 1, STACK_DEPTH)
    check("S0 ...snap_only, no parameter ring, one stack ring, K with the stack terms",
          st3.get("snap_only") is True and snap3.get("allocated") is True and snap3.get("rings") == 0 and
          sk3.get("rings") == 1 and snap3.get("slots_per_ring") == k1,
          f"snap_only={st3.get('snap_only')} rings={snap3.get('rings')}/{sk3.get('rings')} "
          f"K={snap3.get('slots_per_ring')} want {k1}")
    check("S0 ...its altered second key refused by name, the first kept",
          names3.get("stacks") == 1 and any(x.get("func") == "SnapProbe_PerFrame" for x in names3.get("refused", [])),
          json.dumps(names3)[:160])
    sleep(STACKS_ONLY_S)
    t3 = data_of(c.request("pe_profile_stop")).get("trace", {})
    ent3: list[tuple] = []
    wrong: list[int] = []
    if t3.get("allocated"):
        fn3 = trace_names(c, t3.get("gen", 0))
        call3 = next((a for a, it in fn3.items()
                      if it.get("class_name") == FIXTURE_CLASS and it.get("func_name") == "SnapProbe_Call"), None)
        ent3 = [x for x in read_ring(c) if not x[0] & RET_BIT]
        wrong = [x[0] for x in ent3 if x[2] != call3 or x[5] != F_LONE | F_STACK_TAKEN]
    check("S0 ...it records lone SnapProbe_Call calls and nothing else, each flagged 4|32 (Lone, StackTaken)",
          ent3 != [] and not wrong, f"{len(ent3)} entries; wrong {wrong[:3]}")
    c.request("pe_trace_release")
    after = c.request("pe_trace_get", **{"from": 0, "max": 1})   # its own `data` is the records: not data_of
    check("S7 the release frees everything (the stacks-only trace)",
          after.get("allocated") is False and "snap" not in after and "stack" not in after)
    check.not_run("S7 the default run and livefuncs_trace_live.py on the same DLL",
                  "separate invocations: cut 4, which the review's H1 took")


def run_game_stacks(c, check: Checks, out: dict, args, sleep=time.sleep, clock=time.perf_counter) -> None:
    """--stacks --choose on a real game (the design's 8.3): the busiest named functions whose class or name holds one
    of the substrings, chosen for stacks alone at the given budgets and depth. What it reports is their cost, and with
    --names the names on their stacks (run_names). `sleep` and `clock` as run_stacks takes them."""
    say(f"\nplain recording ({args.plain_s:.0f} s) to find the functions to choose:")
    table = plain_table(c, args.plain_s)
    pats = [p.lower() for p in args.choose]
    picked: dict[str, dict] = {}
    for f in sorted((f for f in table.get("functions", []) if isinstance(f.get("fname_key"), list)),
                    key=lambda f: -f.get("count", 0)):
        name = f"{f.get('class_name')}::{f.get('func_name')}"
        if any(p in name.lower() for p in pats):
            picked.setdefault(name, f)
    chosen = list(picked.values())[: args.max_choices]
    out["chosen"] = [f"{f['class_name']}::{f['func_name']}" for f in chosen]
    out["machine"] = mc = machine()
    if not check("functions to choose were found", chosen != [], ", ".join(out["chosen"][:12])):
        return
    stacks = {"funcs": [stack_item(f) for f in chosen],
              "depth": STACK_DEPTH if args.stack_depth is None else args.stack_depth}
    for key, given in (("per_ring_per_s", args.stack_per_ring), ("total_per_s", args.stack_total)):
        if given is not None:   # left out, the DLL applies its own default, which is what 8.3 measures
            stacks[key] = given
    say(f"\nstacks-only recording ({args.record_s:.0f} s), {len(chosen)} chosen, budgets "
        f"{json.dumps({k: v for k, v in stacks.items() if k != 'funcs'})} (the DLL's default for any left out):")
    t0 = clock()
    start = c.request("pe_profile_start", trace={"bytes": 64 << 20, "snapshots": {"bytes": 32 << 20, "funcs": [],
                                                                                  "stacks": stacks}})
    t1 = clock()
    if not check("the stacks-only Start is accepted", ok_of(start), str(start.get("error", ""))[:120]):
        return
    st = data_of(start).get("trace", {})
    state = stack_echo_state(st, len(chosen))
    if not check("the reply carries names.stacks and trace.stack (a DLL with step 3)",
                 state != "old" and isinstance(st.get("stack"), dict), state):
        return
    check("every stack choice is named", state == "ok", json.dumps(st.get("names"))[:160])
    per, total = int_or(st["stack"].get("per_ring_per_s"), 0), int_or(st["stack"].get("total_per_s"), 0)
    out["stack_budgets"] = {"per_ring_per_s": per, "total_per_s": total}
    say(f"     the DLL uses {per}/s a function, {total}/s in all")
    sleep(args.record_s)
    t2 = clock()
    stop = data_of(c.request("pe_profile_stop"))
    after = data_of(c.request("pe_profile_get", limit=1))
    st2 = stop.get("trace", {})
    stack2 = st2.get("stack", {})
    cost = stack_cost(stack2, st2.get("qpc_freq", 0), t2 - t1)
    rates = {"plain": calls_per_s(table), "with_stacks": calls_per_s(after)}
    _, hi = budget_window(total, t2 - t1, t2 - t0)
    check(f"the total budget held: at most about {total}/s captured", cost["captures"] <= hi,
          f"{cost['captures']} captures, at most {hi:.0f}")
    census: dict[str, int] = {}
    slots: list[dict] = []
    if st2.get("allocated"):
        for r in read_stack_rings(c, st2.get("gen", 0), len(chosen)).values():
            slots.extend(r["slots"])
            for sl in r["slots"]:
                for nm, bit in (("partial", STK_PARTIAL), ("fault", STK_FAULT), ("more", STK_MORE),
                                ("bad_sp", STK_BADSP), ("low_stack", STK_LOWSTACK), ("no_capturer", STK_NOCAPTURER)):
                    if sl["flags"] & bit:
                        census[nm] = census.get(nm, 0) + 1
                census["slots"] = census.get("slots", 0) + 1
    out["stack_cost"] = {"cost": cost, "calls_per_s": rates, "census": census, "stack": stack2}
    check.record("microseconds a capture, mean / max", f"{fmt(cost['mean_us'])} / {fmt(cost['max_us'])} us over "
                 f"{cost['captures']} captures")
    check.record("captures a second; skipped / dropped by the budget",
                 f"{fmt(cost['captures_per_s'], '.1f')}; {stack2.get('skipped_budget')} / {stack2.get('dropped_budget')}")
    check.record("calls/s without / with stacks", f"{rates['plain']:,.0f} / {rates['with_stacks']:,.0f}")
    check.record("slot flags", json.dumps(census))
    check.record("the machine", f"{mc['cpu'] or '?'}, battery {mc['battery']}, on AC {mc['on_ac']}")
    check.record("D3's re-weighed total", f"{cost['reweighed_total']} against {total} now")
    c.request("pe_trace_release")
    # The stacks are in hand and the DLL holds no trace; the names' UFunctions are asked about as objects.
    if args.names:
        run_names(c, check, out, slots)


def named_entries(slots: list[dict]) -> tuple[list[dict], int]:
    """S3-A1's named frames of these stacks as entries, one per (ufunc, fn), the most frequent first (ties in the order
    first seen), and how many frames were named. A ufunc reached at two starts is two entries: each start is a claim
    of its own about that UFunction's Func."""
    by: dict[tuple, dict] = {}
    named = 0
    for sl in slots:
        for f in sl["frames"]:
            if f["ufunc"] is None:
                continue
            named += 1
            e = by.setdefault((f["ufunc"], f["fn"]), {"ufunc": f["ufunc"], "fn": f["fn"], "class": f["class"],
                                                      "func": f["func"], "shared": f["shared"], "frames": 0})
            e["frames"] += 1
    return sorted(by.values(), key=lambda e: -e["frames"]), named


def pe_gap(frames: list[dict], i: int) -> int | None:
    """How many frames lie between frame i and the next known:"process_event" frame toward the root, or None when no
    such frame follows it."""
    j = next((k for k in range(i + 1, len(frames)) if frames[k]["known"] == KNOWN_PE), None)
    return None if j is None else j - i - 1


def fn_offsets(blob: bytes, fn: int) -> list[int]:
    """Every 8-aligned offset of `blob` holding `fn` as a little-endian u64: a pointer member of a UFunction sits at a
    multiple of 8, as x64 stores it."""
    return [o for o in range(0, len(blob) - 7, 8) if struct.unpack_from("<Q", blob, o)[0] == fn]


def common_offsets(per_entry: list[list[int]], reach: list[int] | None = None) -> list[int]:
    """Every offset every entry holds its fn at, the lowest first; none when there is no entry, or one without its fn.
    `reach` is how many bytes of each entry were read (all of NAMES_READ when None): an offset whose 8 bytes lie past
    an entry's read is not contradicted by that entry, which says nothing there, but must be held by some entry."""
    reach = [NAMES_READ] * len(per_entry) if reach is None else reach
    return [o for o in sorted({o for offs in per_entry for o in offs})
            if all(o in offs or o + 8 > n for offs, n in zip(per_entry, reach))]


def common_offset(per_entry: list[list[int]], reach: list[int] | None = None) -> int | None:
    """The lowest of common_offsets, or None."""
    return next(iter(common_offsets(per_entry, reach)), None)


def read_ufunc(c, addr: str) -> bytes | None:
    """A named UFunction's bytes for --names: the whole window when read_mem gives it, else the most one of the smaller
    reads gives; None when no read gives 8 bytes, the least that can hold a slot."""
    for size in (NAMES_READ,) + NAMES_READ_RETRY:
        m = c.request("read_mem", addr=addr, size=size)
        if ok_of(m):
            try:
                blob = bytes.fromhex(str(data_of(m).get("bytes") or ""))
            except ValueError:
                return None
            return blob if len(blob) >= 8 else None
    return None


def read_slot(c, addr: int) -> int | None:
    """The 8 bytes at `addr` as a little-endian u64 (one UFunction slot, read alone), or None when read_mem cannot give
    them."""
    m = c.request("read_mem", addr=f"{addr:X}", size=8)
    if not ok_of(m):
        return None
    try:
        blob = bytes.fromhex(str(data_of(m).get("bytes") or ""))
    except ValueError:
        return None
    return struct.unpack_from("<Q", blob)[0] if len(blob) >= 8 else None


def run_names(c, check: Checks, out: dict, slots: list[dict]) -> None:
    """--names: S3-A1's names on the --choose run's stacks, the ledger's DQ XI S probes made repeatable. Each named
    entry, the most frequent first and NAMES_MAX at most, is asked of the DLL as an object (get_object) and as
    memory (read_mem); every named frame counts toward what is recorded."""
    say("\nA1 -- the stacks' names:")
    entries, named = named_entries(slots)
    asked = entries[:NAMES_MAX]
    left = len(entries) - len(asked)
    shared = sorted((e for e in entries if e["shared"] is not None), key=lambda e: -e["shared"])
    gaps: dict[int | None, int] = {}
    for sl in slots:
        for i, f in enumerate(sl["frames"]):
            if f["ufunc"] is not None:
                g = pe_gap(sl["frames"], i)
                gaps[g] = gaps.get(g, 0) + 1
    gap_text = {("none" if g is None else str(g)): n
                for g, n in sorted(gaps.items(), key=lambda kv: (kv[0] is None, kv[0] or 0))}
    out["names"] = {"frames": named, "entries": len(entries), "asked": len(asked), "unchecked": left,
                    "shared_entries": len(shared), "shared_max": shared[0]["shared"] if shared else None,
                    "pe_gap": gap_text, "func_offset": None, "wrong": [], "held": 0, "short": 0, "gone": 0,
                    "unreadable": 0, "per_entry": []}
    say(f"     {named} frames named, {len(entries)} entries; {len(asked)} asked" +
        (f", {left} less frequent left unchecked" if left else ""))
    if not entries:
        why = "no frame was named" + ("" if slots else ": no stack was kept")
        check.not_run(NAMES_IS, why)
        check.not_run(NAMES_AT, why)
    else:
        wrong: list[str] = []
        reads: list[tuple[int, list[int]] | None] = []   # per entry: the bytes read and the offsets holding fn
        for e in asked:
            addr = f"{e['ufunc']:X}"
            o = c.request("get_object", addr=addr)
            d = data_of(o)
            if not ok_of(o):
                wrong.append(f"{e['class']}::{e['func']} at {addr}: get_object failed: {o.get('error')!r}")
            elif not e["func"] or not e["class"]:
                # A name read that gave nothing gives "" on the frame and "" from get_object alike: no name to check.
                wrong.append(f"{e['class']!r}::{e['func']!r} at {addr}: named empty")
            elif not (d.get("class") in NAME_FUNC_CLASSES and d.get("name") == e["func"] and
                      d.get("outer") == e["class"]):
                wrong.append(f"{e['class']}::{e['func']} at {addr} is {d.get('class')!r} "
                             f"{d.get('outer')!r}::{d.get('name')!r}")
            blob = read_ufunc(c, addr)
            r = None if blob is None else (len(blob), fn_offsets(blob, e["fn"]) if e["fn"] is not None else [])
            reads.append(r)
            out["names"]["per_entry"].append({"ufunc": addr, "class": e["class"], "func": e["func"],
                                              "frames": e["frames"], "shared": e["shared"], "fn": fmt(e["fn"], "#x"),
                                              "read": None if r is None else r[0],
                                              "offsets": [] if r is None else [hex(x) for x in r[1]]})
        # An entry no read reached says nothing about the offset: it is kept out of it. So is a named frame without fn,
        # which has nothing to look for; the DLL names a frame by its fn, so that one is wrong.
        got = [(e, r) for e, r in zip(asked, reads) if r is not None]
        looked = [r for e, r in got if e["fn"] is not None]
        cands = common_offsets([r[1] for r in looked], [r[0] for r in looked])

        def unreached(o: int) -> list[int]:
            """The asked entries with fn whose slot at `o` no read reached: short of it, or not read at all."""
            return [k for k, (e, r) in enumerate(zip(asked, reads))
                    if e["fn"] is not None and (r is None or o + 8 > r[0])]
        # The DLL named each frame by reading its UFunction's slot, so the slot was readable then: an unreached one is
        # read alone (8 bytes). The offset is the first candidate none of those slots contradicts -- a decoy copy of
        # fn in the entries read whole is told apart there, as it is by an entry read past it -- else the lowest, and
        # the slots that contradict it fail.
        slot_at: dict[tuple[int, int], int | None] = {}
        off = None
        for o in cands:
            for k in unreached(o):
                slot_at[(k, o)] = read_slot(c, asked[k]["ufunc"] + o)
            if all(slot_at[(k, o)] in (None, asked[k]["fn"]) for k in unreached(o)):
                off = o
                break
        else:
            off = cands[0] if cands else None
        held, short, absent, gone, unread = [], [], [], [], []
        for k, (e, r, pe) in enumerate(zip(asked, reads, out["names"]["per_entry"])):
            label = f"{e['class']}::{e['func']}"
            n, offs = r if r is not None else (0, [])
            if r is not None and e["fn"] is None:
                absent.append(f"{label} (no fn)")
                pe["verdict"] = "no fn"
            elif off is not None and off in offs:
                held.append(label)
                pe["verdict"] = "held"
            elif off is not None and e["fn"] is not None:
                # common_offsets leaves every entry read past the offset holding fn there, so this one stopped short
                # of the slot or was not read at all, and its slot was read alone: what it holds now is the verdict.
                assert (k, off) in slot_at, "common_offsets left an entry read past the offset without fn there"
                slot = slot_at[(k, off)]
                pe["slot"] = fmt(slot, "#x")
                if slot is None:
                    gone.append(label)
                    pe["verdict"] = "gone"
                elif slot == e["fn"]:
                    held.append(label)
                    pe["verdict"] = "held"
                else:
                    absent.append(f"{label} (+0x{off:X} holds {slot:#x})")
                    pe["verdict"] = "absent"
            elif r is None:
                unread.append(label)
                pe["verdict"] = "unreadable"
            elif not offs and n < NAMES_READ:
                short.append(f"{label} (read to +0x{n:X})")   # with no offset known, the slot may lie past the read
                pe["verdict"] = "short"
            else:
                absent.append(label)
                pe["verdict"] = "absent"
        out["names"].update(func_offset=off, wrong=wrong, held=len(held), short=len(short), gone=len(gone),
                            unreadable=len(unread))
        tail = f" ({left} less frequent left unchecked)" if left else ""
        check(NAMES_IS, not wrong, f"{len(asked) - len(wrong)} of {len(asked)} asked{tail}; first wrong {wrong[:2]}")
        # This shows the DLL reads one slot for every name, not on its own that the slot is UFunction::Func: the DLL
        # found each name by reading that very slot. The independent evidence is the frame order recorded below (a
        # native entry one frame, UFunction::Invoke, below ProcessEvent) and, where the game ships one, a PDB.
        apart = (f"; {len(gone)} gone since its frame was named (the slot unreadable now) {gone[:3]}"
                 if gone else "") + \
            (f"; {len(short)} read short of it {short[:3]}" if short else "") + \
            (f"; {len(unread)} unreadable {unread[:3]}" if unread else "")
        if not got:
            check.not_run(NAMES_AT, f"no named UFunction could be read: read_mem failed at every size down to "
                                    f"0x{NAMES_READ_RETRY[-1]:X} for all {len(asked)}{tail}")
        elif off is None and not absent:
            check.not_run(NAMES_AT, f"no read reached a slot holding fn{apart}{tail}")
        else:
            # N is the entries judged, held or not; an entry gone since it was named is no verdict on the DLL, so it is
            # listed beside the count, with the number asked, and never read as a failure.
            check(NAMES_AT, off is not None and not absent,
                  (f"Func at +0x{off:X} in {len(held)} of {len(held) + len(absent)} read ({len(asked)} asked)"
                   if off is not None else
                   f"no offset common to the {len(got)} read; without their fn {absent[:3]}; offsets "
                   f"{[[hex(x) for x in r[1]] for _, r in got[:3]]}") +
                  (f"; without it {absent[:3]}" if off is not None and absent else "") + apart + tail)
    examples = ", ".join(f"{e['class']}::{e['func']} (shared by {e['shared']}, {e['frames']} frames)"
                         for e in shared[:5])
    check.record("A1 frames named, distinct entries, shared entries",
                 f"{named} frames, {len(entries)} entries; {len(shared)} shared" +
                 (f", the largest by {shared[0]['shared']}: {examples}" if shared else ""))
    # A record, not a check: a thunk reached from the interpreter has no ProcessEvent beyond it, legitimately.
    check.record('A1 frames between a named frame and the next known:"process_event" toward the root',
                 json.dumps(gap_text))


# ======================================================================================================================
# [LIVEFUNCS-STEP3] --pdb (the ledger's S3-R2): the stacks' function starts named by the fixture's shipped PDB.
# ======================================================================================================================

# The DLL takes a site's `fn` from .pdata and follows a chained fragment to its primary function (S3-M3), so a PDB that
# matches the exe names every such start at displacement 0: a displacement says the start is inside a function the PDB
# knows, which is what a fragment's own start looks like.

PDB_DISP = "PDB every in-module site with unwind data names a symbol at displacement 0"
PDB_PE = 'PDB every known:"process_event" site names ProcessEvent'
PDB_OUTER = "PDB SnapNest_Outer's native entry (S4's frame) names SnapNest_Outer"
PDB_NOT_RUN = "PDB the stacks' function starts named by the game's PDB"
SYMOPT_UNDNAME, SYMOPT_FAIL_CRITICAL_ERRORS, SYMOPT_EXACT_SYMBOLS, SYMOPT_NO_PROMPTS = 0x2, 0x200, 0x400, 0x80000
# A PDB loaded: SymPdb, or SymDia, which a DIA-backed dbghelp may report for the same file. Anything else (SymNone,
# SymExport) names no function the DLL's starts can be checked against.
SYM_TYPE_PDB = (3, 7)
MAX_SYM_NAME = 2000       # dbghelp.h's


class SYMBOL_INFOW(ctypes.Structure):
    """dbghelp.h's SYMBOL_INFOW. Name runs on past the struct, MaxNameLen characters in all."""
    _fields_ = [("SizeOfStruct", w.ULONG), ("TypeIndex", w.ULONG), ("Reserved", ctypes.c_uint64 * 2),
                ("Index", w.ULONG), ("Size", w.ULONG), ("ModBase", ctypes.c_uint64), ("Flags", w.ULONG),
                ("Value", ctypes.c_uint64), ("Address", ctypes.c_uint64), ("Register", w.ULONG), ("Scope", w.ULONG),
                ("Tag", w.ULONG), ("NameLen", w.ULONG), ("MaxNameLen", w.ULONG), ("Name", ctypes.c_wchar * 1)]


class GUID(ctypes.Structure):
    _fields_ = [("Data1", w.DWORD), ("Data2", w.WORD), ("Data3", w.WORD), ("Data4", ctypes.c_ubyte * 8)]


class IMAGEHLP_MODULEW64(ctypes.Structure):
    """dbghelp.h's IMAGEHLP_MODULEW64, whole: SymType says whether a PDB was loaded, LoadedPdbName which file."""
    _fields_ = [("SizeOfStruct", w.DWORD), ("BaseOfImage", ctypes.c_uint64), ("ImageSize", w.DWORD),
                ("TimeDateStamp", w.DWORD), ("CheckSum", w.DWORD), ("NumSyms", w.DWORD), ("SymType", w.DWORD),
                ("ModuleName", ctypes.c_wchar * 32), ("ImageName", ctypes.c_wchar * 256),
                ("LoadedImageName", ctypes.c_wchar * 256), ("LoadedPdbName", ctypes.c_wchar * 256),
                ("CVSig", w.DWORD), ("CVData", ctypes.c_wchar * (260 * 3)), ("PdbSig", w.DWORD),
                ("PdbSig70", GUID), ("PdbAge", w.DWORD), ("PdbUnmatched", w.BOOL), ("DbgUnmatched", w.BOOL),
                ("LineNumbers", w.BOOL), ("GlobalSymbols", w.BOOL), ("TypeInfo", w.BOOL), ("SourceIndexed", w.BOOL),
                ("Publics", w.BOOL), ("MachineType", w.DWORD), ("Reserved", w.DWORD)]


def pdb_option_problem(args) -> str | None:
    """Why --pdb cannot go with the other options, or None. Its checks are written for the fixture run's stacks (the
    fixture's call paths, SnapNest_Outer's native entry), which a --choose run on a game does not have."""
    if args.pdb is None:
        return None
    if not args.stacks or args.choose is not None or args.fixture_check:
        return "--pdb names the stacks of the --stacks fixture run: not with --choose or --fixture-check"
    return None


def game_option_problem(args) -> str | None:
    """Why --names or --stack-depth cannot go with the other options, or None. Both belong to the --stacks --choose run:
    --names reads that run's stacks-only recording, and the fixture run's checks are written for its stacks at
    STACK_DEPTH. A depth outside Linie's range would be clamped by the DLL, so the run would not get what it asked."""
    if (args.names or args.stack_depth is not None) and not (args.stacks and args.choose is not None):
        return "--names and --stack-depth belong to the --stacks --choose run"
    if args.stack_depth is not None and not 1 <= args.stack_depth <= STACK_MAX_DEPTH:
        return f"--stack-depth takes 1..{STACK_MAX_DEPTH}, the depths Linie keeps"
    return None


def image_path(pid: int) -> str | None:
    """The exe a process runs, as Windows names it: the file dbghelp reads, beside which the fixture ships its PDB."""
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.OpenProcess.restype = w.HANDLE
    k32.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, ctypes.POINTER(w.DWORD)]
    k32.CloseHandle.argtypes = [w.HANDLE]
    h = k32.OpenProcess(0x1000, False, pid)   # PROCESS_QUERY_LIMITED_INFORMATION
    if not h:
        return None
    try:
        buf = ctypes.create_unicode_buffer(32768)
        n = w.DWORD(len(buf))
        return buf.value if k32.QueryFullProcessImageNameW(h, 0, buf, ctypes.byref(n)) else None
    finally:
        k32.CloseHandle(h)


def load_dbghelp():
    dbg = ctypes.WinDLL("dbghelp", use_last_error=True)
    dbg.SymSetOptions.argtypes, dbg.SymSetOptions.restype = [w.DWORD], w.DWORD
    dbg.SymInitializeW.argtypes, dbg.SymInitializeW.restype = [w.HANDLE, w.LPCWSTR, w.BOOL], w.BOOL
    dbg.SymLoadModuleExW.argtypes = [w.HANDLE, w.HANDLE, w.LPCWSTR, w.LPCWSTR, ctypes.c_uint64, w.DWORD,
                                     ctypes.c_void_p, w.DWORD]
    dbg.SymLoadModuleExW.restype = ctypes.c_uint64
    dbg.SymGetModuleInfoW64.argtypes = [w.HANDLE, ctypes.c_uint64, ctypes.POINTER(IMAGEHLP_MODULEW64)]
    dbg.SymGetModuleInfoW64.restype = w.BOOL
    dbg.SymFromAddrW.argtypes = [w.HANDLE, ctypes.c_uint64, ctypes.POINTER(ctypes.c_uint64),
                                 ctypes.POINTER(SYMBOL_INFOW)]
    dbg.SymFromAddrW.restype = w.BOOL
    dbg.SymCleanup.argtypes, dbg.SymCleanup.restype = [w.HANDLE], w.BOOL
    return dbg


class DbghelpPdb:
    """One exe and its PDB in a private dbghelp session. The session's handle is the address of a value this object
    owns, never a process handle: dbghelp reads the exe and the PDB from disk and never touches the game."""
    def __init__(self, dbg, token, base: int, size: int, info: IMAGEHLP_MODULEW64) -> None:
        self._dbg, self._token = dbg, token
        self.base, self.size = base, size
        self.pdb = info.LoadedPdbName
        self.sym_type = info.SymType

    def sym(self, rva: int) -> tuple[str, int] | None:
        """The symbol holding base + rva and the address's displacement into it, or None when the PDB names none."""
        buf = (ctypes.c_byte * (ctypes.sizeof(SYMBOL_INFOW) + 2 * MAX_SYM_NAME))()
        info = SYMBOL_INFOW.from_buffer(buf)
        info.SizeOfStruct = ctypes.sizeof(SYMBOL_INFOW)   # the struct's own size: the name's room is MaxNameLen
        info.MaxNameLen = MAX_SYM_NAME
        disp = ctypes.c_uint64()
        if not self._dbg.SymFromAddrW(ctypes.addressof(self._token), self.base + rva, ctypes.byref(disp),
                                      ctypes.byref(info)):
            return None
        name = ctypes.wstring_at(ctypes.addressof(info) + SYMBOL_INFOW.Name.offset, min(info.NameLen, MAX_SYM_NAME))
        return name, disp.value

    def close(self) -> None:
        if self._token is not None:
            self._dbg.SymCleanup(ctypes.addressof(self._token))
            self._token = None


def open_image_pdb(path: str, base: int, size: int, search: str):
    """`path` loaded at `base` in a private dbghelp session with the PDB that matches it, looked for in `search`: a
    DbghelpPdb, or why there is none, a string that starts "no PDB" when dbghelp found no matching one. Exact symbols
    and no export fallback, so a PDB of another build, or the exe's exports, never answers for it."""
    try:
        dbg = load_dbghelp()
    except (OSError, AttributeError) as e:
        return f"dbghelp.dll did not load ({e})"
    dbg.SymSetOptions(SYMOPT_UNDNAME | SYMOPT_FAIL_CRITICAL_ERRORS | SYMOPT_EXACT_SYMBOLS | SYMOPT_NO_PROMPTS)
    token = ctypes.c_int()
    h = ctypes.addressof(token)
    if not dbg.SymInitializeW(h, search, False):
        return f"SymInitializeW failed (error {ctypes.get_last_error()})"
    session = None
    try:
        got = dbg.SymLoadModuleExW(h, None, path, None, base, size, None, 0)
        if not got:   # the exe itself: without a PDB the load still succeeds, and SymType says so below
            return f"dbghelp would not load {path} (error {ctypes.get_last_error()})"
        info = IMAGEHLP_MODULEW64()
        info.SizeOfStruct = ctypes.sizeof(info)
        if not dbg.SymGetModuleInfoW64(h, got, ctypes.byref(info)):
            return f"SymGetModuleInfoW64 failed (error {ctypes.get_last_error()})"
        if info.SymType not in SYM_TYPE_PDB:
            return f"no PDB matching {pathlib.Path(path).name} in {search} (dbghelp's SymType {info.SymType})"
        session = DbghelpPdb(dbg, token, got, size, info)
        return session
    finally:
        if session is None:
            dbg.SymCleanup(h)


def open_game_pdb(pid: int, exe: tuple[int, int] | None, search: str | None):
    """--pdb's session over the running game's exe: its path from the process, its base and size from psapi (`exe`),
    the PDB looked for in `search`, else beside the exe. A DbghelpPdb, or why there is none."""
    if not pid or not exe:
        return "no game process to take the exe from (out/host.pid)"
    path = image_path(pid)
    if not path:
        return f"pid {pid} would not give its exe's path"
    return open_image_pdb(path, exe[0], exe[1] - exe[0], search or str(pathlib.Path(path).parent))


def pdb_targets(slots: list[dict], game_module: str) -> dict[int, dict]:
    """The function starts the PDB is asked to name, each once, by fn_rva: those of every site in the game's exe (by
    name, ignoring case) with unwind data. The session loads the exe alone, so another module's sites are not asked."""
    want = game_module.lower()
    out: dict[int, dict] = {}
    for sl in slots:
        for s in sl["frames"]:
            if s["unwind"] and s["fn_rva"] is not None and s["module"].lower() == want:
                out.setdefault(s["fn_rva"], s)
    return out


def pdb_misses(answers: dict, rvas, needle: str = "") -> list[str]:
    """The starts in `rvas` the PDB does not name at displacement 0 with a name holding `needle`, as text."""
    bad: list[str] = []
    for rva in rvas:
        a = answers.get(rva)
        if a is None:
            bad.append(f"+{rva:X}: no symbol")
        elif a[1] != 0 or needle not in a[0]:
            bad.append(f"+{rva:X}: {a[0]} +0x{a[1]:X}")
    return bad


def readable_stack(frames: list[dict], answers: dict, game_module: str) -> list[str]:
    """A stack as a reader names it, one line a frame: the PDB's function and the return address's offset into it for
    a frame of the exe, else CE's `"module"+RVA`, else the bare address; the hook and a known frame marked."""
    want = game_module.lower()
    lines: list[str] = []
    for i, f in enumerate(frames):
        a = answers.get(f["fn_rva"]) if f["unwind"] and f["module"].lower() == want else None
        if a and f["fn"] is not None and f["addr"] is not None:
            text = f"{a[0]} +0x{f['addr'] - f['fn'] + a[1]:X}"
        elif f["module"] and f["rva"] is not None:
            text = ce_text(f)
        else:
            text = fmt(f["addr"], "#x")
        lines.append(f"#{i} {text}" + (" [the dumper's hook]" if f["own"] else "") +
                     (f" [{f['known']}]" if f["known"] else ""))
    return lines


def run_pdb(check: Checks, out: dict, opened, slots: list[dict], in_scope: list[dict], game_module: str,
            code: int | None) -> None:
    """--pdb's checks over the stacks already read. `opened` is what open_game_pdb returned: a session, closed here, or
    why there is none, reported not run. `code` is SnapNest_Outer's native entry from pe_trace_names (S4's)."""
    say("\nPDB -- the stacks' function starts named by the game's PDB:")
    if isinstance(opened, str):
        check.not_run(PDB_NOT_RUN, opened)
        out["pdb"] = {"run": False, "why": opened}
        return
    try:
        targets = pdb_targets(slots, game_module)
        answers = {rva: opened.sym(rva) for rva in sorted(targets)}
        code_rva = code - opened.base if code is not None and opened.base <= code < opened.base + opened.size else None
        if code_rva is not None and code_rva not in answers:
            answers[code_rva] = opened.sym(code_rva)
    finally:
        opened.close()
    say(f"     {opened.pdb}: {len(targets)} function starts asked")
    miss = pdb_misses(answers, sorted(targets))
    check(PDB_DISP, targets != {} and not miss,
          f"{len(targets) - len(miss)} of {len(targets)} named at their start; first wrong {miss[:3]}")
    known = sorted(rva for rva, s in targets.items() if s["known"] == KNOWN_PE)
    pe_miss = pdb_misses(answers, known, "ProcessEvent")
    check(PDB_PE, known != [] and not pe_miss,
          f"{[answers[r][0] if answers.get(r) else None for r in known][:3]}; wrong {pe_miss[:2]}")
    outer = [code_rva] if code_rva is not None else []
    s4 = sum(1 for sl in in_scope if code is not None and any(f["fn"] == code for f in sl["frames"]))
    check(PDB_OUTER, outer != [] and not pdb_misses(answers, outer, "SnapNest_Outer"),
          f"{answers.get(code_rva)} at {fmt(code, '#x')}; S4's frame on {s4} of {len(in_scope)} in-scope stacks"
          if outer else f"code_addr {fmt(code, '#x')} is not in the exe: nothing to name")
    first = readable_stack(in_scope[0]["frames"], answers, game_module) if in_scope else []
    for line in first:
        say(f"     {line}")
    check.record("PDB the first in-scope stack, named", " | ".join(first) if first else "no in-scope stack",
                 as_expected=first != [])
    out["pdb"] = {"run": True, "pdb": opened.pdb, "asked": len(targets),
                  "names": {f"{rva:#x}": answers.get(rva) for rva in sorted(answers)}, "first_in_scope": first}


DRY_RECORD_S = 2.0       # the dry run's --record-s: S5's window at a budget of B a second is then B..3B, above zero


class FakeClock:
    """The dry run's time. It moves only when the run sleeps, so a recording spans exactly what it slept and S5's
    window is tested at its real width; on the real clock a sleep that returns at once makes the span about zero."""
    def __init__(self) -> None:
        self.t = 1000.0

    def now(self) -> float:
        return self.t

    def sleep(self, s: float) -> None:
        self.t += s


class ScriptedDll:
    """A DLL with step 3 answering as the design's section 3 says it does, so --self-test can drive run_stacks and
    run_game_stacks end to end with no pipe. Its calls are a fixed script shaped like DumperTest58's: four rounds of
    SnapNest_Outer -> SnapProbe_Call in scope beside a lone SnapProbe_Call, and SnapProbe_PerFrame over its budget.
    Its stacks carry S3-A1's names, and get_object / read_mem answer for the UFunctions they name. Each fault named in
    FAULTS makes it answer as one broken DLL would, so a control can show the check that must catch that DLL failing.
    Step 2 (run_full) it scripts only as far as the parameter budget on SnapProbe_PerFrame: no snapshot's values, no
    layout, no refusal by K, so that run's other checks fail against it and only its budget line is read.
    It proves the rig's glue, never the DLL."""
    QPC, BASE, OWN = 10_000_000, 0x7FF6A0000000, 0x7FFC12300000
    FUNCS = {"SnapNest_Outer": (0x1000, [1, 0, 9, 0], 4), "SnapProbe_Call": (0x1100, [2, 0, 9, 0], 96),
             "SnapProbe_PerFrame": (0x1200, [3, 0, 9, 0], 4), "SnapProbe_RetOnly": (0x1300, [4, 0, 9, 0], 8),
             "SnapProbe_ConstRefOnly": (0x1400, [5, 0, 9, 0], 16)}
    # The step-2 run's late choice, kept apart: a --choose run chooses every keyed function, and its counts are FUNCS'.
    LATE = {"SnapLate_Call": (0x1500, [6, 0, 9, 0], 4)}
    OUTER_FN = 0x6000            # SnapNest_Outer's native entry, as an RVA
    INTERP_FN = 0xA000           # the interpreter's start, as an RVA: a script function's native entry
    # UFunction::Func in the scripted UFunctions: past 0x100, where the DLL's window (up to +0x158) still finds it, so
    # a run that searched less of the window would miss it.
    FUNC_AT = 0x148
    # names_func_low's: where UE5 keeps Func in a UFunction of about 0xE0 bytes, so a read retried at 0xE0 still
    # reaches it -- the path a read at the end of a block takes on a real game.
    FUNC_LOW = 0xD8
    INTERP = 0x2A000             # the script function whose names the interpreter's frames carry
    NAMES_MANY = 70              # names_many's extra entries: more than the run asks about
    # The UFunctions a stack's names point at, as get_object and read_mem answer them: ufunc -> (class, func, the
    # object's class, fn as an RVA, the offsets fn is stored at, shared). SnapNest_Outer's native entry is its own; the
    # interpreter's is shared by every script function, and named after the lowest-addressed function entering it, as
    # the DLL names it -- here a delegate's signature, a DelegateFunction, which the DLL indexes beside Function and
    # SparseDelegateFunction. Each holds a decoy copy of its fn at an offset the other lacks, so only the offset common
    # to both is Func.
    UFUNCS = {FUNCS["SnapNest_Outer"][0]: (FIXTURE_CLASS, "SnapNest_Outer", "Function", OUTER_FN, (0x30, FUNC_AT), None),
              INTERP: ("BP_ScriptedActor_C", "OnScripted__DelegateSignature", "DelegateFunction", INTERP_FN,
                       (FUNC_AT, 0x140), 37)}
    PAGE = 2                     # slots a page: small, so the rig's paging runs over several pages
    # The plain recording's window, as pe_profile_get reports it. A traced recording reports its own, the dry run's
    # --record-s: apart, a rig that divides one recording's count by another's window is seen.
    WINDOW_S = 3.0
    # SnapProbe_PerFrame's calls a second by default, in the plain recording and in the main one (each can be set):
    # a fixture far above 30 fps. The main recording keeps what a per-second budget keeps of them (see _start).
    PF_RATE = 60.0
    FAULTS = {
        "old": "a DLL without step 3: no names.stacks, no trace.stack, no names[].stack",
        "no_known": "no site labelled known (S3-F2's mutation: known compared with the trampoline)",
        "ignore_kind": "parameter items served to kind:\"stack\" (S3-F2's mutation)",
        "known_everywhere": "every unwound site labelled known (the compare with ProcessEvent's start dropped)",
        "known_twice": "a second known frame, of the same function, in each in-scope stack",
        "known_split": "every other in-scope stack labels a frame of another function known, not ProcessEvent's",
        "refuse_main": "refuses a Start that ticks a function",
        "names_stacks_absent": "step 3 in all but its echo: names.stacks left out of the Start's reply",
        "refuse_one": "accepts the first stack choice and refuses the others",
        "echo_default": "echoes its default stack budgets, not the ones sent",
        "unclamped": "echoes a stack budget as sent, not clamped to its 24 bits",
        "k_no_stacks": "sizes K without the stack rings (step 2's formula)",
        "names_no_stack": "the Stop's names[] carry no `stack`",
        "released_at_stop": "releases the main trace at Stop, as if it had kept nothing",
        "pf_flags": "a SnapProbe_PerFrame entry also takes a parameter copy (flags 4|2|32)",
        "call_noflag": "a lone SnapProbe_Call entry says nothing of its stack (neither 32 nor 64) and has no slot",
        "slot_lost": "an in-scope SnapProbe_Call entry flagged 32 has no slot",
        "orphans": "a stack page counts an orphan",
        "slot_fault": "a stack slot flagged Fault",
        "frame0_elsewhere": "the lone stacks' frame 0 lies in another module",
        "site_off": "a site's rva is one off its addr",
        "no_own": "no own frame in the in-scope stacks",
        "lone_own": "an own frame in the lone stacks",
        "stack_dropped0": "trace.stack counts no budget drop though the ring dropped calls",
        "ring_dropped0": "the ring counts no budget drop though trace.stack did",
        "snap_counted": "the stack budget's drops counted in the parameter counters too",
        "leak": "a release leaves trace.stack in the reply",
        "lax_keys": "accepts a stack key whose class string was altered",
        "no_stack_ok": "leaves the stack choices out of the refusal sum (S3-F1's mutation): stacks alone are refused",
        "not_snap_only": "a stacks-only Start is not snap_only (the rule counts parameter rings alone)",
        "refused_unnamed": "names.refused leaves out the stack key it refused",
        "only_flags": "a stacks-only call also takes a parameter copy (flags 4|2|32)",
        "only_other": "a stacks-only trace also records a call of the choice it refused, flagged 4|32",
        "over_total": "a stacks-only recording captures past the total budget",
        "unkeyed": "a fixture row without its name key",
        "no_trace_stack": "names.stacks, but no trace.stack in the reply",
        "names_stacks_off": "names.stacks counts one stack choice more than it accepted",
        "names_ticks_off": "names.ticks counts no tick",
        "names_chosen_off": "names.chosen counts no parameter choice",
        "refused_phantom": "names.refused lists a function nobody asked for",
        "stack_rings_off": "trace.stack.rings counts one ring more than it allocated",
        "depth_off": "trace.stack.depth is not the depth it used",
        "snap_unallocated": "snap.allocated false though the trace kept calls",
        "snap_rings_off": "snap.rings counts the stack rings too",
        "names_stack_false": "the Stop's names[] say stack false on the stack choices",
        "names_all_stack": "the Stop's names[] say stack true on the tick too",
        "names_missing": "pe_trace_names leaves out a stack choice",
        "slot_twice": "an entry's stack written to two slots",
        "slot_stray": "a stack slot whose entry_seq names no entry",
        "bad_ref": "a frame index past its page's sites",
        "short_slot": "a stack slot of two frames",
        "nothing_taken": "every stack choice over its budget: entries flagged 64, no slot written",
        "call_refused": "every SnapProbe_Call stack of the main recording refused as over the budget (flagged 64, "
                        "skipped) though the total has room for it",
        "call_refused_some": "the first round's in-scope SnapProbe_Call stack of the main recording refused as over "
                             "the budget (flagged 64, skipped) though both budgets have room for it; the others "
                             "kept",
        "known_no_fn": "a known frame without unwind data, so without fn",
        "snap_skipped": "the stack budget's refusals counted as skips in the parameter counters",
        "snap_phantom": "the parameter counters count one skip though no budget refused anything",
        "leak_snap": "a release leaves snap in the reply",
        "leak_alloc": "a release leaves the trace allocated",
        "only_empty": "a stacks-only recording keeps no call",
        "accept_unknown": "accepts a Start whose stack keys all name nothing, keeps no call, releases it at Stop",
        "no_calls": "a plain recording that records no call (the game not running, or the hook down)",
        "no_probes": "a plain recording without the fixture's probes (a package without them)",
        "no_code_addr": "pe_trace_names gives SnapNest_Outer no code_addr",
        # The table fetched after a traced recording, which the main recording's own rate is read from.
        "main_get_error": "pe_profile_get answers an error once a trace was started",
        "main_window0": "pe_profile_get reports window_ms 0 once a trace was started",
        "main_rows_lost": "pe_profile_get loses SnapProbe_PerFrame's row once a trace was started",
        "main_pf_under": "pe_profile_get counts a tenth of SnapProbe_PerFrame's calls once a trace was started: a "
                         "plausible count, and wrong",
        "main_window_long": "pe_profile_get reports three times the traced window once a trace was started: a "
                            "plausible window, and wrong",
        # The game's PDB, as --pdb's session reads it.
        "pdb_none": "no PDB matches the exe",
        "pdb_fragment": "the PDB names one function start at a displacement (a fragment's start)",
        "pdb_unnamed": "the PDB names nothing at one function start",
        "pdb_pe_other": "the known site's function is not ProcessEvent in the PDB",
        "pdb_outer_other": "SnapNest_Outer's native entry is another function in the PDB",
        # S3-A1's names, as --names asks the DLL about them.
        "names_none": "no site named (no ufunc on any frame)",
        "names_wrong_func": "get_object names SnapNest_Outer's UFunction another function",
        "names_wrong_outer": "get_object puts SnapNest_Outer's UFunction in another class",
        "names_not_function": "get_object says the interpreter's UFunction is a Class, not a Function",
        "names_fn_absent": "SnapNest_Outer's UFunction does not hold its fn",
        "names_offset_split": "the interpreter's UFunction holds its fn at another offset than SnapNest_Outer's",
        "names_many": "the first stack holds NAMES_MANY more named frames, each its own entry, before the others",
        "names_read_edge": "the interpreter's UFunction ends right after its Func slot, before a page that cannot be "
                           "read: read_mem of anything past Func + 8 fails",
        "names_read_failed": "read_mem fails at every size for the interpreter's UFunction",
        "names_slot_only": "read_mem of the interpreter's UFunction fails at the window and every retry, yet a read of "
                           "one of its slots alone (8 bytes) answers",
        "names_unreadable": "read_mem fails at every size for every UFunction",
        "names_object_error": "get_object answers an error for SnapNest_Outer's UFunction",
        "names_empty": "SnapNest_Outer's UFunction named \"\": its frames carry class and func \"\", and get_object "
                       "answers \"\" for its name and outer (the DLL's name reads gave nothing)",
        "names_empty_class": "only the outer's name read gave nothing: SnapNest_Outer's frames carry class \"\", and "
                             "get_object answers \"\" for its outer, its own name kept",
        "names_empty_func": "only the function's name read gave nothing: SnapNest_Outer's frames carry func \"\", and "
                            "get_object answers \"\" for its name, its outer kept",
        "names_no_fn": "the interpreter's named frame has no unwind data, so no fn (and no fn_rva)",
        "names_no_fn_all": "no named frame has unwind data, so none has fn",
        "names_short_only": "the interpreter's UFunction cannot be read at any size, and SnapNest_Outer's fails past "
                            "0xE0 and holds its fn only at FUNC_AT, with no decoy",
        "names_func_low": "both UFunctions keep Func at 0xD8 (FUNC_LOW), where UE5's UFunction of about 0xE0 bytes "
                          "keeps it",
        "names_read_edge_all": "every UFunction ends 0xE0 bytes in, before a page that cannot be read: read_mem of "
                               "more fails for each",
        # The step-2 run's parameter budget on SnapProbe_PerFrame (a DLL made with late=True).
        "param_dropped0": "SnapProbe_PerFrame's parameter ring counts no budget drop though it dropped calls",
        "param_overkept": "SnapProbe_PerFrame's parameter ring keeps 25 a second whatever its budget, the rest dropped",
    }
    # The scripted game's PDB: a name for each function start its stacks hold, by RVA.
    PDB_NAMES = {0x480440: "ADumperTest58Actor::SnapProbe_Dispatch", OUTER_FN: "ADumperTest58Actor::execSnapNest_Outer",
                 0x7000: "UFunction::Invoke", 0x9000: "UObject::ProcessEvent",
                 0x3000: "ADumperTest58Actor::TraceNest_Dispatch", 0x2000: "ADumperTest58Actor::SnapNest_Fire",
                 0x1000: "FTimerManager::Tick", 0x0800: "UWorld::Tick"}

    def __init__(self, *faults: str, per_frame: int | None = None, pf_rate: float = PF_RATE,
                 main_pf_rate: float | None = None, late: bool = False) -> None:
        """`pf_rate` is SnapProbe_PerFrame's calls a second (a fixture at 30 fps calls it about 30 times) in the plain
        recordings, `main_pf_rate` in every recording a trace is started for (pf_rate unless given: a game slowed by
        the trace, or one whose frame rate moved); `per_frame`, when given, is the count of its slots a DLL that keeps
        the wrong count writes, whatever the budget. `late` adds SnapLate_Call (LATE), which the step-2 run needs."""
        unknown = set(faults) - set(self.FAULTS)
        if unknown:   # a misspelt fault would script the good DLL, and its control would test nothing
            raise ValueError(f"the scripted DLL has no fault {sorted(unknown)}")
        self.f = set(faults)
        self.funcs = dict(self.FUNCS, **(self.LATE if late else {}))
        self.per_frame = per_frame
        self.pf_rate = pf_rate
        self.main_pf_rate = pf_rate if main_pf_rate is None else main_pf_rate
        self.gen = 0
        self.t: dict | None = None
        self.cmds: list[str] = []
        self.starts: list[dict | None] = []   # each Start's trace object, as the rig sent it
        self.pdb_opens: list[str | None] = []   # each --pdb session's search folder, as the rig asked
        self.pdb_sessions: list[ScriptedPdb] = []
        self.asked: list[tuple[str, dict]] = []   # each get_object / read_mem, with what the rig sent

    def _ufuncs(self) -> dict[int, tuple]:
        """The UFunctions of the script (UFUNCS), names_many's extras with them."""
        u = dict(self.UFUNCS)
        if "names_many" in self.f:
            u.update({0x40000 + 0x200 * k: ("ManyActor", f"Fn{k}", "Function", 0xB0000 + 0x40 * k, (self.FUNC_AT,), None)
                      for k in range(self.NAMES_MANY)})
        return u

    def _named(self, ufunc: int) -> dict:
        """A site's S3-A1 fields for the UFunction at `ufunc`, as the DLL sends them: `class` is its outer's name."""
        if "names_none" in self.f:
            return {}
        cls, func, _, _, _, shared = self._ufuncs()[ufunc]
        if ufunc == self.FUNCS["SnapNest_Outer"][0]:
            if self.f & {"names_empty", "names_empty_class"}:
                cls = ""
            if self.f & {"names_empty", "names_empty_func"}:
                func = ""
        return {"ufunc": f"0x{ufunc:X}", "class": cls, "func": func, **({"shared": shared} if shared else {})}

    def _object(self, addr: str) -> dict:
        """get_object, as the DLL answers it: the object's own name, its class's and its outer's."""
        u = int(str(addr), 16)   # with or without 0x, as Renge::StrToAddr reads it
        row = self._ufuncs().get(u)
        if row is None:   # not an object: the DLL's name reads give nothing
            return {"ok": True, "addr": addr, "name": "", "full_name": "", "class": "", "outer": ""}
        cls, func, kind, _, _, _ = row
        first = u == self.FUNCS["SnapNest_Outer"][0]
        if "names_object_error" in self.f and first:
            return {"ok": False, "error": "the scripted get_object failed (names_object_error)"}
        if "names_empty" in self.f and first:
            return {"ok": True, "addr": addr, "name": "", "full_name": "", "class": kind, "outer": ""}
        if "names_empty_class" in self.f and first:
            cls = ""
        if "names_empty_func" in self.f and first:
            func = ""
        if "names_wrong_func" in self.f and first:
            func = "SnapNest_Fire"
        if "names_wrong_outer" in self.f and first:
            cls = "OtherActor"
        if "names_not_function" in self.f and u == self.INTERP:
            kind = "Class"
        return {"ok": True, "addr": addr, "name": func, "full_name": f"{kind} /Script/Scripted.{cls}:{func}",
                "class": kind, "outer": cls}

    def _memory(self, addr: str, size: int) -> dict:
        """read_mem inside a scripted UFunction, from its start or from a slot within it: filler that never holds an
        address, fn wherever the UFunction stores it. Like Macht::ReadBytesSafe it is one copy, all or nothing: a read
        that runs into memory it cannot read fails whole."""
        a = int(str(addr), 16)
        u = next((x for x in self._ufuncs() if x <= a < x + NAMES_READ), None)
        row = None if u is None else self._ufuncs()[u]
        rel = 0 if u is None else a - u
        end = rel + size                  # how far into the object the read runs
        f = self.f
        outer = u == self.FUNCS["SnapNest_Outer"][0]
        func_at = self.FUNC_LOW if "names_func_low" in f else self.FUNC_AT
        if row is None or "names_unreadable" in f or (u == self.INTERP and (
                f & {"names_read_failed", "names_short_only"} or ("names_read_edge" in f and end > func_at + 8) or
                ("names_slot_only" in f and size > 8))) or \
                ("names_read_edge_all" in f and end > 0xE0) or ("names_short_only" in f and outer and end > 0xE0):
            return {"ok": False, "error": "Read failed"}
        offsets = tuple(func_at if o == self.FUNC_AT else o for o in row[4])
        if "names_short_only" in f and outer:
            offsets = (self.FUNC_AT,)
        if "names_fn_absent" in self.f and outer:
            offsets = ()
        if "names_offset_split" in self.f and u == self.INTERP:
            offsets = (self.FUNC_AT + 8, 0x140)
        blob = bytearray((k * 37 + 11) & 0xFF for k in range(rel, end))
        for o in offsets:
            if rel <= o and o + 8 <= end:
                blob[o - rel: o - rel + 8] = struct.pack("<Q", self.BASE + row[3])
        return {"ok": True, "bytes": bytes(blob).hex().upper()}   # Renge::BytesToHex: two digits a byte, no spaces

    def open_symbols(self, pid: int, exe: tuple[int, int] | None, search: str | None):
        """--pdb's session over the scripted game, as open_game_pdb opens one: a session, or why there is none."""
        self.pdb_opens.append(search)
        if "pdb_none" in self.f:
            return "no PDB matching the scripted exe"
        names = {rva: (n, 0) for rva, n in self.PDB_NAMES.items()}
        if "pdb_fragment" in self.f:
            names[0x3000] = (self.PDB_NAMES[0x3000], 0x40)
        if "pdb_unnamed" in self.f:
            del names[0x2000]
        if "pdb_pe_other" in self.f:
            names[0x9000] = ("UObject::CallFunction", 0)
        if "pdb_outer_other" in self.f:
            names[self.OUTER_FN] = ("ADumperTest58Actor::execSnapNest_Fire", 0)
        self.pdb_sessions.append(ScriptedPdb(names, self.BASE, 0x10000000))
        return self.pdb_sessions[-1]

    def window_s(self) -> float:
        """The window pe_profile_get reports: the plain recording's, or once a trace was started the traced one's."""
        return DRY_RECORD_S if any(self.starts) else self.WINDOW_S

    def rows(self) -> dict[str, dict]:
        """The fixture rows a recording gives, by name: SnapProbe_PerFrame at the main rate, over the traced
        recording's window, once a trace was started."""
        unkeyed = "SnapProbe_PerFrame" if "unkeyed" in self.f else None
        pf = self.main_pf_rate if any(self.starts) else self.pf_rate
        return {n: {"class_name": FIXTURE_CLASS, "func_name": n, "fname_key": None if n == unkeyed else k,
                    "parms_size": ps, "num_parms": 1,
                    "count": round(pf * self.window_s()) if n == "SnapProbe_PerFrame" else 6,
                    "per_frame": n == "SnapProbe_PerFrame"}
                for n, (_, k, ps) in self.funcs.items()}

    def _site(self, rva: int, fn_rva: int, **extra) -> dict:
        return dict({"addr": f"0x{self.BASE + rva:X}", "module": FIXTURE_EXE, "module_base": f"0x{self.BASE:X}",
                     "rva": rva, "fn": f"0x{self.BASE + fn_rva:X}", "fn_rva": fn_rva, "unwind": True}, **extra)

    def _stacks(self, rnd: int = 0) -> tuple[list[dict], list[dict]]:
        """Round `rnd`'s in-scope and lone stacks. A DLL labels a site by its address, so a fault that labels some
        stacks differently takes its other stacks through other call sites."""
        g = self._site
        known = {} if "no_known" in self.f else {"known": KNOWN_PE}
        if "known_split" in self.f and rnd % 2:
            invoke, pe = g(0x7020, 0x7000, **known), g(0x9133, 0x9000)
        else:
            invoke, pe = g(0x7010, 0x7000), g(0x9123, 0x9000, **known)
        if "known_no_fn" in self.f:
            pe = {k: v for k, v in pe.items() if k not in ("fn", "fn_rva")} | {"unwind": False}
        own = {"addr": f"0x{self.OWN + 0x45678:X}", "module": "dxgi.dll", "module_base": f"0x{self.OWN:X}",
               "rva": 0x45678, "fn": f"0x{self.OWN + 0x45000:X}", "fn_rva": 0x45000, "unwind": True, "own": True}
        call = g(0x4804C9, 0x480440)
        # SnapNest_Outer's native entry carries its name, as S3-A1 sends it.
        outer = g(self.OUTER_FN + 0x31, self.OUTER_FN, **self._named(self.FUNCS["SnapNest_Outer"][0]))
        in_scope = [call, outer, invoke, pe, own, g(0x3010, 0x3000), g(0x2010, 0x2000), g(0x1010, 0x1000)]
        if "known_twice" in self.f:
            in_scope.insert(5, g(0x9456, 0x9000, **known))
        if "no_own" in self.f:
            in_scope.remove(own)
        lone = [call, g(0x2020, 0x2000), g(0x1010, 0x1000), g(0x0810, 0x0800)]
        if "frame0_elsewhere" in self.f:
            lone[0] = {"addr": "0x7FFC00001010", "module": "other.dll", "module_base": "0x7FFC00000000", "rva": 0x1010,
                       "fn": "0x7FFC00001000", "fn_rva": 0x1000, "unwind": True}
        if "site_off" in self.f:
            lone[1] = dict(lone[1], rva=lone[1]["rva"] + 1)
        if "lone_own" in self.f:
            lone.insert(2, own)
        if "known_everywhere" in self.f:
            in_scope, lone = ([dict(s, known=KNOWN_PE) if s.get("unwind") else s for s in x] for x in (in_scope, lone))
        return in_scope, lone

    def _game_stack(self, first: bool) -> list[dict]:
        """A stacks-only call's stack, standing for a real game's: SnapNest_Outer's native entry one frame
        (UFunction::Invoke) below ProcessEvent, and past the hook the interpreter, the entry every script function
        shares, with no ProcessEvent beyond it. names_many puts its extra named frames first in the first stack, so
        only the run's ordering by frequency keeps the two entries above in the ones it asks about."""
        frames = self._stacks()[0]
        k = next((i + 1 for i, s in enumerate(frames) if s.get("own")), len(frames))
        frames.insert(k, self._site(self.INTERP_FN + 0x51, self.INTERP_FN, **self._named(self.INTERP)))
        if first and "names_many" in self.f:
            frames = [self._site(row[3] + 0x11, row[3], **self._named(u)) for u, row in self._ufuncs().items()
                      if u not in self.UFUNCS] + frames
        interp = f"0x{self.INTERP:X}"
        return [{k: v for k, v in s.items() if k not in ("fn", "fn_rva")} | {"unwind": False}
                if "ufunc" in s and ("names_no_fn_all" in self.f or ("names_no_fn" in self.f and s["ufunc"] == interp))
                else s for s in frames]

    def _start(self, t: dict | None) -> dict:
        if t is None:
            return {"data": {"recording": True, "hook_active": True}}
        by_key = {tuple(k): n for n, (_, k, _) in self.funcs.items()}
        f = self.f

        def good(it: dict, lax: bool = False) -> bool:
            keys = it.get("keys") or [[]]
            return (lax or it.get("class") == FIXTURE_CLASS) and by_key.get(tuple(keys[0])) == it.get("func")
        s = t.get("snapshots") or {}
        st = s.get("stacks") if isinstance(s.get("stacks"), dict) and "old" not in f else None
        if t.get("ticked_names") and "refuse_main" in f:
            return {"error": "the scripted DLL refuses a Start that ticks (refuse_main)"}
        asked = t.get("ticked_names", []) + s.get("funcs", []) + (st or {}).get("funcs", [])
        ticks = [i for i in t.get("ticked_names", []) if good(i)]
        params = [i["func"] for i in s.get("funcs", []) if good(i)]
        stack_items = [i for i in (st or {}).get("funcs", []) if good(i, "lax_keys" in f)]
        turned_away = stack_items[1:] if "refuse_one" in f else []
        stacks = [i["func"] for i in stack_items if i not in turned_away]
        if (not ticks and not params and (not stacks or "no_stack_ok" in f)) or (t.get("ticked_names") and not ticks):
            if "accept_unknown" in f and (st or {}).get("funcs"):
                self.gen += 1
                self.t = {"gen": self.gen, "recs": [], "slots": [], "depth": 16, "dropped": [], "tracing": True,
                          "ticks": 0, "snap_only": True, "snap": {"allocated": True, "rings": 0}, "stack": None,
                          "names": [], "empty": True}
                return {"data": {"recording": True, "trace": dict(self._info(), names={"stacks": 0})}}
            return {"error": "None of the chosen functions is known by that name in this game any more."}
        self.gen += 1
        ring_of = {n: k for k, n in enumerate(stacks)}
        lone = self._stacks()[1]
        recs: list[bytes] = []
        slots: list[list[dict]] = [[] for _ in stacks]
        dropped = [0] * len(stacks)           # each stack ring's lone calls refused, and so not recorded at all
        skipped = [0] * len(stacks)           # its calls recorded for their parameters without their stack
        # The budgets as the DLL clamps them, admitted as Linie's StackAdmit does: in call order within each second,
        # the ring's word first, then the total's, which every stack choice shares. A call the total refuses has used
        # one of its ring's places.
        per = min(max(int((st or {}).get("per_ring_per_s", 100)), 1), 0xFFFFFF)
        total = min(max(int((st or {}).get("total_per_s", 200)), 1), 0xFFFFFF)
        ring_word: dict[tuple[str, int], int] = {}
        total_word: dict[int, int] = {}
        p_kept = {n: 0 for n in params}       # each parameter choice's calls copied, and those its budget dropped
        p_dropped = {n: 0 for n in params}

        def admit(func: str, at: float) -> bool:
            sec = int(at)
            if ring_word.get((func, sec), 0) >= per:
                return False
            ring_word[(func, sec)] = ring_word.get((func, sec), 0) + 1
            if total_word.get(sec, 0) >= total:
                return False
            total_word[sec] = total_word.get(sec, 0) + 1
            return True

        def call(func: str, flags: int, frames: list[dict], at: float = 0.0, inner=None, flag32: bool = True,
                 slot: bool = True, forced: bool = False, refused: bool = False) -> None:
            """One call at `at` seconds into the recording. `forced` keeps its stack whatever the budget, as a DLL that
            keeps the wrong count does; `refused` refuses it whatever the budget, as a DLL that refuses wrongly does."""
            stacked = func in ring_of and flag32
            refuse = "nothing_taken" in f or ("call_refused" in f and func == "SnapProbe_Call" and bool(ticks)) or \
                refused
            taken = stacked and not refuse and (forced or admit(func, at))
            over = stacked and not taken
            if over and flags & F_LONE and func not in params:
                # Linie's TraceEnter: a lone call that took nothing it was chosen for is dropped -- no record at all.
                dropped[ring_of[func]] += 1
                return
            if over:      # recorded for its parameters, flagged, its stack counted as skipped
                skipped[ring_of[func]] += 1
            seq = len(recs)
            if func in params:
                p_kept[func] += 1
            flags |= (F_TAKEN if func in params else 0) | (F_STACK_TAKEN if taken else 0) | \
                (F_STACK_BUDGET if over else 0)
            recs.append(REC.pack(seq, seq * 10, self.funcs[func][0], 0x5000, 1, flags))
            if taken and slot:
                ring = slots[ring_of[func]]
                first = ring_of[func] == 0 and not ring
                ring.append({"entry_seq": seq, "frames": frames,
                             "flags": STK_FAULT if "slot_fault" in f and first else 0})
                if first and "slot_twice" in f:
                    ring.append({"entry_seq": seq, "frames": frames, "flags": 0})
                if first and "slot_stray" in f:
                    ring.append({"entry_seq": 99999, "frames": frames, "flags": 0})
            if inner:
                inner()
            r = len(recs)
            recs.append(REC.pack(r | RET_BIT, r * 10, seq, 0, 1, 0))
        if ticks:      # the main recording: scoped by SnapNest_Outer
            # The calls in time order over the dry run's --record-s (the DLL is never told the span): four rounds
            # evenly spaced, each SnapNest_Outer -> SnapProbe_Call then a lone SnapProbe_Call, and SnapProbe_PerFrame
            # every 1/main_pf_rate seconds from the start, a frame's call before a round's at the same instant.
            pf_events = "SnapProbe_PerFrame" in ring_of and self.per_frame is None
            events = [((rnd + 0.5) * DRY_RECORD_S / 4, 1, rnd) for rnd in range(4)]
            if pf_events:
                events += [(k / self.main_pf_rate, 0, k) for k in range(round(self.main_pf_rate * DRY_RECORD_S))]
            for at, kind, k in sorted(events):
                if kind == 0:
                    call("SnapProbe_PerFrame", F_LONE | (F_TAKEN if "pf_flags" in f and k == 0 else 0), lone[:3], at)
                    continue
                in_scope = self._stacks(k)[0]
                call("SnapNest_Outer", F_ROOT, [], at, lambda fr=in_scope, r=k, a=at: call(
                    "SnapProbe_Call", 0, fr, a, slot=not ("slot_lost" in f and r == 0),
                    refused="call_refused_some" in f and r == 0))
                call("SnapProbe_Call", F_LONE, lone[:2] if "short_slot" in f and k == 0 else lone, at,
                     flag32=not ("call_noflag" in f and k == 0))
            if "SnapProbe_PerFrame" in ring_of and not pf_events:
                # A DLL that keeps the wrong count writes per_frame slots whatever the budget, and counts as dropped
                # what a probe at main_pf_rate over min(per, total) a second would drop.
                rate, cap = self.main_pf_rate, min(per, total)
                for k in range(self.per_frame):
                    call("SnapProbe_PerFrame", F_LONE | (F_TAKEN if "pf_flags" in f and k == 0 else 0), lone[:3],
                         forced=True)
                dropped[ring_of["SnapProbe_PerFrame"]] = round(max(0.0, rate - cap) * DRY_RECORD_S)
            elif "SnapProbe_PerFrame" in params:
                # Step 2: chosen for its parameters, it is held to the parameter budget by the same rule.
                rate = self.main_pf_rate
                cap = 25 if "param_overkept" in f else min(max(int(s.get("per_ring_per_s", 1000)), 1), 0xFFFFFF)
                for _ in range(round(min(rate, cap) * DRY_RECORD_S)):
                    call("SnapProbe_PerFrame", F_LONE, [])
                if "param_dropped0" not in f:
                    p_dropped["SnapProbe_PerFrame"] = round(max(0.0, rate - cap) * DRY_RECORD_S)
        else:          # stacks only: every chosen call is lone, a few of each, all kept (over_total: past the total)
            game, game_first = self._game_stack(False), self._game_stack(True)
            for n in [] if "only_empty" in f else stacks:
                for k in range(1000 if "over_total" in f else 3):
                    call(n, F_LONE | (F_TAKEN if "only_flags" in f and k == 0 else 0), game if recs else game_first,
                         forced=True)
            if "only_other" in f:   # flagged as a kept stack would be, so only the function tells it apart
                call("SnapProbe_PerFrame", F_LONE | F_STACK_TAKEN, [])
        depth = min(max(int((st or {}).get("depth", 16)), 1), 62)
        caps = [ring_cap(self.funcs[n][2]) for n in params]
        stack_terms = 0 if "k_no_stacks" in f else len(stacks)
        per_round = sum(24 + c for c in caps) + stack_terms * (24 + 8 * depth)
        sb = s.get("bytes", 0)
        if "unclamped" in f:
            budget = lambda k, dflt: int(st.get(k, dflt))
        elif "echo_default" in f:
            budget = lambda k, dflt: dflt
        else:
            budget = lambda k, dflt: min(max(int(st.get(k, dflt)), 1), 0xFFFFFF)
        captures = sum(len(x) for x in slots)
        self.t = {"gen": self.gen, "recs": recs, "slots": slots, "depth": depth, "dropped": dropped, "skipped": skipped,
                  "tracing": True, "ticks": len(ticks),
                  "snap_rings": [{"ring": k, "written": p_kept[n], "skipped_budget": 0, "dropped_budget": p_dropped[n]}
                                 for k, n in enumerate(params)],
                  "snap_only": not ticks and (bool(params) or "not_snap_only" not in f),
                  "snap": {"allocated": "snap_unallocated" not in f, "bytes": sb,
                           "rings": len(params) + (len(stacks) if "snap_rings_off" in f else 0),
                           "per_ring_per_s": 1000, "total_per_s": 10000,
                           # A refusal counted in the wrong place shows only as far as the stack budget refused calls,
                           # as on the DLL it stands for; a phantom count shows whatever was refused.
                           "skipped_budget": (sum(dropped) + sum(skipped) if "snap_skipped" in f else 0) +
                                             ("snap_phantom" in f),
                           "dropped_budget": sum(dropped) if "snap_counted" in f else 0,
                           "slots_per_ring": (sb - 64 * (len(params) + stack_terms)) // per_round if per_round else 0},
                  "stack": {"rings": len(stacks) + ("stack_rings_off" in f), "depth": depth * (1 + ("depth_off" in f)),
                            "per_ring_per_s": budget("per_ring_per_s", 100),
                            "total_per_s": budget("total_per_s", 200), "captures": captures,
                            "skipped_budget": sum(skipped), "dropped_budget": 0 if "stack_dropped0" in f else sum(dropped),
                            "spent_ticks": 7 * captures, "max_ticks": 7} if stacks else None,
                  "names": [{"class": FIXTURE_CLASS, "func": n, "tick": n in [i["func"] for i in ticks],
                             "chosen": n in params, "addresses": 1, "arms": 1,
                             **({} if f & {"old", "names_no_stack"} else
                                {"stack": "names_all_stack" in f or (n in ring_of and "names_stack_false" not in f)})}
                            for n in self.funcs if n in params or n in ring_of or n in [i["func"] for i in ticks]]}
        accepted = [id(i) for i in stack_items if i not in turned_away]
        refused = [i for i in asked if id(i) not in accepted and (i in turned_away or not good(i))]
        if "refused_phantom" in f:
            refused.append({"class": FIXTURE_CLASS, "func": "Phantom"})
        names = {"ticks": 0 if "names_ticks_off" in f else len(ticks),
                 "chosen": 0 if "names_chosen_off" in f else len(params),
                 "refused": [] if "refused_unnamed" in f else
                 [{"class": i.get("class"), "func": i.get("func"), "why": "no key names it in this process"}
                  for i in refused]}
        if not f & {"old", "names_stacks_absent"}:
            names["stacks"] = len(stacks) + ("names_stacks_off" in f)
        return {"data": {"recording": True, "hook_active": True, "trace": dict(self._info(), names=names)}}

    def _info(self) -> dict:
        t = self.t
        if t is None:   # released; a leak fault leaves part of the trace in the reply
            return dict({"allocated": "leak_alloc" in self.f, "tracing": False, "gen": self.gen},
                        **({"stack": {"rings": 0}} if "leak" in self.f else {}),
                        **({"snap": {"allocated": False}} if "leak_snap" in self.f else {}))
        d = {"allocated": True, "tracing": t["tracing"], "quiesced": not t["tracing"], "gen": t["gen"],
             "qpc_freq": self.QPC, "written": len(t["recs"]), "first_valid": 0, "scoped": True,
             "ticked_names": t["ticks"], "snap_only": t["snap_only"], "snap": t["snap"]}
        if t.get("snap_rings"):
            d["snap_rings"] = t["snap_rings"]
        if t["stack"] and "no_trace_stack" not in self.f:
            d["stack"] = t["stack"]
        return d

    def _snap_get(self, p: dict) -> dict:
        t, frm, ring = self.t, p.get("from", 0), p.get("ring", -1)
        d = dict(self._info(), ring=ring)
        if t is None or t["tracing"]:
            return {"data": dict(d, count=0, next=frm, items=[])}
        if self.f & {"old", "ignore_kind"}:
            return {"data": dict(d, count=1, next=frm + 1, orphans=0, items=[
                {"index": frm, "entry_seq": 1, "phase": "entry", "len": 0, "flags": 0, "arm": 0, "data": ""}])}
        d.update(kind="stack", rings=[{"ring": k, "cap": 8 * t["depth"], "depth": t["depth"], "written": len(x),
                                       "first_valid": 0, "skipped_budget": t.get("skipped", [0] * (k + 1))[k],
                                       "dropped_budget": 0 if "ring_dropped0" in self.f else t["dropped"][k],
                                       "spent_ticks": 7 * len(x)} for k, x in enumerate(t["slots"])])
        if p.get("gen") != t["gen"] or not 0 <= ring < len(t["slots"]):
            return {"data": dict(d, stale=p.get("gen") != t["gen"], count=0, next=frm, items=[])}
        sites: list[dict] = []
        index: dict[str, int] = {}
        items = []
        for k, s in enumerate(t["slots"][ring][frm: frm + min(p.get("max", 1024), self.PAGE)]):
            fr = []
            for site in s["frames"]:
                if site["addr"] not in index:
                    index[site["addr"]] = len(sites)
                    sites.append(site)
                fr.append(index[site["addr"]])
            items.append({"index": frm + k, "entry_seq": s["entry_seq"], "flags": s.get("flags", 0), "ticks": 7,
                          "frames": fr})
        first_page = ring == 0 and frm == 0
        if "bad_ref" in self.f and first_page and items:
            items[0]["frames"].append(len(sites) + 5)
        orphans = 1 if "orphans" in self.f and first_page else 0
        return {"data": dict(d, count=len(items), next=frm + len(items), orphans=orphans, items=items, sites=sites)}

    def request(self, cmd: str, **p) -> dict:
        self.cmds.append(cmd)
        t = self.t
        if cmd == "pe_profile_start":
            self.starts.append(p.get("trace"))
            return self._start(p.get("trace"))
        if cmd == "pe_profile_stop":
            if t is None:
                return {"data": {"recording": False}}
            t["tracing"] = False
            if t.get("empty") or ("released_at_stop" in self.f and t["ticks"]):   # an empty trace goes at Stop
                self.t = None
            return {"data": {"recording": False, "trace": self._info(), "names": t["names"]}}
        if cmd == "pe_profile_get":
            traced = any(self.starts)
            if traced and "main_get_error" in self.f:
                return {"error": "the scripted pe_profile_get failed (main_get_error)"}
            rows = [] if "no_calls" in self.f else list(self.rows().values())
            if "no_probes" in self.f:   # a game without the fixture's probes still records its own calls
                rows = [{"class_name": "OtherActor", "func_name": "Tick", "fname_key": None, "count": 100}]
            if traced and "main_rows_lost" in self.f:
                rows = [r for r in rows if r.get("func_name") != "SnapProbe_PerFrame"]
            if traced and "main_pf_under" in self.f:
                rows = [dict(r, count=r["count"] // 10) if r.get("func_name") == "SnapProbe_PerFrame" else r
                        for r in rows]
            window_ms = 0 if traced and "main_window0" in self.f else \
                int(self.window_s() * 1000) * (3 if traced and "main_window_long" in self.f else 1)
            return {"data": {"total_calls": sum(r["count"] for r in rows), "window_ms": window_ms,
                             "functions": rows[: p.get("limit", len(rows))]}}
        if cmd == "pe_trace_get":
            if t is None:
                return self._info()
            frm, mx = p.get("from", 0), p.get("max", 1)
            part = t["recs"][frm: frm + mx]
            return dict(self._info(), data=base64.b64encode(b"".join(part)).decode(), next=frm + len(part))
        if cmd == "pe_trace_names":
            items = [{"addr": f"0x{a:X}", "class_name": FIXTURE_CLASS, "func_name": n,
                      "code_addr": "" if "no_code_addr" in self.f and n == "SnapNest_Outer" else
                      f"0x{self.BASE + (self.OUTER_FN if n == 'SnapNest_Outer' else 0x8000):X}"}
                     for n, (a, _, _) in self.funcs.items()
                     if not ("names_missing" in self.f and n == "SnapProbe_PerFrame")]
            off = p.get("offset", 0)
            return {"data": {"items": items[off: off + p.get("limit", 20000)], "total": len(items)}}
        if cmd == "pe_snap_get":
            return self._snap_get(p)
        if cmd == "pe_trace_release":
            self.t = None
            return {"data": {"released": True}}
        if cmd == "get_object":
            self.asked.append((cmd, dict(p)))
            return self._object(p.get("addr", "0"))
        if cmd == "read_mem":
            self.asked.append((cmd, dict(p)))
            return self._memory(p.get("addr", "0"), int(p.get("size", 256)))
        # Step 2's commands, answered as far as its budget line needs (the class docstring).
        if cmd == "invoke_function":
            return {"ok": True, "data": {}}
        if cmd == "pe_snap_layouts":
            return {"data": {"arms": [], "layouts": [], "total": 0, "next": 0}}
        raise PipeError(f"the scripted DLL has no {cmd}")


class ScriptedPdb:
    """--pdb's session as the scripted game answers it: (name, displacement) by RVA, as DbghelpPdb.sym answers."""
    def __init__(self, names: dict[int, tuple[str, int]], base: int, size: int) -> None:
        self.names, self.base, self.size = names, base, size
        self.pdb = "scripted.pdb"
        self.closed = False

    def sym(self, rva: int) -> tuple[str, int] | None:
        return self.names.get(rva)

    def close(self) -> None:
        self.closed = True


def dry_run(dll: ScriptedDll, game: bool = False, argv: tuple[str, ...] = (), stacks: bool = True) -> \
        tuple[Checks, dict]:
    """main()'s --stacks run (run_selected) against a scripted DLL, its printing captured: no pipe, no game, and no
    wait, on a FakeClock; --pdb reads the scripted game's PDB. `argv` adds options to the command line the run parses,
    after (so over) the ones it sets, parsed as main() parses it: a combination main() refuses raises SystemExit here
    before the DLL is asked anything. `stacks` False runs the step-2 checks instead (run_full), on a DLL made with
    late=True. out["exit"] is what run_selected returned."""
    check, out = Checks(), {"label": "dry"}
    args = parse_args((["--stacks"] if stacks else []) + ["--record-s", str(DRY_RECORD_S), "--plain-s", "0"] +
                      (["--choose", ""] if game else []) + list(argv))
    fake = FakeClock()
    with contextlib.redirect_stdout(io.StringIO()):
        out["exit"] = run_selected(dll, check, out, args, pid=0, sleep=fake.sleep, clock=fake.now,
                                   symbols=dll.open_symbols)
    return check, out


def self_test() -> int:
    """--self-test: the pure pieces of --stacks against hand-made replies, each rule holding on a good input AND failing
    on a bad one, so a helper that silently accepts everything cannot pass. Then run_stacks and run_game_stacks against
    a scripted DLL, whole and with each fault it scripts, and run_full as far as its parameter budget, whose line alone
    is read. That side is kept complete by controls of two kinds: every scripted fault has its control, and every check
    the good runs make (--names's included) is named by a control whose fault fails it, so a check whose condition is
    reduced to True fails the self-test."""
    results: list[tuple[str, bool, str]] = []

    def expect(name: str, fn) -> None:
        try:
            ok, why = bool(fn()), ""
        # A helper that throws on a hand-made reply, or a dry run whose command line parse_args refuses (SystemExit),
        # fails its control: it does not end the run.
        except (Exception, SystemExit) as e:
            ok, why = False, f"{type(e).__name__}: {e}"
        results.append((name, ok, why))

    # K, as Linie computes it: the design's 2.2 examples, S3-L1 case 5, and step 2's formula when no stack is chosen.
    expect("K: 5 param rings of 64 B and 2 stack rings at depth 16 in 32 MB is 45,099",
           lambda: k_with_stacks(32 << 20, [64] * 5, 2, 16) == 45099)
    expect("K: 1 param ring of 64 B and 1 stack ring in 32 MB is 139,809",
           lambda: k_with_stacks(32 << 20, [64], 1, 16) == 139809)
    expect("K: S3-L1 case 5 (cap 16, depth 4, 896 B) is 8", lambda: k_with_stacks(128 + 8 * 96, [16], 1, 4) == 8)
    expect("K: stacks only, 1 ring at depth 16", lambda: k_with_stacks(32 << 20, [], 1, 16) == ((32 << 20) - 64) // 152)
    expect("K: without stacks it is step 2's K", lambda: k_with_stacks(32 << 20, [256, 64], 0, 16) ==
           k_for(32 << 20, [256, 64]) and k_for(8 << 20, [2048] * 512) < 8)
    expect("K: nothing chosen keeps nothing", lambda: k_with_stacks(32 << 20, [], 0, 16) == 0)
    expect("StackRingCap clamps the depth to 1..62", lambda: [stack_ring_cap(d) for d in (0, 4, 16, 62, 100)] ==
           [8, 32, 128, 496, 496])
    expect("a budget echoes clamped to 1..0xFFFFFF", lambda: [budget_echo(v) for v in (0, -5, 30, 200, 1 << 30)] ==
           [1, 1, 30, 200, 0xFFFFFF])

    # M3: an old DLL is told from a refusal by names.stacks being absent, never by trace.stack.
    expect("M3: names without `stacks` is an old DLL", lambda: stack_echo_state({"names": {"ticks": 1, "chosen": 1}},
                                                                                2) == "old")
    expect("M3: no names at all is an old DLL", lambda: stack_echo_state({"snap": {}}, 1) == "old")
    expect("M3: fewer than asked is a refusal", lambda: stack_echo_state({"names": {"stacks": 1}}, 2) == "refused")
    expect("M3: 0 of 1 is a refusal, not an old DLL", lambda: stack_echo_state({"names": {"stacks": 0}}, 1) == "refused")
    expect("M3: all named", lambda: stack_echo_state({"names": {"stacks": 2}}, 2) == "ok")

    # A page shaped as the design's section 3.3 example.
    base, game = 0x7FF6A0000000, FIXTURE_EXE
    raw_sites = [
        {"addr": f"0x{base + 4719817:X}", "module": game, "module_base": f"0x{base:X}", "rva": 4719817,
         "fn": f"0x{base + 4719680:X}", "fn_rva": 4719680, "unwind": True},
        {"addr": f"0x{base + 15729955:X}", "module": game, "module_base": f"0x{base:X}", "rva": 15729955,
         "fn": f"0x{base + 15728640:X}", "fn_rva": 15728640, "unwind": True, "known": "process_event"},
        {"addr": "0x7FFC12345678", "module": "dxgi.dll", "module_base": "0x7FFC12300000", "rva": 284280,
         "fn": "0x7FFC12345000", "fn_rva": 282624, "unwind": True, "own": True},
        {"addr": "0x2A0000123", "module": "", "unwind": False},
    ]
    sites = [parse_site(s) for s in raw_sites]

    def item_(i: int, seq: int, frames: list, flags: int = 0, ticks: int = 7) -> dict:
        return {"index": i, "entry_seq": seq, "flags": flags, "ticks": ticks, "frames": frames}
    page = {"kind": "stack", "ring": 0, "rings": [{"ring": 0, "written": 2, "first_valid": 0}], "count": 2, "next": 2,
            "orphans": 0, "items": [item_(0, 10, [0, 1, 2]), item_(1, 20, [0, 3, 0], STK_MORE)], "sites": raw_sites}
    expect("a site's hex strings parse; absent fields are None, an absent module \"\"",
           lambda: sites[2]["addr"] == 0x7FFC12345678 and sites[2]["own"] and sites[1]["known"] == KNOWN_PE and
           sites[3]["module"] == "" and sites[3]["module_base"] is None and sites[3]["fn"] is None and
           not sites[3]["unwind"])

    def resolved_ok() -> bool:
        slots, bad = resolve_stack_page(page)
        return (bad == [] and [[f["addr"] for f in s["frames"]] for s in slots] ==
                [[sites[0]["addr"], sites[1]["addr"], sites[2]["addr"]],
                 [sites[0]["addr"], sites[3]["addr"], sites[0]["addr"]]] and slots[1]["flags"] == STK_MORE)
    expect("frames resolve to THIS page's sites, by index", resolved_ok)

    def bad_refs() -> bool:
        slots, bad = resolve_stack_page(dict(page, items=[item_(0, 10, [0, 9, True, -1]), {"index": 1}]))
        return len(slots[0]["frames"]) == 1 and len(bad) == 4
    expect("an index past the sites, a bool, a negative index and absent frames are reported, never dropped", bad_refs)

    # M1: an all-orphan page in the middle of a ring must not end the read.
    rings = [{"ring": 1, "written": 9, "first_valid": 2}]
    pages = {0: {"kind": "stack", "rings": rings, "items": [item_(2, 1, [0]), item_(3, 2, [0])], "sites": raw_sites,
                 "next": 4, "orphans": 0},
             4: {"kind": "stack", "rings": rings, "items": [], "sites": [], "next": 7, "orphans": 3},
             7: {"kind": "stack", "rings": rings, "items": [item_(7, 3, [1]), item_(8, 4, [1])], "sites": raw_sites,
                 "next": 9, "orphans": 0}}
    expect("M1: an all-orphan page mid-ring does not end the read",
           lambda: (lambda r: [s["index"] for s in r["slots"]] == [2, 3, 7, 8] and r["orphans"] == 3 and
                    r["pages"] == 3 and r["problems"] == [] and r["ring"]["written"] == 9)(
               page_stack_ring(lambda f: pages[f], 1)))
    expect("a page that does not move `next` ends the read",
           lambda: page_stack_ring(lambda f: dict(pages[0], next=0), 1)["pages"] == 1)
    expect("a stale page is a problem, not an empty ring",
           lambda: page_stack_ring(lambda f: {"stale": True, "count": 0, "items": []}, 0)["problems"] != [])
    expect("parameter items (a DLL that ignored kind) are a problem",
           lambda: page_stack_ring(lambda f: {"items": [{"index": 0, "entry_seq": 1, "phase": "entry", "data": ""}],
                                              "next": 1}, 0)["problems"] != [])
    expect("a reply whose rings do not list the ring is a problem",
           lambda: page_stack_ring(lambda f: {"kind": "stack", "rings": [], "items": [], "next": 0}, 3)["problems"] != [])

    # S1: the join and the flags. A record is (seq, ticks, func, obj, tid, flags).
    CALL, PF = 0x1000, 0x2000
    recs = {10: (10, 0, CALL, 0, 1, F_STACK_TAKEN | F_TAKEN), 11: (11, 0, CALL, 0, 1, F_LONE | F_TAKEN | F_STACK_BUDGET),
            20: (20, 0, PF, 0, 1, F_LONE | F_STACK_TAKEN), 21: (21, 0, PF, 0, 1, F_LONE | F_STACK_TAKEN)}
    funcs = {0: CALL, 1: PF}

    def sl(seq: int, idx: int, flags: int = 0, n: int = 3) -> dict:
        return {"index": idx, "entry_seq": seq, "flags": flags, "frames": [sites[0]] * n}

    def problems(slots_by_ring: dict, key: str):
        return s1_join(recs, funcs, slots_by_ring)[key]
    whole = {0: [sl(10, 0, STK_MORE)], 1: [sl(20, 0), sl(21, 1)]}
    expect("S1: a whole join has no problem, and More is no fault",
           lambda: (lambda j: all(j[k] == [] for k in S1_PROBLEMS) and j["taken"] == 3 and j["slots"] == 3)(
               s1_join(recs, funcs, whole)))
    expect("S1: an entry flagged 32 without its slot", lambda: problems({0: [sl(10, 0)], 1: [sl(20, 0)]},
                                                                       "no_slot") == [21])
    expect("S1: two slots for one entry",
           lambda: problems({**whole, 1: [sl(20, 0), sl(20, 1), sl(21, 2)]}, "two_slots") == [20])
    expect("S1: a slot of an entry flagged 64, not 32, is stray",
           lambda: problems({**whole, 0: [sl(10, 0), sl(11, 1)]}, "stray") == [(0, 1)])
    expect("S1: a slot in another function's ring is stray",
           lambda: problems({**whole, 0: [sl(10, 0), sl(20, 1)]}, "stray") == [(0, 1)])
    expect("S1: a slot of no entry is stray",
           lambda: problems({**whole, 0: [sl(10, 0), sl(99, 1)]}, "stray") == [(0, 1)])
    for nm, bit in (("Partial", STK_PARTIAL), ("Fault", STK_FAULT), ("BadSp", STK_BADSP), ("LowStack", STK_LOWSTACK),
                    ("NoCapturer", STK_NOCAPTURER)):
        expect(f"S1: a slot flagged {nm} is reported",
               lambda bit=bit: problems({**whole, 0: [sl(10, 0, bit)]}, "bad_flags") == [(0, 0, bit)])
    expect("S1: a slot of 2 frames is short",
           lambda: problems({**whole, 0: [sl(10, 0, n=2)]}, "short") == [(0, 0, 2)])
    expect("S1: stack flags: 32 or 64 on every entry of a chosen function, 4|32 exactly where asked",
           lambda: stack_flag_problems(recs, CALL) == [] and
           stack_flag_problems(recs, PF, exact=F_LONE | F_STACK_TAKEN) == [])
    expect("S1: an entry with neither 32 nor 64, or both, is reported",
           lambda: stack_flag_problems({**recs, 12: (12, 0, CALL, 0, 1, F_TAKEN),
                                        13: (13, 0, CALL, 0, 1, F_STACK_TAKEN | F_STACK_BUDGET)}, CALL) == [12, 13])
    expect("S1: a lone stack-only entry with a parameter copy is not 4|32",
           lambda: stack_flag_problems({**recs, 22: (22, 0, PF, 0, 1, F_LONE | F_TAKEN | F_STACK_TAKEN)}, PF,
                                       exact=F_LONE | F_STACK_TAKEN) == [22])

    # S2: frame 0, the CE text, and a site's arithmetic.
    exe = (base, base + 0x2000000)
    s0 = sites[0]
    expect("S2: the CE text of frame 0, and CE adding it back (module name in any case: L11)",
           lambda: ce_text(s0) == f'"{game}"+4804C9' and
           ce_text_addr(f'"{game.lower()}"+4804C9', {game: base}) == s0["addr"] and
           ce_text_addr("4804C9", {game: base}) is None and ce_text_addr('"other.dll"+10', {game: base}) is None)
    expect("S2: frame 0 in the exe holds", lambda: s2_frame0_problems(s0, game, exe) == [] and
           s2_frame0_problems(s0, game, None) == [])
    expect("S2 (L11): the module name compares without case",
           lambda: s2_frame0_problems(s0, game.lower(), exe) == [] and s2_frame0_problems(s0, game.upper(), None) == [])
    expect("S2: another module is reported", lambda: s2_frame0_problems(sites[2], game, exe) != [])
    expect("S2: module_base + rva off by one is reported",
           lambda: s2_frame0_problems(dict(s0, rva=s0["rva"] + 1), game, None) != [])
    expect("S2: an address outside the exe's image is reported",
           lambda: s2_frame0_problems(s0, game, (base + 0x10000000, base + 0x20000000)) != [])
    expect("S2: a module_base psapi disagrees with is reported, though base + rva still adds up",
           lambda: s2_frame0_problems(dict(s0, module_base=base + 0x1000, rva=s0["rva"] - 0x1000), game, exe) != [])
    expect("S2: frame 0 outside any module is reported", lambda: s2_frame0_problems(sites[3], game, exe) != [])
    expect("S2: every site of the design's example adds up", lambda: [site_problems(s) for s in sites] == [[]] * 4)
    expect("S2: fn at or above addr is reported", lambda: site_problems(dict(s0, fn=s0["addr"])) != [])
    expect("S2: unwind without fn, and fn without unwind, are reported",
           lambda: site_problems(dict(s0, fn=None)) != [] and site_problems(dict(sites[3], fn=0x2A0000000)) != [])
    expect("S2: module_base + fn_rva off is reported", lambda: site_problems(dict(s0, fn_rva=s0["fn_rva"] + 16)) != [])
    expect("S2: a module without module_base, and a site without addr, are reported",
           lambda: site_problems(dict(s0, module_base=None)) != [] and site_problems(dict(s0, addr=None)) != [])

    # S3 and S4: where the hook and ProcessEvent sit, and SnapNest_Outer's entry before them.
    g2 = dict(s0, addr=base + 0x5010, fn=base + 0x5000, rva=0x5010, fn_rva=0x5000)
    in_scope = [s0, g2, sites[1], sites[2], s0]
    expect("S3: in scope, known:process_event then own", lambda: nesting(in_scope) == (2, 3) and
           known_before_own(nesting(in_scope)))
    expect("S3: a lone stack holds neither", lambda: nesting([s0, g2, s0]) == (None, None))
    expect("S3: own before known is not known-before-own", lambda: not known_before_own(nesting([sites[2], sites[1]]))
           and not known_before_own(nesting([s0, sites[2]])))
    expect("S4: SnapNest_Outer's entry found before the bound, not past it",
           lambda: outer_frame(in_scope, base + 0x5000, 2) == 1 and outer_frame(in_scope, base + 0x5000, 1) is None
           and outer_frame(in_scope, base + 0x9999, None) is None)

    # S5 and S6.
    expect("S5: 30/s over 8 s keeps 210..270", lambda: budget_window(30, 8.0, 8.0) == (210.0, 270.0))
    expect("S5: 300 or 200 slots over 8 s at 30/s are outside it",
           lambda: (lambda lo, hi: not lo <= 300 <= hi and not lo <= 200 <= hi)(*budget_window(30, 8.0, 8.0)))
    expect("S6: mean and max microseconds a capture from the design's example reply",
           lambda: (lambda cst: abs(cst["mean_us"] - 2412345 / 812 / 10.0) < 1e-9 and abs(cst["max_us"] - 5123.4) < 1e-9
                    and cst["captures_per_s"] == 101.5)(
               stack_cost({"captures": 812, "spent_ticks": 2412345, "max_ticks": 51234}, 10_000_000, 8.0)))
    expect("S6: no captures gives no mean", lambda: stack_cost({"captures": 0}, 10_000_000, 8.0)["mean_us"] is None)
    expect("S6: D3's re-weigh", lambda: [reweigh_total(v) for v in (1.413, 7.0, 10.0, None, 0)] ==
           [1400, 250, 200, None, None])
    expect("S6: the slots' ticks agree with the totals, and a lost tick is seen",
           lambda: slot_ticks_agree([{"ticks": 7}] * 3, {"spent_ticks": 21, "max_ticks": 7})["agree"] and
           not slot_ticks_agree([{"ticks": 7}] * 3, {"spent_ticks": 22, "max_ticks": 7})["agree"] and
           not slot_ticks_agree([{"ticks": 9}], {"spent_ticks": 9, "max_ticks": 7})["agree"])

    # The flags as the design numbers them, and the options as the ledger names them.
    expect("flags: entry 32 / 64; slot 1 / 2 / 4 / 8 / 16 / 0x8000; More is no fault",
           lambda: (F_STACK_TAKEN, F_STACK_BUDGET, STK_PARTIAL, STK_FAULT, STK_MORE, STK_BADSP, STK_LOWSTACK,
                    STK_NOCAPTURER) == (32, 64, 1, 2, 4, 8, 16, 0x8000) and STK_BAD & STK_MORE == 0 and
           STK_BAD == 0x801B)
    expect("options: --stacks off by default; the stack budgets unset until given (each run picks its own default)",
           lambda: (lambda a, b: not a.stacks and not a.self_test and a.stack_per_ring is None and
                    a.stack_total is None and a.record_s == 8.0 and b.stacks and b.stack_per_ring == 7)(
               build_parser().parse_args([]), build_parser().parse_args(["--stacks", "--stack-per-ring", "7"])))

    # --pdb (S3-R2): its predicates over scripted symbol answers. Nothing here loads dbghelp; the structs it would be
    # handed are checked against dbghelp.h's layout.
    expect("PDB: SYMBOL_INFOW is dbghelp.h's 88 bytes with Name at 84; IMAGEHLP_MODULEW64 its 3,264, SymType at 32",
           lambda: ctypes.sizeof(SYMBOL_INFOW) == 88 and SYMBOL_INFOW.Name.offset == 84 and
           SYMBOL_INFOW.Value.offset == 48 and ctypes.sizeof(IMAGEHLP_MODULEW64) == 3264 and
           IMAGEHLP_MODULEW64.SymType.offset == 32 and IMAGEHLP_MODULEW64.LoadedPdbName.offset == 1124 and
           IMAGEHLP_MODULEW64.PdbAge.offset == 3220)
    no_unwind = dict(s0, addr=base + 0x10, rva=0x10, fn=None, fn_rva=None, unwind=False)
    expect("PDB: the starts asked are the exe's unwound sites', each once, the module compared without case",
           lambda: sorted(pdb_targets([{"frames": sites + [s0, no_unwind]}], game)) == [4719680, 15728640] and
           sorted(pdb_targets([{"frames": sites}, {"frames": [s0]}], game.upper())) == [4719680, 15728640] and
           pdb_targets([{"frames": sites}], "other.exe") == {})
    answers = {1: ("A::F", 0), 2: ("B::G", 0x40), 4: ("UObject::ProcessEvent", 0)}
    expect("PDB: a start named at displacement 0 holds; a displacement (a fragment's start) or no symbol is reported",
           lambda: pdb_misses(answers, [1, 4]) == [] and len(pdb_misses(answers, [1, 2, 3])) == 2)
    expect("PDB: the name must hold the function asked for",
           lambda: pdb_misses(answers, [4], "ProcessEvent") == [] and
           len(pdb_misses(answers, [1, 4], "ProcessEvent")) == 1)
    named = {4719680: ("ADumperTest58Actor::SnapProbe_Dispatch", 0), 15728640: ("UObject::ProcessEvent", 0)}
    expect("PDB: a readable stack names the exe's frames with the offset into them, the rest as CE's text or the address",
           lambda: readable_stack(sites, named, game) == [
               "#0 ADumperTest58Actor::SnapProbe_Dispatch +0x89", "#1 UObject::ProcessEvent +0x523 [process_event]",
               "#2 \"dxgi.dll\"+45678 [the dumper's hook]", "#3 0x2a0000123"] and
           readable_stack([s0], {4719680: ("F", 0x40)}, game) == ["#0 F +0xC9"] and
           # another module's frame never takes the exe's name, though its fn_rva is one the exe's PDB names
           readable_stack([dict(s0, module="other.dll")], named, game) == ['#0 "other.dll"+4804C9'])

    def pdb_opt(*argv: str) -> str | None:
        return pdb_option_problem(build_parser().parse_args(list(argv)))
    expect("PDB: --pdb belongs to the --stacks fixture run, bare (beside the exe) or with a folder",
           lambda: pdb_opt("--stacks", "--pdb") is None and pdb_opt("--stacks", "--pdb", "syms") is None and
           pdb_opt("--pdb") is not None and pdb_opt("--stacks", "--pdb", "--choose", "x") is not None and
           pdb_opt("--stacks", "--fixture-check", "--pdb") is not None and
           build_parser().parse_args(["--stacks", "--pdb"]).pdb == "" and
           build_parser().parse_args(["--stacks", "--pdb", "syms"]).pdb == "syms" and
           build_parser().parse_args([]).pdb is None)

    # The refusal itself, driven through main() and through the dry run: with it gone, --pdb is silently ignored on a
    # run that cannot use it while the predicate's control above still holds.
    bad_pdb = (("--pdb",), ("--stacks", "--choose", "", "--pdb"), ("--stacks", "--fixture-check", "--pdb"))

    def refused(run) -> int | None:
        """The exit code a refusal ends `run` with, argparse's usage text captured; None when it ran on."""
        try:
            with contextlib.redirect_stderr(io.StringIO()), contextlib.redirect_stdout(io.StringIO()):
                run()
        except SystemExit as e:
            return e.code
        return None

    def main_refuses(argv: tuple[str, ...]) -> tuple[int | None, int]:
        opened: list[bool] = []

        def connect():
            opened.append(True)
            raise PipeError("scripted: no pipe")
        return refused(lambda: main(list(argv), connect=connect)), len(opened)
    expect("PDB: main() refuses --pdb without --stacks, with --choose or with --fixture-check before the pipe opens, "
           "and lets --stacks --pdb reach it",
           lambda: [main_refuses(a) for a in bad_pdb] == [(2, 0)] * 3 and
           main_refuses(("--stacks", "--pdb")) == (None, 1))

    def dry_refused(game: bool, argv: tuple[str, ...]) -> tuple[int | None, list[str]]:
        dll = ScriptedDll()
        return refused(lambda: dry_run(dll, game=game, argv=argv)), dll.cmds
    expect("PDB: the dry run refuses --pdb with --choose or --fixture-check as main() does, before the DLL is asked "
           "anything",
           lambda: dry_refused(True, ("--pdb",)) == (2, []) and
           dry_refused(False, ("--fixture-check", "--pdb")) == (2, []))

    # The whole run against a scripted DLL: the glue between the helpers, which nothing else runs before a game does.
    def ran(check: Checks) -> list[str]:
        return [n for n, _, _ in check.items]

    def failing(check: Checks) -> list[str]:
        return [n for n, ok, _ in check.items if not ok]

    def recorded(check: Checks, prefix: str) -> list:
        return [e for n, e, _ in check.records if n.startswith(prefix)]

    def fail_set(check: Checks, *prefixes: str) -> bool:
        """The run fails exactly the checks the prefixes name, each at least once, and nothing else: a fault caught
        only by some other check would leave the named one unproven."""
        bad = failing(check)
        return bad != [] and all(any(n.startswith(p) for p in prefixes) for n in bad) and \
            all(any(n.startswith(p) for n in bad) for p in prefixes)
    # What a live --stacks run on a correct DLL prints: main() checks the fixture first, then S0-S7.
    fixture_names = ["the table recorded calls"] + [f"{FIXTURE_CLASS}::{p} was called" for p in PROBES]
    expect(f"dry run: --stacks on a DLL with step 3 holds every check, the fixture's {len(fixture_names)} then S0-S7's "
           f"26, {len(fixture_names) + 26} in all, and records 7 facts",
           lambda: (lambda ch: failing(ch) == [] and len(ran(ch)) == len(fixture_names) + 26 and
                    ran(ch)[:len(fixture_names)] == fixture_names and
                    {n.split()[0] for n in ran(ch)[len(fixture_names):]} == {"S0", "S1", "S2", "S3", "S5", "S7"} and
                    recorded(ch, "S3") == [] and len(recorded(ch, "S4")) == 1 and len(recorded(ch, "S6")) == 6 and
                    len(ch.records) == 7)(dry_run(ScriptedDll())[0]))
    expect("dry run: --fixture-check stops after the fixture's checks",
           lambda: (lambda r: ran(r[0]) == fixture_names and failing(r[0]) == [] and r[1].get("exit") is None)(
               dry_run(ScriptedDll(), argv=("--fixture-check",))))
    expect("dry run: a plain recording with no call ends the run after the fixture check, exit 2",
           lambda: (lambda r: r[1].get("exit") == 2 and not any(n.startswith("S") for n in ran(r[0])))(
               dry_run(ScriptedDll("no_calls"))))
    win_lo, win_hi = budget_window(FIXTURE_STACK_PER_RING, DRY_RECORD_S, DRY_RECORD_S)
    expect(f"dry run: S5's window over {DRY_RECORD_S:g} s is {win_lo:.0f}..{win_hi:.0f}, read on the run's own clock",
           lambda: win_lo > 0 and any(n.startswith(S5_WINDOW) and f"({win_lo:.0f}..{win_hi:.0f})" in n
                                      for n in ran(dry_run(ScriptedDll())[0])))
    expect("dry run: S4 finds SnapNest_Outer's entry at frame 1 of every in-scope stack",
           lambda: any(n.startswith("S4") and g.startswith("4 of 4; at frames [1]")
                       for n, _, g in dry_run(ScriptedDll())[0].records))
    expect("dry run (H1): a DLL without step 3 fails S0 and runs no S1-S6",
           lambda: (lambda ch: failing(ch) == ["S0 the reply carries names.stacks and trace.stack (a DLL with step 3)"]
                    and not any(n[:2] in ("S1", "S2", "S3", "S5") for n in ran(ch)))(
               dry_run(ScriptedDll("old"))[0]))

    # Every check the two runs make, failing on a scripted fault it exists to catch. By default the run must fail
    # exactly the named checks (fail_set); `exact=False` is for a fault that also breaks what later checks read, where
    # the named check must be among the failures. A check whose condition is replaced by True passes on its fault, so
    # its control fails.
    caught_by: set[str] = set()
    exercised: set[str] = {"old"}

    def caught(faults: tuple[str, ...], *prefixes: str, game: bool = False, argv: tuple[str, ...] = (),
               exact: bool = True, per_frame: int | None = None, pf_rate: float = ScriptedDll.PF_RATE) -> None:
        caught_by.update(prefixes)
        exercised.update(faults)
        what = "+".join(faults) or (f"per_frame={per_frame}" if per_frame is not None else "") or \
            (f"pf_rate={pf_rate:g}" if pf_rate != ScriptedDll.PF_RATE else "") or " ".join(argv)

        def run() -> bool:
            ch = dry_run(ScriptedDll(*faults, per_frame=per_frame, pf_rate=pf_rate), game=game, argv=argv)[0]
            if exact:
                return fail_set(ch, *prefixes)
            return all(any(n.startswith(p) for n in failing(ch)) for p in prefixes)
        expect(f"dry run{' --choose' if game else ''}: {what} fails {' | '.join(p[:44] for p in prefixes)}", run)
    s7_main, s7_only = "S7 the release frees everything (the main", "S7 the release frees everything (the stacks-only"
    caught(("no_calls",), "the table recorded calls", f"{FIXTURE_CLASS}::")
    caught(("no_probes",), f"{FIXTURE_CLASS}::", "the rows the stack run needs carry keys")
    caught(("unkeyed",), "the rows the stack run needs carry keys")
    caught(("refuse_main",), "S0 the main Start is accepted")
    caught(("names_stacks_absent",), "S0 the reply carries names.stacks")
    caught(("no_trace_stack",), "S0 the reply carries names.stacks")
    caught(("refuse_one",), "S0 names:", exact=False)
    caught(("names_stacks_off",), "S0 names:", "S0 ...its altered second key")
    caught(("names_ticks_off",), "S0 names:")
    caught(("names_chosen_off",), "S0 names:")
    caught(("refused_phantom",), "S0 names:")
    caught(("echo_default",), "S0 trace.stack:")
    caught(("unclamped",), "S0 trace.stack:", argv=("--stack-total", str(1 << 24)))
    caught(("stack_rings_off",), "S0 trace.stack:", "S0 ...snap_only")
    caught(("depth_off",), "S0 trace.stack:")
    caught(("k_no_stacks",), "S0 one parameter ring", "S0 ...snap_only")
    caught(("snap_unallocated",), "S0 one parameter ring", "S0 ...snap_only")
    caught(("snap_rings_off",), "S0 one parameter ring", "S0 ...snap_only")
    caught(("names_no_stack",), "S0 the Stop reply's names")
    caught(("names_stack_false",), "S0 the Stop reply's names")
    caught(("names_all_stack",), "S0 the Stop reply's names")
    caught(("released_at_stop",), "the trace kept calls")
    caught(("pf_flags",), "S1 every SnapProbe_Call entry says")
    caught(("call_noflag",), "S1 every SnapProbe_Call entry says")
    caught(("names_missing",), "S1 every SnapProbe_Call entry says", exact=False)
    caught(("slot_lost",), "S1 every entry flagged 32")
    caught(("slot_twice",), "S1 every entry flagged 32")
    caught(("slot_stray",), "S1 every entry flagged 32")
    caught(("orphans",), "S1 no orphans")
    caught(("bad_ref",), "S1 no orphans")
    caught(("ignore_kind",), "S1 no orphans", exact=False)
    caught(("slot_fault",), "S1 no slot Partial")
    caught(("short_slot",), "S1 no slot Partial")
    caught(("frame0_elsewhere",), "S2 frame 0")
    caught(("site_off",), "S2 every site adds up")
    # Nothing taken: the checks that would hold on an empty set must still fail.
    caught(("nothing_taken",), "S1 every entry flagged 32", "S1 no slot Partial", "S2 frame 0", "S2 every site adds up",
           "S3 every in-scope stack holds an own frame", "S3 no lone stack holds an own frame", S3_KNOWN_IN,
           S3_KNOWN_LONE, exact=False)
    caught(("no_own",), "S3 every in-scope stack holds an own frame", S3_KNOWN_IN)
    caught(("lone_own",), "S3 no lone stack holds an own frame")
    caught(("no_known",), S3_KNOWN_IN)              # S3-F2's mutation: known compared with the trampoline
    caught(("known_everywhere",), S3_KNOWN_LONE, S3_KNOWN_ONE)
    caught(("known_twice",), S3_KNOWN_ONE)
    caught(("known_split",), S3_KNOWN_ONE)
    caught(("known_no_fn",), S3_KNOWN_ONE)
    caught((), S5_WINDOW, per_frame=20)
    caught((), S5_WINDOW, per_frame=100)
    caught(("stack_dropped0",), S5_WINDOW)
    caught(("ring_dropped0",), S5_WINDOW)
    caught(("snap_counted",), "S5 the parameter counters")
    caught(("snap_skipped",), "S5 the parameter counters")
    caught(("snap_phantom",), "S5 the parameter counters")
    caught(("leak",), s7_main, s7_only)
    caught(("leak_snap",), s7_main, s7_only)
    caught(("leak_alloc",), s7_main, "S0 an altered stack key alone refuses", s7_only)
    caught(("lax_keys",), "S0 an altered stack key alone refuses", exact=False)
    caught(("accept_unknown",), "S0 an altered stack key alone refuses")
    caught(("no_stack_ok",), "S0 a stacks-only Start")     # S3-F1's mutation: stackOk out of the refusal sum
    caught(("not_snap_only",), "S0 ...snap_only")
    caught(("refused_unnamed",), "S0 ...its altered second key")
    caught(("only_flags",), "S0 ...it records lone")
    caught(("only_other",), "S0 ...it records lone")
    caught(("only_empty",), "S0 ...it records lone")
    caught((), "functions to choose were found", game=True, argv=("--choose", "no such function"))
    caught(("no_stack_ok",), "the stacks-only Start is accepted", game=True)
    caught(("names_stacks_absent",), "the reply carries names.stacks", game=True)
    caught(("no_trace_stack",), "the reply carries names.stacks", game=True)
    caught(("refuse_one",), "every stack choice is named", game=True)
    caught(("over_total",), "the total budget held", game=True)
    expect("dry run: a total over 24 bits is echoed clamped, and the run holds",
           lambda: failing(dry_run(ScriptedDll(), argv=("--stack-total", str(1 << 24)))[0]) == [])

    # --pdb against the scripted game's PDB.
    pdb_checks = [PDB_DISP, PDB_PE, PDB_OUTER]
    expect(f"dry run --pdb: every check holds, {len(fixture_names) + 26} and the PDB's {len(pdb_checks)}, with 8 "
           "recorded (the first in-scope stack named)",
           lambda: (lambda ch: failing(ch) == [] and [n for n in ran(ch) if n.startswith("PDB")] == pdb_checks and
                    len(ran(ch)) == len(fixture_names) + 26 + len(pdb_checks) and len(ch.records) == 8 and
                    len(recorded(ch, "PDB")) == 1)(dry_run(ScriptedDll(), argv=("--pdb",))[0]))

    def pdb_stack_record(ch: Checks) -> str:
        return next((g for n, _, g in ch.records if n.startswith("PDB")), "")
    expect("dry run --pdb: the first in-scope stack reads by name, ProcessEvent and the hook in their places",
           lambda: (lambda g: g.startswith("#0 ADumperTest58Actor::SnapProbe_Dispatch +0x89 | ") and
                    "| #1 ADumperTest58Actor::execSnapNest_Outer +0x31 | #2 UFunction::Invoke +0x10 | " in g and
                    "| #3 UObject::ProcessEvent +0x123 [process_event] | #4 \"dxgi.dll\"+45678 [the dumper's hook] |"
                    in g)(pdb_stack_record(dry_run(ScriptedDll(), argv=("--pdb",))[0])))

    def pdb_opens(argv: tuple[str, ...]) -> tuple[list, bool]:
        dll = ScriptedDll()
        dry_run(dll, argv=argv)
        return dll.pdb_opens, all(s.closed for s in dll.pdb_sessions)
    expect("dry run: the PDB is opened only with --pdb, once, beside the exe unless a folder is given, then closed",
           lambda: pdb_opens(()) == ([], True) and pdb_opens(("--pdb",)) == ([None], True) and
           pdb_opens(("--pdb", "syms")) == (["syms"], True))
    exercised.add("pdb_none")
    expect("dry run --pdb: no PDB reports the PDB checks not run, makes none, and fails nothing",
           lambda: (lambda ch: failing(ch) == [] and not any(n.startswith("PDB") for n in ran(ch)) and
                    any(n == PDB_NOT_RUN and why.startswith("no PDB") for n, why in ch.skipped))(
               dry_run(ScriptedDll("pdb_none"), argv=("--pdb",))[0]))
    caught(("pdb_fragment",), PDB_DISP, argv=("--pdb",))
    caught(("pdb_unnamed",), PDB_DISP, argv=("--pdb",))
    caught(("pdb_pe_other",), PDB_PE, argv=("--pdb",))
    caught(("pdb_outer_other",), PDB_OUTER, argv=("--pdb",))
    caught(("no_code_addr",), PDB_OUTER, argv=("--pdb",))
    # With nothing to ask, the PDB checks fail: a run whose stacks hold no site is not a pass.
    caught(("no_known",), S3_KNOWN_IN, PDB_PE, argv=("--pdb",))
    caught(("nothing_taken",), PDB_DISP, PDB_PE, argv=("--pdb",), exact=False)

    # --names (S3-A1 on a real game): its pieces over hand-made sites, then the --choose run against the scripted DLL.
    A1_KEYS = ("ufunc", "class", "func", "shared")
    expect("A1: a site keeps ufunc / class / func / shared; absent they are None / \"\" / \"\" / None, as is a shared "
           "that is not a count",
           lambda: (lambda s, t, u: (s["ufunc"], s["class"], s["func"], s["shared"]) == (0x2A000, "C", "F", 3) and
                    (t["ufunc"], t["class"], t["func"], t["shared"]) == (None, "", "", None) and u["shared"] is None and
                    {k: v for k, v in s.items() if k not in A1_KEYS} == {k: v for k, v in t.items() if k not in A1_KEYS}
                    and len(s) == len(A1_KEYS) + 9)(
               parse_site(dict(raw_sites[0], ufunc="0x2A000", func="F", shared=3, **{"class": "C"})),
               parse_site(raw_sites[0]), parse_site(dict(raw_sites[0], shared="3"))))
    fn_a, fn_b, uf_a, uf_b = base + 0x5000, base + 0xA000, 0x2A000, 0x2B000

    def nsite(ufunc: int | None, fn: int, known: str = "", **more) -> dict:
        raw = {"addr": f"0x{fn + 0x10:X}", "fn": f"0x{fn:X}", "unwind": True, "known": known, **more}
        if ufunc is not None:
            raw.update({"ufunc": f"0x{ufunc:X}", "class": "C", "func": f"F{ufunc:X}"})
        return parse_site(raw)
    expect("A1: named frames group by (ufunc, fn), the most frequent first, every named frame counted",
           lambda: (lambda r: r[1] == 5 and [(e["ufunc"], e["fn"], e["frames"]) for e in r[0]] ==
                    [(uf_a, fn_a, 3), (uf_b, fn_b, 1), (uf_a, fn_b, 1)] and r[0][1]["shared"] == 5 and
                    r[0][0]["shared"] is None and (r[0][0]["class"], r[0][0]["func"]) == ("C", "F2A000"))(
               named_entries([{"frames": [nsite(uf_b, fn_b, shared=5), nsite(None, base + 0x7000), nsite(uf_a, fn_a)]},
                              {"frames": [nsite(uf_a, fn_a), nsite(uf_a, fn_a), nsite(uf_a, fn_b)]}])))
    pe_at = nsite(None, base + 0x9000, KNOWN_PE)
    na, nb, plain = nsite(uf_a, fn_a), nsite(uf_b, fn_b), nsite(None, base + 0x7000)
    expect("A1: the frames between a named frame and the next known:\"process_event\" toward the root; None past the "
           "last one",
           lambda: pe_gap([na, plain, pe_at, nb], 0) == 1 and pe_gap([na, pe_at], 0) == 0 and
           pe_gap([pe_at, na, plain], 1) is None and pe_gap([na, plain, pe_at, nb, pe_at], 3) == 0)
    blob = bytearray((k * 37 + 11) & 0xFF for k in range(0x180))
    for o in (0x30, 0xD8):
        blob[o: o + 8] = struct.pack("<Q", fn_a)
    blob[0x101: 0x109] = struct.pack("<Q", fn_a)    # unaligned: no pointer member sits there
    blob[0x140: 0x148] = struct.pack(">Q", fn_b)    # big-endian: not how x64 stores it
    expect("A1: fn is found at every 8-aligned offset that holds it little-endian, and nowhere else",
           lambda: fn_offsets(bytes(blob), fn_a) == [0x30, 0xD8] and fn_offsets(bytes(blob), fn_b) == [] and
           fn_offsets(bytes(blob[:0xDC]), fn_a) == [0x30] and fn_offsets(b"", fn_a) == [])
    expect("A1: the offset common to every entry, the lowest of several; none when one entry lacks it or none is shared",
           lambda: common_offset([[0x30, 0xD8], [0xD8, 0x140]]) == 0xD8 and
           common_offset([[0x30, 0xD8, 0x140], [0xD8, 0x140]]) == 0xD8 and common_offset([[0x30], [0xD8]]) is None and
           common_offset([[0xD8], []]) is None and common_offset([]) is None)
    expect("A1: an offset past where an entry's read stopped is not contradicted by it; one inside its read is",
           lambda: common_offset([[0x30, 0x148], []], [0x160, 0xE0]) == 0x148 and
           common_offset([[0x30, 0x148], [0x30]], [0x160, 0xE0]) == 0x30 and
           common_offset([[0x148], []], [0x160, 0x160]) is None and
           common_offset([[0x148], []], [0x160, 0x14C]) == 0x148 and common_offset([[0x148], []], [0x160, 0x150]) is None
           and common_offset([[], []], [0xE0, 0xC8]) is None)

    names_argv = ("--names",)

    def names_run(*faults: str, argv: tuple[str, ...] = names_argv) -> tuple[Checks, dict, list]:
        dll = ScriptedDll(*faults)
        ch, out = dry_run(dll, game=True, argv=argv)
        return ch, out, dll.asked
    expect("dry run --names: --stacks --choose holds every check, A1's two after the run's 5, and records 2 more",
           lambda: (lambda r: failing(r[0]) == [] and ran(r[0])[5:] == [NAMES_IS, NAMES_AT] and len(ran(r[0])) == 7 and
                    len(r[0].records) == 8 and len(recorded(r[0], "A1")) == 2)(names_run()))
    expect("dry run --names: under the cap every entry is asked and none is left unchecked, and no line says one was",
           lambda: (lambda r: (r[1]["names"]["asked"], r[1]["names"]["unchecked"]) == (2, 0) and
                    not any("left unchecked" in g for n, _, g in r[0].items if n.startswith("A1")))(names_run()))
    # N is the entries judged -- held, or a slot that holds something else -- and the number asked is said beside it,
    # so a line with entries gone since they were named does not read as that many failures (the second review's N).
    expect("dry run --names: Func at the one offset both entries share, never at a decoy, 'in 2 of 2 read (2 asked)'",
           lambda: names_run()[1]["names"]["func_offset"] == ScriptedDll.FUNC_AT and
           any(f"Func at +0x{ScriptedDll.FUNC_AT:X} in 2 of 2 read (2 asked)" in g
               for n, _, g in names_run()[0].items if n == NAMES_AT))
    expect("dry run --names: 30 frames named in 2 entries, 1 shared (by 37); the native entry 1 frame below ProcessEvent, "
           "the interpreter with none beyond it",
           lambda: (lambda n: n["frames"] == 30 and n["entries"] == 2 and n["shared_entries"] == 1 and
                    n["shared_max"] == 37 and n["pe_gap"] == {"1": 15, "none": 15})(names_run()[1]["names"]))
    expect("dry run --names: each entry asked once, get_object by its ufunc in hex without 0x, then read_mem of 0x160 "
           "bytes there (0x158 + 8, the end of the window the DLL looks for Func in); without --names nothing is asked",
           lambda: names_run()[2] == [("get_object", {"addr": "1000"}), ("read_mem", {"addr": "1000", "size": 0x160}),
                                      ("get_object", {"addr": "2A000"}), ("read_mem", {"addr": "2A000", "size": 0x160})]
           and names_run(argv=())[2] == [])

    def read_sizes(asked: list, addr: str) -> list[int]:
        return [p["size"] for cmd, p in asked if cmd == "read_mem" and p["addr"] == addr]
    # [SNAPRIG-NAMES] MED-1: read_mem is one copy, all or nothing, and a UFunction (0xC8 to 0xE0 bytes) is smaller than
    # the window. An object that ends a block whose next page cannot be read fails a read of the whole window.
    # The second review's LOW: the DLL named every frame by reading its UFunction's slot, so once the offset is known an
    # entry read short of it (the retries jump from 0x160 to 0x100, past a slot in between) has that slot read alone.
    interp_slot = f"{ScriptedDll.INTERP + ScriptedDll.FUNC_AT:X}"
    expect("dry run --names: a UFunction that ends right after Func, before a page that cannot be read: the window "
           "read fails, the retry at 0x100 stops short of Func, and the slot at the common offset, read alone (8 bytes), "
           "holds fn: both held, nothing read short, nothing failed",
           lambda: (lambda r: failing(r[0]) == [] and NAMES_AT in ran(r[0]) and
                    read_sizes(r[2], "2A000") == [0x160, 0x100] and read_sizes(r[2], "1000") == [0x160] and
                    read_sizes(r[2], interp_slot) == [8] and
                    [x["read"] for x in r[1]["names"]["per_entry"]] == [0x160, 0x100] and
                    (r[1]["names"]["func_offset"], r[1]["names"]["held"], r[1]["names"]["short"]) ==
                    (ScriptedDll.FUNC_AT, 2, 0) and
                    any(n == NAMES_AT and "in 2 of 2 read (2 asked)" in g for n, _, g in r[0].items))(
               names_run("names_read_edge")))
    # Read alone, a slot that holds another value is the wrong slot: the frame's fn is not where every other entry
    # keeps it.
    caught(("names_read_edge", "names_offset_split"), NAMES_AT, game=True, argv=names_argv)
    expect("dry run --names: an entry read short whose slot, read alone, holds another value fails the offset check, "
           "what the slot holds the reason",
           lambda: any(n == NAMES_AT and not ok and f"+0x{ScriptedDll.FUNC_AT:X} holds 0x" in g and "OnScripted" in g
                       for n, ok, g in names_run("names_read_edge", "names_offset_split")[0].items))
    def slot_reads(asked: list) -> list[str]:
        return [p["addr"] for cmd, p in asked if cmd == "read_mem" and p["size"] == 8]
    # With one entry read, every copy of fn in it is a candidate; a slot that cannot be read contradicts none, so the
    # lowest stands, read alone once.
    expect("dry run --names: a UFunction no read reaches (every size down to 0xC8 tried) whose slot cannot be read "
           "alone either is listed as gone since it was named, kept out of N, and fails nothing",
           lambda: (lambda r: failing(r[0]) == [] and
                    read_sizes(r[2], "2A000") == [0x160, 0x100, 0xE0, 0xC8] and
                    slot_reads(r[2]) == [f"{ScriptedDll.INTERP + (r[1]['names']['func_offset'] or 0):X}"] and
                    (r[1]["names"]["held"], r[1]["names"]["gone"]) == (1, 1) and
                    r[1]["names"]["per_entry"][1]["read"] is None and
                    any(n == NAMES_AT and ok and "in 1 of 1 read (2 asked)" in g and "1 gone since" in g and
                        "OnScripted" in g for n, ok, g in r[0].items))(names_run("names_read_failed")))
    # SnapNest_Outer's UFunction, read whole, holds a decoy copy of fn below Func: read alone, the other entry's slot at
    # the decoy holds something else, which rules the decoy out rather than failing the DLL; its slot at Func holds fn.
    exercised.add("names_slot_only")
    expect("dry run --names: one entry read whole (fn at a decoy and at Func), the other's window and retries all "
           "failing but its slots answering alone: the decoy's slot contradicts, Func's holds, both held at Func",
           lambda: (lambda r: failing(r[0]) == [] and slot_reads(r[2]) == ["2A030", interp_slot] and
                    (r[1]["names"]["func_offset"], r[1]["names"]["held"], r[1]["names"]["gone"]) ==
                    (ScriptedDll.FUNC_AT, 2, 0) and
                    any(n == NAMES_AT and ok and f"+0x{ScriptedDll.FUNC_AT:X} in 2 of 2 read (2 asked)" in g
                        for n, ok, g in r[0].items))(names_run("names_slot_only")))
    expect("dry run --names: when no named UFunction can be read, the offset check is not run (the reason given), the "
           "name check still runs, and nothing fails",
           lambda: (lambda r: failing(r[0]) == [] and NAMES_IS in ran(r[0]) and NAMES_AT not in ran(r[0]) and
                    [n for n, why in r[0].skipped if "could be read" in why] == [NAMES_AT])(
               names_run("names_unreadable")))
    exercised.update(("names_read_edge", "names_read_failed", "names_unreadable"))
    # [SNAPRIG-NAMES] The second review's MED-B: a real UE5 game's layout, Func at 0xD8 in a UFunction of about 0xE0
    # bytes, where a read retried at 0xE0 after the window failed still reaches the slot. Such an entry holds fn: it
    # is never read short, nor left out of the offset, and asks for nothing past its own reads.
    exercised.update(("names_func_low", "names_read_edge_all"))

    def low_layout(r) -> bool:
        return failing(r[0]) == [] and NAMES_AT in ran(r[0]) and \
            (r[1]["names"]["func_offset"], r[1]["names"]["held"], r[1]["names"]["short"]) == (ScriptedDll.FUNC_LOW, 2, 0) \
            and {p["addr"] for cmd, p in r[2] if cmd == "read_mem"} == {"1000", "2A000"}
    expect("dry run --names: Func at 0xD8 and the interpreter's next page unreadable: its read retried down to 0xE0 "
           "reaches Func, and both entries hold it there",
           lambda: (lambda r: low_layout(r) and read_sizes(r[2], "2A000") == [0x160, 0x100, 0xE0] and
                    read_sizes(r[2], "1000") == [0x160])(names_run("names_func_low", "names_read_edge")))
    expect("dry run --names: Func at 0xD8 and every UFunction's next page unreadable: no read covers the window, the "
           "offset is still found from the reads retried at 0xE0, and both entries hold it there",
           lambda: (lambda r: low_layout(r) and read_sizes(r[2], "2A000") == [0x160, 0x100, 0xE0] ==
                    read_sizes(r[2], "1000"))(names_run("names_func_low", "names_read_edge_all")))
    # The defensive branch: no offset common to the reads, and the one entry read short of its slot with no copy of
    # fn in what it read, says nothing either way (the second review's U4).
    exercised.add("names_short_only")
    expect("dry run --names: with no offset common to the reads and the only readable entry read short of its slot, "
           "holding no copy of fn, the offset check is not run ('no read reached'), and nothing fails",
           lambda: (lambda r: failing(r[0]) == [] and NAMES_IS in ran(r[0]) and
                    [n for n, why in r[0].skipped if "no read reached" in why] == [NAMES_AT])(
               names_run("names_short_only")))
    # A named frame without fn has nothing to look for, and the DLL names a frame by its fn: it is wrong, never
    # skipped (the second review's U2 / U3); with no named frame carrying fn, the check fails, it is not stood down
    # as if nothing could be read (U8).
    caught(("names_no_fn",), NAMES_AT, game=True, argv=names_argv)
    caught(("names_no_fn_all",), NAMES_AT, game=True, argv=names_argv)
    caught(("names_object_error",), NAMES_IS, game=True, argv=names_argv)
    expect("dry run --names: a get_object that answers an error lists the entry as wrong, the DLL's error the reason",
           lambda: any(n == NAMES_IS and not ok and "get_object failed" in g and "names_object_error" in g
                       for n, ok, g in names_run("names_object_error")[0].items))
    # The review's LOW-3: a name read that gives nothing gives "" on the frame and "" from get_object, which agree.
    caught(("names_empty",), NAMES_IS, game=True, argv=names_argv)
    expect("dry run --names: an entry named \"\" (class and func) is wrong whatever get_object answers, 'named empty' "
           "the reason",
           lambda: any(n == NAMES_IS and not ok and "named empty" in g
                       for n, ok, g in names_run("names_empty")[0].items))
    # Either half read empty is enough (the second review's E1 / E2): '' == '' must not pass for the other half.
    caught(("names_empty_class",), NAMES_IS, game=True, argv=names_argv)
    caught(("names_empty_func",), NAMES_IS, game=True, argv=names_argv)
    expect("dry run --names: an entry with only its class, or only its func, named \"\" is wrong, 'named empty' the "
           "reason",
           lambda: all(any(n == NAMES_IS and not ok and "named empty" in g for n, ok, g in names_run(fault)[0].items)
                       for fault in ("names_empty_class", "names_empty_func")))
    many = ScriptedDll.NAMES_MANY + 2
    expect(f"dry run --names: of {many} entries the {NAMES_MAX} most frequent are asked, the frequent two among them, "
           f"and the {many - NAMES_MAX} left are counted, never silently",
           lambda: (lambda r: failing(r[0]) == [] and
                    (r[1]["names"]["entries"], r[1]["names"]["asked"], r[1]["names"]["unchecked"]) ==
                    (many, NAMES_MAX, many - NAMES_MAX) and
                    sum(1 for cmd, _ in r[2] if cmd == "get_object") == NAMES_MAX and
                    {"1000", "2A000"} <= {p["addr"] for cmd, p in r[2] if cmd == "get_object"} and
                    any(f"{many - NAMES_MAX} less frequent left unchecked" in g for n, _, g in r[0].items
                        if n == NAMES_IS))(names_run("names_many")))

    def depth_sent(argv: tuple[str, ...]) -> int | None:
        dll = ScriptedDll()
        dry_run(dll, game=True, argv=argv)
        return next(t["snapshots"]["stacks"].get("depth") for t in dll.starts if t and "stacks" in t.get("snapshots", {}))
    expect(f"dry run: --stacks --choose sends --stack-depth as the stacks' depth, {STACK_DEPTH} when none is given, and "
           "--names holds at 62",
           lambda: depth_sent(()) == STACK_DEPTH and depth_sent(("--stack-depth", "62")) == 62 and
           depth_sent(("--names", "--stack-depth", "1")) == 1 and
           failing(names_run(argv=("--names", "--stack-depth", "62"))[0]) == [])
    # --choose without --stacks with each option alone: the step-2 game run takes neither (LOW-9).
    bad_game = (("--names",), ("--stacks", "--names"), ("--choose", "x", "--names"), ("--stacks", "--stack-depth", "16"),
                ("--stack-depth", "8"), ("--choose", "x", "--stack-depth", "8"),
                ("--stacks", "--choose", "", "--stack-depth", "0"), ("--stacks", "--choose", "", "--stack-depth", "63"))
    expect("A1: main() refuses --names and --stack-depth outside --stacks --choose, and a depth outside 1..62, before the "
           "pipe opens; --stacks --choose takes both",
           lambda: [main_refuses(a) for a in bad_game] == [(2, 0)] * len(bad_game) and
           main_refuses(("--stacks", "--choose", "", "--names", "--stack-depth", "62")) == (None, 1) and
           main_refuses(("--stacks", "--choose", "x", "--stack-depth", "1")) == (None, 1))
    expect("A1: the dry run refuses them as main() does, before the DLL is asked anything, and runs on with --choose",
           lambda: dry_refused(False, ("--names",)) == (2, []) and dry_refused(False, ("--stack-depth", "16")) == (2, [])
           and dry_refused(True, ("--stack-depth", "63")) == (2, []) and
           dry_refused(True, ("--names", "--stack-depth", "62"))[0] is None)
    for fault in ("names_wrong_func", "names_wrong_outer", "names_not_function"):
        caught((fault,), NAMES_IS, game=True, argv=names_argv)
    for fault in ("names_fn_absent", "names_offset_split"):
        caught((fault,), NAMES_AT, game=True, argv=names_argv)
    exercised.update(("names_none", "names_many"))
    expect("dry run --names: with no frame named, A1's two checks are not run (the reason given), never passed, nothing "
           "is asked, and the run fails nothing",
           lambda: (lambda r: failing(r[0]) == [] and not any(n.startswith("A1") for n in ran(r[0])) and
                    [n for n, why in r[0].skipped if why.startswith("no frame was named")] == [NAMES_IS, NAMES_AT] and
                    r[2] == [])(names_run("names_none")))
    expect("dry run --names: with no stack kept at all, A1's two checks are not run and say so ('no stack was "
           "kept'), nothing is asked, and nothing fails",
           lambda: (lambda r: failing(r[0]) == [] and not any(n.startswith("A1") for n in ran(r[0])) and
                    [n for n, why in r[0].skipped if "no stack was kept" in why] == [NAMES_IS, NAMES_AT] and
                    r[2] == [])(names_run("only_empty")))

    def refuses_unknown_fault() -> bool:
        try:
            ScriptedDll("no_such_fault")
        except ValueError:
            return True
        return False
    expect("the scripted DLL refuses a fault it does not have", refuses_unknown_fault)
    expect("dry run: --stacks --choose on a scripted game chooses every keyed function and records the cost",
           lambda: (lambda r: failing(r[0]) == [] and len(r[1]["chosen"]) == len(ScriptedDll.FUNCS) and
                    r[1]["stack_cost"]["census"].get("slots") == 3 * len(ScriptedDll.FUNCS) and
                    len(r[0].records) == 6)(dry_run(ScriptedDll(), game=True)))

    # The budgets each run sends: the fixture run its 8.1 values, a game run the DLL's own defaults (8.3 measures
    # those) unless a budget is given on the command line.
    def stack_budgets_sent(game: bool, argv: tuple[str, ...] = ()) -> dict:
        dll = ScriptedDll()
        dry_run(dll, game=game, argv=argv)
        st = next(t["snapshots"]["stacks"] for t in dll.starts if t and "stacks" in t.get("snapshots", {}))
        return {k: st[k] for k in ("per_ring_per_s", "total_per_s") if k in st}
    expect("dry run: the fixture run sends 30/s a function and 200/s in all when no budget is given",
           lambda: stack_budgets_sent(False) == {"per_ring_per_s": 30, "total_per_s": 200})
    expect("dry run: the fixture run sends a budget that is given",
           lambda: stack_budgets_sent(False, ("--stack-per-ring", "12", "--stack-total", "150")) ==
           {"per_ring_per_s": 12, "total_per_s": 150})
    expect("dry run (8.3): --stacks --choose leaves both budgets to the DLL's defaults when none is given",
           lambda: stack_budgets_sent(True) == {})
    expect("dry run: --stacks --choose sends the one budget that is given, and leaves the other to the DLL",
           lambda: stack_budgets_sent(True, ("--stack-per-ring", "100")) == {"per_ring_per_s": 100})
    expect("dry run: --stacks --choose checks the total the DLL echoes, its own default when none was given",
           lambda: any(n.startswith("the total budget held: at most about 200/s")
                       for n in ran(dry_run(ScriptedDll(), game=True, argv=("--stack-per-ring", "100"))[0])))

    # [SNAPRIG-S5-RATE] The fixture run's per-function budget from the plain recording's rates: S5 needs it to bite on
    # SnapProbe_PerFrame and to cut no other stack choice. Live on 2026-10-08 the fixture ran at about 30 fps, and a
    # budget of 30 never bit.
    def pick(pf: float, call: float, given: int | None = None, total: int = FIXTURE_STACK_TOTAL) -> tuple:
        ch_ = per_frame_budget({"SnapProbe_Call": call, "SnapProbe_PerFrame": pf}, given, total)
        return ch_["per"], ch_["runs"]
    expect("S5 rate: the old 30 is kept whenever it sits 1.5x under SnapProbe_PerFrame and 1.5x over SnapProbe_Call",
           lambda: pick(178, 8) == (30, True) and pick(45, 20) == (30, True))
    expect("S5 rate: else the budget between them, as far from both as it can be (30/s: 15; 40/s: 17; 60/s against "
           "25/s: 38)",
           lambda: pick(30, 8) == (15, True) and pick(40, 8) == (17, True) and pick(60, 25) == (38, True))
    expect("S5 rate: when none fits the old 30 is sent and S5 cannot run, the measured rates in the reason",
           lambda: (lambda c_: (c_["per"], c_["runs"], c_["given"]) == (30, False, False) and "25.0/s" in c_["why"] and
                    "30.0/s" in c_["why"])(
               per_frame_budget({"SnapProbe_Call": 25, "SnapProbe_PerFrame": 30}, None, FIXTURE_STACK_TOTAL)) and
           pick(0, 0) == (30, False) and pick(7.875, 3.5) == (30, False))
    expect("S5 rate: a given budget is sent as given; S5 runs only when SnapProbe_PerFrame is 1.5x above it",
           lambda: pick(30, 8, given=20) == (20, True) and pick(30, 8, given=21) == (21, False) and
           pick(30, 8, given=30) == (30, False) and pick(178, 8, given=100) == (100, True) and per_frame_budget(
               {"SnapProbe_Call": 8, "SnapProbe_PerFrame": 30}, 20, FIXTURE_STACK_TOTAL)["given"] is True)
    # The review's LOW-2: the total is one total for every stack choice, admitted in call order each second, so a total
    # that leaves the others no room beside SnapProbe_PerFrame's share may starve SnapProbe_Call on a correct DLL.
    expect("S5 rate: a total under SnapProbe_PerFrame's share plus 1.5x the others' rates starves them: said in why, "
           "and S5 not run (SnapProbe_PerFrame's share of a shared total is not the budget)",
           lambda: (lambda c_: (c_["per"], c_["runs"], c_["starves"]) == (40, False, True) and "starve" in c_["why"])(
               per_frame_budget({"SnapProbe_Call": 8, "SnapProbe_PerFrame": 30}, 40, 15)) and
           per_frame_budget({"SnapProbe_Call": 8, "SnapProbe_PerFrame": 60}, 30, 42)["starves"] is False and
           per_frame_budget({"SnapProbe_Call": 8, "SnapProbe_PerFrame": 60}, 30, 41)["starves"] is True)
    expect("S5 rate: a chosen budget leaves the others their room in the total (60/s against 8/s in 35: between 12 and "
           "23, so 16, not 30), and none fits when the total has no room at all",
           lambda: pick(60, 8, total=35) == (16, True) and pick(60, 2, total=40) == (30, True) and
           pick(60, 8, total=20) == (30, False) and
           per_frame_budget({"SnapProbe_Call": 8, "SnapProbe_PerFrame": 60}, None, 35)["starves"] is False)
    # The starve rule's two terms, pinned (the second review's T2 / T3): SnapProbe_PerFrame keeps no more than it is
    # called, so a budget above its rate leaves the rest of the total free; and the room is every other choice's
    # rate, summed, not the busiest one's.
    expect("S5 rate: a budget of 100 over a probe at 60 a second leaves 60 + 1.5 x 2 in a total of 80: nothing starves",
           lambda: per_frame_budget({"SnapProbe_Call": 2, "SnapProbe_PerFrame": 60}, 100, 80)["starves"] is False)
    expect("S5 rate: two other choices at 8 a second need 1.5 x 16 beside 30 (54): a total of 50 starves them",
           lambda: per_frame_budget({"A": 8, "B": 8, "SnapProbe_PerFrame": 60}, 30, 50)["starves"] is True)
    expect("S5 rate: when no budget fits, the total may still starve the others (5 a second against 60 and 2)",
           lambda: per_frame_budget({"SnapProbe_Call": 2, "SnapProbe_PerFrame": 60}, None, 5)["starves"] is True)
    # The main-rate rule holds at exactly 1.5x, as bites() does (the second review's P1).
    expect("S5 rate: the main recording at exactly 1.5x the budget lets the check run; just under it does not",
           lambda: main_rate_problem(45.0, 30, 60.0) is None and main_rate_problem(44.9, 30, 60.0) is not None)
    # S3's stand-down from the main recording: measured refusals, every one, and a total that explains them.
    refused_in = [(k, 0, CALL, 0, 1, F_STACK_BUDGET) for k in range(4)]
    plain_rates = {"SnapProbe_Call": 2.0, "SnapProbe_PerFrame": 60.0}
    expect("S3 starve: every in-scope entry refused (64), none kept, and a total that starves them stands the checks "
           "down, naming the rate that explains it",
           lambda: "the plain recording's 60.0/s" in (starved_in_scope(refused_in, 0, plain_rates, None, 30, 5) or "")
           and "the main recording's 60.0/s" in (starved_in_scope(
               refused_in, 0, dict(plain_rates, SnapProbe_PerFrame=4.0), 60.0, 100, 10) or ""))
    expect("S3 starve: a stack kept, an entry not refused, no entry, or a total that explains nothing runs the checks",
           lambda: starved_in_scope(refused_in, 1, plain_rates, 60.0, 30, 5) is None and
           starved_in_scope(refused_in[:3] + [(3, 0, CALL, 0, 1, F_STACK_TAKEN)], 0, plain_rates, 60.0, 30, 5) is None
           and starved_in_scope([], 0, plain_rates, 60.0, 30, 5) is None and
           starved_in_scope(refused_in, 0, plain_rates, 60.0, 30, 200) is None and
           starved_in_scope(refused_in, 0, dict(plain_rates, SnapProbe_PerFrame=4.0), None, 100, 10) is None)

    def s5_run(pf_rate: float = ScriptedDll.PF_RATE, argv: tuple[str, ...] = (), faults: tuple[str, ...] = (),
               main_pf_rate: float | None = None) -> tuple[Checks, dict, int | None]:
        """The fixture run at a probe rate: its checks, its out, and the per-function budget its Start sent."""
        dll = ScriptedDll(*faults, pf_rate=pf_rate, main_pf_rate=main_pf_rate)
        ch, out = dry_run(dll, argv=argv)
        sent = next((t["snapshots"]["stacks"] for t in dll.starts if t and "stacks" in t.get("snapshots", {})), {})
        return ch, out, sent.get("per_ring_per_s")

    def s5_ran(ch: Checks) -> list[str]:
        return [n for n in ran(ch) if n.startswith(S5_WINDOW)]

    def s5_skipped(ch: Checks) -> list[str]:
        return [why for n, why in ch.skipped if n.startswith(S5_WINDOW)]

    def s5_params(ch: Checks) -> tuple[list[bool], list[str]]:
        """S5's parameter-counter check: its verdicts where it ran, its reasons where it was not run."""
        return [ok for n, ok, _ in ch.items if n == S5_PARAMS], [why for n, why in ch.skipped if n == S5_PARAMS]

    def s5_vacuous(ch: Checks) -> bool:
        """S5's two lines both reported not run, the counters' because the stack budget refused nothing: there a DLL
        that counts its refusals in the wrong place has nothing to count, and 0 proves nothing."""
        oks, whys = s5_params(ch)
        return s5_ran(ch) == [] and len(s5_skipped(ch)) == 1 and oks == [] and len(whys) == 1 and \
            "refused nothing" in whys[0]
    lo7, hi7 = budget_window(7, DRY_RECORD_S, DRY_RECORD_S)
    expect(f"dry run: a probe at 30 a second (the fixture at 30 fps), no --stack-per-ring: the budget is lowered to 7 "
           f"so it bites, and S5 holds at {lo7:.0f}..{hi7:.0f}",
           lambda: (lambda r: failing(r[0]) == [] and r[2] == 7 and r[1]["stack_budget"]["per"] == 7 and
                    len(s5_ran(r[0])) == 1 and f"({lo7:.0f}..{hi7:.0f})" in s5_ran(r[0])[0])(s5_run(30)))
    # 25, not the 30 the run falls back to, so a run that sends its fallback in place of a given budget is seen; and the
    # ring drops some calls at it, so a run that runs S5 whenever the ring dropped something is seen too (LOW-4).
    expect("dry run: --stack-per-ring 25 with the probe at 30 a second cannot bite (25 x 1.5 > 30), though the ring "
           "drops some: S5's window not run (the rate in the reason), neither failed nor passed, 25 still sent; the "
           "budget refused calls, so the parameter counters are checked, and hold",
           lambda: (lambda r: failing(r[0]) == [] and r[2] == 25 and s5_ran(r[0]) == [] and
                    int_or(r[1]["stack_rings"][1].get("dropped_budget"), 0) > 0 and
                    len(s5_skipped(r[0])) == 1 and "30.0/s" in s5_skipped(r[0])[0] and
                    s5_params(r[0]) == ([True], []))(s5_run(30, ("--stack-per-ring", "25"))))
    # [SNAPRIG-S5-RATE] The second review's MED-A: a nonzero parameter counter is a defect whether or not the window
    # runs, and the stack budget refuses calls on paths where the window cannot: those refusals are what a DLL counts
    # in the wrong place.
    expect("dry run: the same with a DLL that counts the ring's refusals as parameter skips: the counter check fails, "
           "and nothing else does",
           lambda: (lambda r: fail_set(r[0], S5_PARAMS) and s5_ran(r[0]) == [])(
               s5_run(30, ("--stack-per-ring", "25"), faults=("snap_skipped",))))
    expect("dry run: no budget fits (the probe at 4 a second): S5's window not run with the measured rates, the "
           "counters not run as the budget refused nothing, every other check run at the old 30",
           lambda: (lambda r: failing(r[0]) == [] and r[2] == 30 and s5_ran(r[0]) == [] and
                    len(s5_skipped(r[0])) == 1 and "4.0/s" in s5_skipped(r[0])[0] and "2.0/s" in s5_skipped(r[0])[0]
                    and s5_vacuous(r[0]) and len(ran(r[0])) == len(fixture_names) + 24)(s5_run(4)))
    expect("dry run: where the stack budget refuses nothing, a DLL that would count its refusals in the parameter "
           "counters shows nothing: the counter check is not run, never passed",
           lambda: all(failing(r[0]) == [] and s5_vacuous(r[0])
                       for r in (s5_run(4, faults=("snap_skipped",)), s5_run(4, faults=("snap_counted",)))))
    expect("dry run: where the stack budget refuses nothing, a parameter counter that moves anyway fails the check",
           lambda: (lambda r: fail_set(r[0], S5_PARAMS) and s5_ran(r[0]) == [])(s5_run(4, faults=("snap_phantom",))))
    # Whether anything was refused has two detectors beside the parameter counters, and each must see a refusal the
    # other cannot: a trace.stack that counts no drop leaves the main table's count over the ring's written, and a
    # main table without the probe's row leaves trace.stack's drops.
    expect("dry run: --stack-per-ring 25 at 30 a second with trace.stack counting no drop: the main table still shows "
           "the refusals, so the counters are checked, and hold",
           lambda: (lambda r: failing(r[0]) == [] and s5_params(r[0]) == ([True], []))(
               s5_run(30, ("--stack-per-ring", "25"), faults=("stack_dropped0",))))
    expect("dry run: --stack-per-ring 25 at 30 a second with no SnapProbe_PerFrame row in the main table: trace.stack's "
           "drops still show the refusals, so the counters are checked, and hold",
           lambda: (lambda r: fail_set(r[0], f"S5 {MAIN_TABLE}") and s5_params(r[0]) == ([True], []))(
               s5_run(30, ("--stack-per-ring", "25"), faults=("main_rows_lost",))))
    # The plain recording chooses the budget; the main recording is the one S5 reads, and its own rate decides whether
    # the budget bit there (the review's LOW-1).
    expect("dry run: the plain recording at 60 a second, the main one at 30 (under 1.5x the 30 sent): S5's window not "
           "run, both rates in the reason, the counters not run as nothing was refused, nothing failed",
           lambda: (lambda r: failing(r[0]) == [] and r[2] == 30 and s5_vacuous(r[0]) and
                    "60.0/s" in s5_skipped(r[0])[0] and "30.0/s" in s5_skipped(r[0])[0])(
               s5_run(60, main_pf_rate=30)))
    expect("dry run: the main recording slower than the plain one but still 1.5x over the budget: S5 runs and holds",
           lambda: (lambda r: failing(r[0]) == [] and r[2] == 30 and len(s5_ran(r[0])) == 1 and
                    S5_PARAMS in ran(r[0]))(s5_run(60, main_pf_rate=46)))
    # The second review's LOW: a reply that cannot give the main rate -- an error, no window, no row for the probe --
    # is not a slow probe. Its own check fails, and S5's window runs from the trace ring, as it did before the main
    # rate was read, rather than standing down at "0.0/s".
    s5_main, main_faults = f"S5 {MAIN_TABLE}", ("main_get_error", "main_window0", "main_rows_lost")
    for fault in main_faults:
        caught((fault,), s5_main)
    expect("dry run: a main table without SnapProbe_PerFrame's rate fails its own check, and S5's window still runs "
           "and holds, never stood down",
           lambda: all((lambda r: fail_set(r[0], s5_main) and len(s5_ran(r[0])) == 1 and s5_skipped(r[0]) == [])(
               s5_run(faults=(fault,))) for fault in main_faults))
    # The round-3 review's LOW: a main table can give a rate that is plausible and wrong -- a count too low, a window
    # too long -- under 1.5x the 30 sent though the probe ran at 60 a second. The probe's own ring counts every lone
    # call it wrote or dropped, so its rate is never above the truth, and it shows the budget bit.
    under_faults = ("main_pf_under", "main_window_long")
    exercised.update(under_faults)
    expect("dry run: a main table whose SnapProbe_PerFrame rate is a tenth or a third of the truth (its count, or its "
           "window three times too long): the probe's own ring shows the rate, so S5's window still runs and holds, "
           "nothing stood down and nothing failed",
           lambda: all((lambda r: failing(r[0]) == [] and len(s5_ran(r[0])) == 1 and s5_skipped(r[0]) == [] and
                        r[1]["stack_budget"]["main_rate"] < BUDGET_MARGIN * 30 and
                        r[1]["stack_budget"]["main_rate_ring"] == 60.0)(s5_run(faults=(fault,)))
                       for fault in under_faults))
    s3_in_scope = ("S3 every in-scope stack holds an own frame", S3_KNOWN_IN)

    def s3_starved(ch: Checks, *words: str) -> bool:
        """S3's two in-scope checks both reported not run, never run, each reason holding every word."""
        return [p for p in s3_in_scope if any(n.startswith(p) and all(w in why for w in words)
                                              for n, why in ch.skipped)] == list(s3_in_scope) and \
            not any(n.startswith(s3_in_scope) for n in ran(ch))
    # The second review's LOW: the total is one total, admitted in call order each second, so what SnapProbe_PerFrame
    # has spent of it grows through the second, and SnapProbe_Call called early in a second still keeps its stack.
    # Whether the total starves the in-scope stacks is the main recording's to say, not the plain rates' prediction.
    expect("dry run: --stack-per-ring 40 --stack-total 15 with the probe at 30 a second: the total, predicted to "
           "starve (said, S5's window not run on it), is spent by mid-second, so each second's first round keeps its "
           "stacks and the later one is refused, booked as skipped; S3's two in-scope checks run over the kept stacks "
           "and hold, the counters are checked, nothing fails",
           lambda: (lambda r: failing(r[0]) == [] and r[2] == 40 and r[1]["stack_budget"]["starves"] is True and
                    (r[1]["stack_rings"][0].get("written"), r[1]["stack_rings"][0].get("skipped_budget"),
                     r[1]["stack_rings"][0].get("dropped_budget")) == (4, 4, 0) and
                    s5_ran(r[0]) == [] and "starve" in s5_skipped(r[0])[0] and s5_params(r[0]) == ([True], []) and
                    all(any(n.startswith(p) and ok and g.startswith("2 of 2") for n, ok, g in r[0].items)
                        for p in s3_in_scope))(
               s5_run(30, ("--stack-per-ring", "40", "--stack-total", "15"))))
    # Without a given budget (the second review's T4 / T5): none fits, the fallback is sent, and SnapProbe_PerFrame
    # spends the total before every round, so every in-scope stack is refused and the checks stand down on that.
    expect("dry run: --stack-total 5 with no --stack-per-ring and the probe at 60 a second: no budget fits, the total "
           "is predicted to starve the others and does, every in-scope SnapProbe_Call entry flagged 64; S3's two "
           "in-scope checks not run with the refusals and the starving total the reason, nothing failed",
           lambda: (lambda r: failing(r[0]) == [] and r[1]["stack_budget"]["starves"] is True and
                    r[1]["stack_budget"]["given"] is False and
                    (r[1]["stack_rings"][0].get("written"), r[1]["stack_rings"][0].get("skipped_budget")) == (0, 8) and
                    s3_starved(r[0], "starve", "4 in-scope", "flagged 64"))(s5_run(60, ("--stack-total", "5"))))
    # Refusals the plain rates did not predict, where the main recording ran SnapProbe_PerFrame fast enough to spend
    # the total first: the main rate explains them.
    expect("dry run: --stack-per-ring 100 --stack-total 10, the probe at 4 a second in the plain recording and 60 in "
           "the main one: every in-scope stack refused, which only the main rate explains; S3's two in-scope checks "
           "not run, that rate the reason, nothing failed",
           lambda: (lambda r: failing(r[0]) == [] and r[1]["stack_budget"]["starves"] is False and
                    s3_starved(r[0], "starve", "the main recording's 60.0/s"))(
               s5_run(4, ("--stack-per-ring", "100", "--stack-total", "10"), main_pf_rate=60)))
    # The same with a table that counts a tenth of the probe's calls (6 a second, under which a total of 10 starves
    # nothing): its ring shows the 60, and that explains the refusals as the table would have.
    expect("dry run: the same with a main table that counts a tenth of SnapProbe_PerFrame's calls: its ring shows the "
           "60 a second, S3's two in-scope checks not run, that rate the reason, nothing failed",
           lambda: (lambda r: failing(r[0]) == [] and r[1]["stack_budget"]["main_rate"] == 6.0 and
                    s3_starved(r[0], "starve", "the main recording's 60.0/s"))(
               s5_run(4, ("--stack-per-ring", "100", "--stack-total", "10"), faults=("main_pf_under",),
                      main_pf_rate=60)))
    # Refusals no total explains are the DLL's: the checks run, and fail on the stacks it did not keep.
    caught(("call_refused",), *s3_in_scope)
    # The round-3 review's LOW: where some in-scope stacks were kept, the checks ran over those alone, and a stack
    # refused with both budgets' room left -- the per-function budget 1.5x above SnapProbe_Call's rate, a total that
    # starves nothing -- passed unjudged.
    caught(("call_refused_some",), *s3_in_scope)
    expect("dry run: the default rates keep the old 30, and out says it was chosen, not given",
           lambda: (lambda r: r[2] == 30 and (r[1]["stack_budget"]["per"], r[1]["stack_budget"]["given"],
                                              r[1]["stack_budget"]["runs"]) == (30, False, True))(s5_run()))
    lo12, hi12 = budget_window(12, DRY_RECORD_S, DRY_RECORD_S)
    expect(f"dry run: a given --stack-per-ring that bites is honoured, S5 holding at its window {lo12:.0f}..{hi12:.0f}",
           lambda: (lambda r: failing(r[0]) == [] and r[2] == 12 and r[1]["stack_budget"]["given"] is True and
                    len(s5_ran(r[0])) == 1 and f"({lo12:.0f}..{hi12:.0f})" in s5_ran(r[0])[0])(
               s5_run(argv=("--stack-per-ring", "12"))))

    # [SNAPRIG-STEP2-RATE] The step-2 run (run_full) holds SnapProbe_PerFrame's parameter ring to a budget and checks
    # it dropped calls over it: the same precondition as S5's, which a fixture at about 30 fps does not meet at 30.
    def step2_run(pf_rate: float = ScriptedDll.PF_RATE, main_pf_rate: float | None = None,
                  faults: tuple[str, ...] = ()) -> tuple[Checks, dict, int | None]:
        """The step-2 run at a probe rate: its checks, its out, and the parameter budget its main Start sent."""
        dll = ScriptedDll(*faults, pf_rate=pf_rate, main_pf_rate=main_pf_rate, late=True)
        ch, out = dry_run(dll, stacks=False)
        main = next((t for t in dll.starts if t and t.get("ticked_names") and (t.get("snapshots") or {}).get("funcs")),
                    {})
        return ch, out, (main.get("snapshots") or {}).get("per_ring_per_s")

    def step2_line(ch: Checks) -> tuple[list[bool], list[str]]:
        """The budget check's verdicts where it ran, and its reasons where it was not run."""
        return ([ok for n, ok, _ in ch.items if n.startswith(STEP2_BUDGET)],
                [why for n, why in ch.skipped if n.startswith(STEP2_BUDGET)])
    expect("dry run step 2: the probe at 60 a second, the old 30 sent, and the budget check holds",
           lambda: (lambda r: r[2] == 30 and step2_line(r[0]) == ([True], []))(step2_run()))
    expect("dry run step 2: the probe at 30 a second (the fixture at 30 fps): the budget is lowered to 7 so it bites, "
           "and the check holds",
           lambda: (lambda r: r[2] == 7 and step2_line(r[0]) == ([True], []))(step2_run(30)))
    expect("dry run step 2: no budget fits (the probe at 4 a second): the old 30 sent, the check not run with the "
           "measured rates, never failed",
           lambda: (lambda r: r[2] == 30 and step2_line(r[0])[0] == [] and len(step2_line(r[0])[1]) == 1 and
                    "4.0/s" in step2_line(r[0])[1][0] and "2.0/s" in step2_line(r[0])[1][0])(step2_run(4)))
    expect("dry run step 2: the plain recording at 60 a second, the main one at 30: the check not run, both rates in "
           "the reason",
           lambda: (lambda r: r[2] == 30 and step2_line(r[0])[0] == [] and len(step2_line(r[0])[1]) == 1 and
                    "60.0/s" in step2_line(r[0])[1][0] and "30.0/s" in step2_line(r[0])[1][0])(
               step2_run(60, main_pf_rate=30)))
    # The same for the step-2 run. Its other checks fail on the scripted DLL (the class docstring), so these controls
    # name the lines they read.
    step2_main = f"the budget: {MAIN_TABLE}"
    expect("dry run step 2: a whole main table passes its own check",
           lambda: (lambda ch: step2_main in ran(ch) and step2_main not in failing(ch))(step2_run()[0]))
    expect("dry run step 2: a main table without SnapProbe_PerFrame's rate fails its own check, and the budget check "
           "still runs and holds",
           lambda: all((lambda ch: step2_main in failing(ch) and step2_line(ch) == ([True], []))(
               step2_run(faults=(fault,))[0]) for fault in main_faults))
    expect("dry run step 2: a main table whose SnapProbe_PerFrame rate is a tenth or a third of the truth: the "
           "parameter ring shows the rate, so the budget check still runs and holds",
           lambda: all(step2_line(step2_run(faults=(fault,))[0]) == ([True], []) for fault in under_faults))
    exercised.update(("param_dropped0", "param_overkept"))
    expect("dry run step 2: a parameter ring that counts no drop fails the budget check",
           lambda: step2_line(step2_run(faults=("param_dropped0",))[0]) == ([False], []))
    expect("dry run step 2: at 7 a second, a ring that keeps 25 a second fails the check (what it keeps is bounded by "
           "the budget sent, not by 30)",
           lambda: step2_line(step2_run(30, faults=("param_overkept",))[0]) == ([False], []))

    # The bookkeeping last, so it counts every control above.
    expect("every scripted fault has a control", lambda: exercised == set(ScriptedDll.FAULTS))
    expect("every check the good runs make has a fault that fails it",
           lambda: (lambda names: names != [] and [n for n in names if not any(n.startswith(p) for p in caught_by)]
                    == [])(ran(dry_run(ScriptedDll())[0]) + ran(dry_run(ScriptedDll(), game=True)[0]) +
                           ran(dry_run(ScriptedDll(), argv=("--pdb",))[0])))
    expect("every check the --names run makes has a fault that fails it, A1's two among them",
           lambda: (lambda names: NAMES_IS in names and NAMES_AT in names and
                    [n for n in names if not any(n.startswith(p) for p in caught_by)] == [])(
               ran(dry_run(ScriptedDll(), game=True, argv=names_argv)[0])))

    failed = [r for r in results if not r[1]]
    for name, _, why in failed:
        say(f"  FAIL  {name}" + (f"   ({why})" if why else ""))
    say(f"self-test: {len(results) - len(failed)}/{len(results)} controls hold")
    return 1 if failed or not results else 0


if __name__ == "__main__":
    sys.exit(main())
