r"""Live check of Live Funcs step 2 -- parameter snapshots and following functions by name -- through the pipe.

    py tools/verify/livefuncs_snap_live.py --fixture-check
    py tools/verify/livefuncs_snap_live.py --label <run> [--record-s 8]

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

Against a DLL older than the item, its checks fail: that run is the item's red. Every recording is stopped in a
`finally` and the trace released. Exit 0 when every check holds; 1 otherwise; 2 when the pipe or the game is not
usable. Output: out/livefuncs-snap/<label>.json and a summary.
"""
from __future__ import annotations

import argparse
import base64
import ctypes
import ctypes.wintypes as w
import json
import pathlib
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

    def __call__(self, name: str, cond: bool, got: str = "") -> bool:
        self.items.append((name, bool(cond), got))
        say(f"  {'ok  ' if cond else 'FAIL'}  {name}" + (f"   ({got})" if got else ""))
        return bool(cond)

    def not_run(self, name: str, why: str) -> None:
        say(f"  --    {name}   (not run: {why})")

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
    want("Soft", "DumperTest58Anchor" in soft[0])
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


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--label", default="dumpertest58")
    ap.add_argument("--fixture-check", action="store_true", help="only check that the package carries the probes")
    ap.add_argument("--plain-s", type=float, default=3.0)
    ap.add_argument("--record-s", type=float, default=8.0)
    args = ap.parse_args()

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
        say("fixture:")
        out["fixture"] = fixture_check(c, check, args.plain_s)
        if out["fixture"]["total_calls"] == 0:
            say("no calls recorded: is the game running, scanned, and the hook up?")
            return 2
        if not args.fixture_check:
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
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    path = OUT_DIR / f"{args.label}{'-fixture' if args.fixture_check else ''}.json"
    path.write_text(json.dumps(out, indent=1, default=str), encoding="utf-8")
    say(f"\n{len(check.items) - check.failed}/{len(check.items)} checks hold; written to {path}")
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
    kinds = {p["name"]: p.get("kind") for p in lay.get("params", [])}
    out["call_kinds"] = kinds
    check("F5 Label is const_ref; OutTwice out; InOut in_out; ReturnValue return",
          kinds.get("Label") == "const_ref" and kinds.get("OutTwice") == "out" and kinds.get("InOut") == "in_out" and
          kinds.get("ReturnValue") == "return", json.dumps(kinds)[:160])
    names_l = [p["name"] for p in lay.get("params", [])]
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


if __name__ == "__main__":
    sys.exit(main())
