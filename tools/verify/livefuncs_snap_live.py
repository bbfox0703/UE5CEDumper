r"""Live check of Live Funcs steps 2 and 3 -- parameter snapshots, native stacks, functions by name -- through the pipe.

    py tools/verify/livefuncs_snap_live.py --fixture-check
    py tools/verify/livefuncs_snap_live.py --label <run> [--record-s 8]
    py tools/verify/livefuncs_snap_live.py --label avowed --choose Inventory --plain-s 20 --record-s 30
    py tools/verify/livefuncs_snap_live.py --stacks [--stack-per-ring 30] [--stack-total 200]
    py tools/verify/livefuncs_snap_live.py --label avowed --stacks --choose "" --plain-s 20 --record-s 30
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

--stacks (`[LIVEFUNCS-STEP3]`, docs/live-funcs-step3-items.md, "8. Live checks") runs step 3's checks instead, on
the same fixture: SnapNest_Outer ticked by name, SnapProbe_Call chosen for its parameters, SnapProbe_Call and
SnapProbe_PerFrame chosen for a native stack (depth 16, --stack-per-ring / --stack-total a second), recorded
--record-s; then an altered stack key alone, and a stacks-only Start. The wire is the design's section 3
(docs/live-funcs-step3-design.md). Each check is named after the ledger's:
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
      chained fragment to ProcessEvent's start (the review's M4)
  S4  recorded, not failed: an in-scope frame whose fn is SnapNest_Outer's code_addr, before the ProcessEvent frame;
      a miss is the tail-call case
  S5  SnapProbe_PerFrame's stack ring keeps about --stack-per-ring a second and drops the rest; the parameter
      counters stay 0
  S6  recorded: mean and max microseconds a capture, captures a second, calls/s with and without stacks, the CPU,
      and D3's re-weighed total
  S7  each release frees everything. Re-running the default checks and livefuncs_trace_live.py on the same DLL is a
      separate invocation (cut 4, which the review's H1 took)
No red run on a DLL without step 3 (H1): it sends no names.stacks, so S0 fails by construction and S1-S6 cannot run.
--stacks --choose is the design's 8.3 on a real game: the busiest named functions whose class or name holds one of
the substrings ("" for any), chosen for stacks alone at the DLL's default budgets (a budget given on the command line
is sent instead); it reports their cost. --self-test runs the pure pieces
against hand-made replies, then both --stacks runs against a scripted DLL (ScriptedDll), each beside a control that
must fail: no pipe, no game.

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

    def __call__(self, name: str, cond: bool, got: str = "") -> bool:
        self.items.append((name, bool(cond), got))
        say(f"  {'ok  ' if cond else 'FAIL'}  {name}" + (f"   ({got})" if got else ""))
        return bool(cond)

    def not_run(self, name: str, why: str) -> None:
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
                    help=f"--stacks: the per-function stack budget a second ({FIXTURE_STACK_PER_RING} on the fixture; "
                         "with --choose, the DLL's own default unless given)")
    ap.add_argument("--stack-total", type=int, default=None,
                    help=f"--stacks: the stack budget a second, every stack choice together ({FIXTURE_STACK_TOTAL} on "
                         "the fixture; with --choose, the DLL's own default unless given)")
    ap.add_argument("--self-test", action="store_true",
                    help="run the --stacks helpers against hand-made replies and the --stacks runs against a "
                         "scripted DLL; needs no pipe and no game")
    return ap


def main() -> int:
    args = build_parser().parse_args()
    if args.self_test:
        return self_test()

    check = Checks()
    out: dict = {"label": args.label}
    try:
        c = PipeClient().connect()
    except PipeError as e:
        say(f"pipe not usable: {e}")
        return 2
    try:
        out["build"] = c.assert_build()
        say(f"DLL build {out['build']}")
        if args.choose is not None:
            (run_game_stacks if args.stacks else run_game)(c, check, out, args)
        else:
            say("fixture:")
            out["fixture"] = fixture_check(c, check, args.plain_s)
            if out["fixture"]["total_calls"] == 0:
                say("no calls recorded: is the game running, scanned, and the hook up?")
                return 2
            if not args.fixture_check:
                if args.stacks:
                    run_stacks(c, check, out, args, out["fixture"]["probes"])
                else:
                    run_full(c, check, out, args)
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
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    suffix = "-fixture" if args.fixture_check else "-stacks" if args.stacks else ""
    path = OUT_DIR / f"{args.label}{suffix}.json"
    path.write_text(json.dumps(out, indent=1, default=str), encoding="utf-8")
    rec = f", {len(check.records)} recorded (not failed)" if check.records else ""
    say(f"\n{len(check.items) - check.failed}/{len(check.items)} checks hold{rec}; written to {path}")
    return 1 if check.failed else 0


def run_full(c: PipeClient, check: Checks, out: dict, args) -> None:
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
    time.sleep(1.0)
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
    trace = {"bytes": 64 << 20, "ticked_names": [item(rows["SnapNest_Outer"])],
             "snapshots": {"funcs": [item(rows[n]) for n in chosen], "bytes": snap_bytes, "per_ring_per_s": 30}}
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
    time.sleep(args.record_s)
    invoke_late(c, 4242)
    t0 = time.perf_counter()
    stop = data_of(c.request("pe_profile_stop"))
    stop_s = time.perf_counter() - t0
    out["stop"] = stop
    check("F4 Stop returns within about 2.5 s", stop_s < 2.5, f"{stop_s:.2f} s")
    gen = stop.get("trace", {}).get("gen", 0)

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
    check("the budget: the per-frame probe's lone calls over 30/s are dropped, the first ones kept",
          pf_ring.get("dropped_budget", 0) > 0 and 0 < pf_ring.get("written", 0) <= 31 * (args.record_s + 3),
          f"written {pf_ring.get('written')}, dropped {pf_ring.get('dropped_budget')}")

    # ---- F6: code_addr.
    say("\nF6 -- code_addr:")
    pid = int(HOST_PID.read_text().strip()) if HOST_PID.exists() else 0
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
S3_KNOWN_IN = 'S3 every in-scope stack holds known:"process_event" before its own frame'
S3_KNOWN_LONE = 'S3 no lone stack holds known:"process_event"'
S3_KNOWN_ONE = "S3 every known frame names one function, and no stack holds two"
S5_WINDOW = "S5 SnapProbe_PerFrame's stack ring keeps about"
# The Start's stack list, in order: the DLL numbers stack rings by the accepted items' order, and S1's join checks
# that every slot of ring s belongs to the s-th name.
STACK_CHOICES = ("SnapProbe_Call", "SnapProbe_PerFrame")
STACKS_ONLY_S = 3.0       # the stacks-only recording: SnapProbe_Call runs about four times a second
# The fixture run's budgets when none is given (the ledger's 8.1): 30/s sits far below SnapProbe_PerFrame's rate, so
# S5 sees the budget drop calls. A game run (8.3) sends none unless given: it measures the DLL's own defaults.
FIXTURE_STACK_PER_RING, FIXTURE_STACK_TOTAL = 30, 200


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
    """One `sites` entry with its hex strings as integers. An absent field is None; an absent module is ""."""
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
            "known": s.get("known") or ""}


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
               clock=time.perf_counter) -> None:
    """--stacks on DumperTest58: S0-S7 (the module docstring). `rows` are the fixture check's rows, by name. `pid`
    (out/host.pid when None) names the game process for frame 0's module; --self-test passes 0, a scripted DLL as
    `c`, and a fake clock that only its sleep moves."""
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
    per_sent = FIXTURE_STACK_PER_RING if args.stack_per_ring is None else args.stack_per_ring
    total_sent = FIXTURE_STACK_TOTAL if args.stack_total is None else args.stack_total
    per, total = budget_echo(per_sent), budget_echo(total_sent)
    say(f"\ngame module {game_module} ({'pid %d' % pid if named else 'the fixture name: no usable out/host.pid'}); "
        f"CPU {out['machine']['cpu'] or '?'}; stack budgets {per}/s a function, {total}/s in all, depth {STACK_DEPTH}")
    snap_bytes = 32 << 20
    stacks = {"funcs": [stack_item(rows[n]) for n in STACK_CHOICES], "depth": STACK_DEPTH,
              "per_ring_per_s": per_sent, "total_per_s": total_sent}

    # ---- S0: the main Start. SnapProbe_Call's parameter budget is far above its four calls a second, so a nonzero
    # parameter counter at S5 can only be a stack refusal counted in the wrong place.
    say("\nS0 -- the main Start echoes the stack choices:")
    trace = {"bytes": 64 << 20, "ticked_names": [item(rows["SnapNest_Outer"])],
             "snapshots": {"bytes": snap_bytes, "per_ring_per_s": 1000, "funcs": [item(rows["SnapProbe_Call"])],
                           "stacks": stacks}}
    t0 = time.perf_counter()
    start = c.request("pe_profile_start", trace=trace)
    t1 = time.perf_counter()
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
    t2 = time.perf_counter()
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
    table = data_of(c.request("pe_profile_get", limit=32768, include_unloaded=True))

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
          j["taken"] > 0 and j["slots"] > 0 and not j["no_slot"] and not j["two_slots"] and not j["stray"],
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
    check("S3 every in-scope stack holds an own frame (the hook under SnapNest_Outer's ProcessEvent)",
          in_scope != [] and all(o is not None for _, o in ns_in),
          f"{sum(1 for _, o in ns_in if o is not None)} of {len(in_scope)}")
    check("S3 no lone stack holds an own frame", lone != [] and all(o is None for _, o in ns_lone),
          f"{sum(1 for _, o in ns_lone if o is not None)} of {len(lone)} do")
    # The known halves are checks since S3-M3: DescribeCode follows a chained fragment to its primary function, so a
    # ProcessEvent call site in a hot/cold or shrink-wrapped fragment is still labelled (the review's M4).
    with_known = sum(1 for n in ns_in if known_before_own(n))
    check(S3_KNOWN_IN, in_scope != [] and with_known == len(in_scope), f"{with_known} of {len(in_scope)}")
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
    win = table.get("window_ms", 0) / 1000.0
    stack2, snap2 = st2.get("stack", {}), st2.get("snap", {})
    say(f"     SnapProbe_PerFrame: about {pf_row.get('count', 0) / win if win else 0:.0f} calls/s; its stack ring wrote "
        f"{pf_ring.get('written')}, skipped {pf_ring.get('skipped_budget')}, dropped {pf_ring.get('dropped_budget')}")
    check(f"S5 SnapProbe_PerFrame's stack ring keeps about {per_s}/s over {span_lo:.1f}-{span_hi:.1f} s "
          f"({lo:.0f}..{hi:.0f}) and the budget drops the rest",
          lo <= int_or(pf_ring.get("written"), -1) <= hi and int_or(pf_ring.get("dropped_budget"), 0) > 0 and
          int_or(stack2.get("dropped_budget"), 0) > 0,
          f"written {pf_ring.get('written')}, ring dropped {pf_ring.get('dropped_budget')}, "
          f"stack.dropped_budget {stack2.get('dropped_budget')}")
    check("S5 the parameter counters are untouched by the stack budget (snap skipped and dropped 0)",
          snap2.get("skipped_budget") == 0 and snap2.get("dropped_budget") == 0,
          f"skipped {snap2.get('skipped_budget')}, dropped {snap2.get('dropped_budget')}")

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
    of the substrings, chosen for stacks alone at the given budgets. What it reports is their cost. `sleep` and
    `clock` as run_stacks takes them."""
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
    stacks = {"funcs": [stack_item(f) for f in chosen], "depth": STACK_DEPTH}
    for key, given in (("per_ring_per_s", args.stack_per_ring), ("total_per_s", args.stack_total)):
        if given is not None:   # left out, the DLL applies its own default, which is what 8.3 measures
            stacks[key] = given
    say(f"\nstacks-only recording ({args.record_s:.0f} s), {len(chosen)} chosen, budgets "
        f"{json.dumps({k: v for k, v in stacks.items() if k != 'funcs'})} (the DLL's default for any left out):")
    t0 = time.perf_counter()
    start = c.request("pe_profile_start", trace={"bytes": 64 << 20, "snapshots": {"bytes": 32 << 20, "funcs": [],
                                                                                  "stacks": stacks}})
    t1 = time.perf_counter()
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
    time.sleep(args.record_s)
    t2 = time.perf_counter()
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
    if st2.get("allocated"):
        for r in read_stack_rings(c, st2.get("gen", 0), len(chosen)).values():
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


DRY_RECORD_S = 2.0        # the dry run's --record-s: S5's window at 30/s is then 30..90, both ends above zero


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
    Each fault named in FAULTS makes it answer as one broken DLL would, so a control can show the check that must
    catch that DLL failing. It proves the rig's glue, never the DLL."""
    QPC, BASE, OWN = 10_000_000, 0x7FF6A0000000, 0x7FFC12300000
    FUNCS = {"SnapNest_Outer": (0x1000, [1, 0, 9, 0], 4), "SnapProbe_Call": (0x1100, [2, 0, 9, 0], 96),
             "SnapProbe_PerFrame": (0x1200, [3, 0, 9, 0], 4)}
    OUTER_FN = 0x6000            # SnapNest_Outer's native entry, as an RVA
    PAGE = 2                     # slots a page: small, so the rig's paging runs over several pages
    # SnapProbe_PerFrame's slots in the main recording: its budget kept over the dry run's span, the middle of S5's
    # window, so a fault that keeps too few or too many falls outside it.
    PER_FRAME_KEPT = int(FIXTURE_STACK_PER_RING * DRY_RECORD_S)
    FAULTS = {
        "old": "a DLL without step 3: no names.stacks, no trace.stack, no names[].stack",
        "no_known": "no site labelled known (S3-F2's mutation: known compared with the trampoline)",
        "ignore_kind": "parameter items served to kind:\"stack\" (S3-F2's mutation)",
        "known_everywhere": "every unwound site labelled known (the compare with ProcessEvent's start dropped)",
        "known_twice": "a second known frame, of the same function, in each in-scope stack",
        "known_split": "every other in-scope stack labels a frame of another function known, not ProcessEvent's",
    }

    def __init__(self, *faults: str, per_frame: int = PER_FRAME_KEPT) -> None:
        unknown = set(faults) - set(self.FAULTS)
        if unknown:   # a misspelt fault would script the good DLL, and its control would test nothing
            raise ValueError(f"the scripted DLL has no fault {sorted(unknown)}")
        self.f = set(faults)
        self.per_frame = per_frame
        self.gen = 0
        self.t: dict | None = None
        self.cmds: list[str] = []
        self.starts: list[dict | None] = []   # each Start's trace object, as the rig sent it

    def rows(self) -> dict[str, dict]:
        """The fixture rows a plain recording gives, by name."""
        return {n: {"class_name": FIXTURE_CLASS, "func_name": n, "fname_key": k, "parms_size": ps, "num_parms": 1,
                    "count": 180 if n == "SnapProbe_PerFrame" else 6, "per_frame": n == "SnapProbe_PerFrame"}
                for n, (_, k, ps) in self.FUNCS.items()}

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
        own = {"addr": f"0x{self.OWN + 0x45678:X}", "module": "dxgi.dll", "module_base": f"0x{self.OWN:X}",
               "rva": 0x45678, "fn": f"0x{self.OWN + 0x45000:X}", "fn_rva": 0x45000, "unwind": True, "own": True}
        call = g(0x4804C9, 0x480440)
        in_scope = [call, g(self.OUTER_FN + 0x31, self.OUTER_FN), invoke, pe, own, g(0x3010, 0x3000),
                    g(0x2010, 0x2000), g(0x1010, 0x1000)]
        if "known_twice" in self.f:
            in_scope.insert(5, g(0x9456, 0x9000, **known))
        lone = [call, g(0x2020, 0x2000), g(0x1010, 0x1000), g(0x0810, 0x0800)]
        if "known_everywhere" in self.f:
            in_scope, lone = ([dict(s, known=KNOWN_PE) if s.get("unwind") else s for s in x] for x in (in_scope, lone))
        return in_scope, lone

    def _start(self, t: dict | None) -> dict:
        if t is None:
            return {"data": {"recording": True, "hook_active": True}}
        by_key = {tuple(k): n for n, (_, k, _) in self.FUNCS.items()}

        def good(it: dict) -> bool:
            keys = it.get("keys") or [[]]
            return it.get("class") == FIXTURE_CLASS and by_key.get(tuple(keys[0])) == it.get("func")
        s = t.get("snapshots") or {}
        st = s.get("stacks") if isinstance(s.get("stacks"), dict) and "old" not in self.f else None
        asked = t.get("ticked_names", []) + s.get("funcs", []) + (st or {}).get("funcs", [])
        ticks = [i for i in t.get("ticked_names", []) if good(i)]
        params = [i["func"] for i in s.get("funcs", []) if good(i)]
        stacks = [i["func"] for i in (st or {}).get("funcs", []) if good(i)]
        if (not ticks and not params and not stacks) or (t.get("ticked_names") and not ticks):
            return {"error": "None of the chosen functions is known by that name in this game any more."}
        self.gen += 1
        ring_of = {n: k for k, n in enumerate(stacks)}
        lone = self._stacks()[1]
        recs: list[bytes] = []
        slots: list[list[dict]] = [[] for _ in stacks]
        dropped = [0] * len(stacks)

        def call(func: str, flags: int, frames: list[dict], inner=None) -> None:
            seq = len(recs)
            flags |= (F_TAKEN if func in params else 0) | (F_STACK_TAKEN if func in ring_of else 0)
            recs.append(REC.pack(seq, seq * 10, self.FUNCS[func][0], 0x5000, 1, flags))
            if func in ring_of:
                slots[ring_of[func]].append({"entry_seq": seq, "frames": frames})
            if inner:
                inner()
            r = len(recs)
            recs.append(REC.pack(r | RET_BIT, r * 10, seq, 0, 1, 0))
        if ticks:      # the main recording: scoped by SnapNest_Outer
            for rnd in range(4):
                in_scope = self._stacks(rnd)[0]
                call("SnapNest_Outer", F_ROOT, [], lambda fr=in_scope: call("SnapProbe_Call", 0, fr))
                call("SnapProbe_Call", F_LONE, lone)
            if "SnapProbe_PerFrame" in ring_of:
                for _ in range(self.per_frame):
                    call("SnapProbe_PerFrame", F_LONE, lone[:3])
                dropped[ring_of["SnapProbe_PerFrame"]] = 25
        else:          # stacks only: every chosen call is lone
            for n in stacks:
                for _ in range(3):
                    call(n, F_LONE, lone)
        depth = min(max(int((st or {}).get("depth", 16)), 1), 62)
        caps = [ring_cap(self.FUNCS[n][2]) for n in params]
        per_round = sum(24 + c for c in caps) + len(stacks) * (24 + 8 * depth)
        sb = s.get("bytes", 0)
        clamp = lambda v: min(max(int(v), 1), 0xFFFFFF)
        captures = sum(len(x) for x in slots)
        self.t = {"gen": self.gen, "recs": recs, "slots": slots, "depth": depth, "dropped": dropped,
                  "tracing": True, "ticks": len(ticks), "snap_only": not ticks,
                  "snap": {"allocated": True, "bytes": sb, "rings": len(params), "per_ring_per_s": 1000,
                           "total_per_s": 10000, "skipped_budget": 0, "dropped_budget": 0,
                           "slots_per_ring": (sb - 64 * (len(params) + len(stacks))) // per_round},
                  "stack": {"rings": len(stacks), "depth": depth,
                            "per_ring_per_s": clamp(st.get("per_ring_per_s", 100)),
                            "total_per_s": clamp(st.get("total_per_s", 200)), "captures": captures,
                            "skipped_budget": 0, "dropped_budget": sum(dropped), "spent_ticks": 7 * captures,
                            "max_ticks": 7} if stacks else None,
                  "names": [{"class": FIXTURE_CLASS, "func": n, "tick": n in [i["func"] for i in ticks],
                             "chosen": n in params, "addresses": 1, "arms": 1,
                             **({} if "old" in self.f else {"stack": n in ring_of})}
                            for n in self.FUNCS if n in params or n in ring_of or n in [i["func"] for i in ticks]]}
        names = {"ticks": len(ticks), "chosen": len(params),
                 "refused": [{"class": i.get("class"), "func": i.get("func"), "why": "no key names it in this process"}
                             for i in asked if not good(i)]}
        if "old" not in self.f:
            names["stacks"] = len(stacks)
        return {"data": {"recording": True, "hook_active": True, "trace": dict(self._info(), names=names)}}

    def _info(self) -> dict:
        t = self.t
        if t is None:
            return {"allocated": False, "tracing": False, "gen": self.gen}
        d = {"allocated": True, "tracing": t["tracing"], "quiesced": not t["tracing"], "gen": t["gen"],
             "qpc_freq": self.QPC, "written": len(t["recs"]), "first_valid": 0, "scoped": True,
             "ticked_names": t["ticks"], "snap_only": t["snap_only"], "snap": t["snap"]}
        if t["stack"]:
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
                                       "first_valid": 0, "skipped_budget": 0, "dropped_budget": t["dropped"][k],
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
            items.append({"index": frm + k, "entry_seq": s["entry_seq"], "flags": 0, "ticks": 7, "frames": fr})
        return {"data": dict(d, count=len(items), next=frm + len(items), orphans=0, items=items, sites=sites)}

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
            return {"data": {"recording": False, "trace": self._info(), "names": t["names"]}}
        if cmd == "pe_profile_get":
            rows = list(self.rows().values())
            return {"data": {"total_calls": sum(r["count"] for r in rows), "window_ms": 3000,
                             "functions": rows[: p.get("limit", len(rows))]}}
        if cmd == "pe_trace_get":
            if t is None:
                return self._info()
            frm, mx = p.get("from", 0), p.get("max", 1)
            part = t["recs"][frm: frm + mx]
            return dict(self._info(), data=base64.b64encode(b"".join(part)).decode(), next=frm + len(part))
        if cmd == "pe_trace_names":
            items = [{"addr": f"0x{a:X}", "class_name": FIXTURE_CLASS, "func_name": n,
                      "code_addr": f"0x{self.BASE + (self.OUTER_FN if n == 'SnapNest_Outer' else 0x8000):X}"}
                     for n, (a, _, _) in self.FUNCS.items()]
            off = p.get("offset", 0)
            return {"data": {"items": items[off: off + p.get("limit", 20000)], "total": len(items)}}
        if cmd == "pe_snap_get":
            return self._snap_get(p)
        if cmd == "pe_trace_release":
            self.t = None
            return {"data": {"released": True}}
        raise PipeError(f"the scripted DLL has no {cmd}")


def dry_run(dll: ScriptedDll, game: bool = False, argv: tuple[str, ...] = ()) -> tuple[Checks, dict]:
    """run_stacks (or run_game_stacks) against a scripted DLL, its printing captured: no pipe, no game, and no wait,
    on a FakeClock. `argv` adds options to the command line the run parses."""
    check, out = Checks(), {"label": "dry", "fixture": {"total_calls": 9000, "window_ms": 3000}}
    args = build_parser().parse_args(["--stacks", "--record-s", str(DRY_RECORD_S), "--plain-s", "0"] + list(argv) +
                                     (["--choose", ""] if game else []))
    fake = FakeClock()
    with contextlib.redirect_stdout(io.StringIO()):
        if game:
            run_game_stacks(dll, check, out, args, sleep=fake.sleep, clock=fake.now)
        else:
            run_stacks(dll, check, out, args, dll.rows(), pid=0, sleep=fake.sleep, clock=fake.now)
    return check, out


def self_test() -> int:
    """--self-test: the pure pieces of --stacks against hand-made replies, then run_stacks and run_game_stacks against a
    scripted DLL. Every rule is shown holding on a good input AND failing on a bad one, so a helper that silently
    accepts everything cannot pass."""
    results: list[tuple[str, bool, str]] = []

    def expect(name: str, fn) -> None:
        try:
            ok, why = bool(fn()), ""
        except Exception as e:   # a helper that throws on a hand-made reply fails its control, it does not end the run
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
    expect("dry run: a DLL with step 3 passes every check, S0 to S7, 25 in all",
           lambda: (lambda ch: failing(ch) == [] and len(ran(ch)) == 25 and
                    {n.split()[0] for n in ran(ch)} == {"S0", "S1", "S2", "S3", "S5", "S7"} and
                    recorded(ch, "S3") == [] and len(recorded(ch, "S4")) == 1 and
                    len(recorded(ch, "S6")) == 6)(dry_run(ScriptedDll())[0]))
    expect(f"dry run: S5's window over {DRY_RECORD_S:g} s is {FIXTURE_STACK_PER_RING * (DRY_RECORD_S - 1):g}.."
           f"{FIXTURE_STACK_PER_RING * (DRY_RECORD_S + 1):g}, read on the run's own clock",
           lambda: any(n.startswith(S5_WINDOW) and
                       f"({FIXTURE_STACK_PER_RING * (DRY_RECORD_S - 1):.0f}..{FIXTURE_STACK_PER_RING * (DRY_RECORD_S + 1):.0f})"
                       in n for n in ran(dry_run(ScriptedDll())[0])))
    expect("dry run: SnapProbe_PerFrame keeping too few stacks falls under S5's window",
           lambda: fail_set(dry_run(ScriptedDll(per_frame=20))[0], S5_WINDOW))
    expect("dry run: SnapProbe_PerFrame keeping too many stacks falls over S5's window",
           lambda: fail_set(dry_run(ScriptedDll(per_frame=100))[0], S5_WINDOW))
    expect("dry run: S4 finds SnapNest_Outer's entry at frame 1 of every in-scope stack",
           lambda: any(n.startswith("S4") and g.startswith("4 of 4; at frames [1]")
                       for n, _, g in dry_run(ScriptedDll())[0].records))
    expect("dry run (H1): a DLL without step 3 fails S0 and runs no S1-S6",
           lambda: (lambda ch: failing(ch) == ["S0 the reply carries names.stacks and trace.stack (a DLL with step 3)"]
                    and not any(n[:2] in ("S1", "S2", "S3", "S5") for n in ran(ch)))(
               dry_run(ScriptedDll("old"))[0]))
    expect("dry run (S3-F2's mutation): no known frame, known compared with the trampoline, fails S3's in-scope half",
           lambda: fail_set(dry_run(ScriptedDll("no_known"))[0], S3_KNOWN_IN))
    expect("dry run: every unwound frame labelled known fails S3's lone half and its one-function rule",
           lambda: fail_set(dry_run(ScriptedDll("known_everywhere"))[0], S3_KNOWN_LONE, S3_KNOWN_ONE))
    expect("dry run: two known frames in one stack fail S3's one-function rule",
           lambda: fail_set(dry_run(ScriptedDll("known_twice"))[0], S3_KNOWN_ONE))
    expect("dry run: known frames of two functions fail S3's one-function rule",
           lambda: fail_set(dry_run(ScriptedDll("known_split"))[0], S3_KNOWN_ONE))
    expect("dry run: a DLL that ignores kind fails S1",
           lambda: (lambda ch: any(n.startswith("S1") for n in failing(ch)))(
               dry_run(ScriptedDll("ignore_kind"))[0]))

    def refuses_unknown_fault() -> bool:
        try:
            ScriptedDll("no_such_fault")
        except ValueError:
            return True
        return False
    expect("the scripted DLL refuses a fault it does not have", refuses_unknown_fault)
    expect("dry run: --stacks --choose on a scripted game chooses every keyed function and records the cost",
           lambda: (lambda r: failing(r[0]) == [] and len(r[1]["chosen"]) == 3 and
                    r[1]["stack_cost"]["census"].get("slots") == 9 and len(r[0].records) == 6)(
               dry_run(ScriptedDll(), game=True)))

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

    failed = [r for r in results if not r[1]]
    for name, _, why in failed:
        say(f"  FAIL  {name}" + (f"   ({why})" if why else ""))
    say(f"self-test: {len(results) - len(failed)}/{len(results)} controls hold")
    return 1 if failed or not results else 0


if __name__ == "__main__":
    sys.exit(main())
