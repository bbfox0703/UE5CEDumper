#!/usr/bin/env python3
r"""Live check for [LISTINSTANCES-KEEPS-TEMPLATES] (build 3682 on, mailbox contract 8): no write pool lists a TEMPLATE.

    py tools/verify/listinstances_templates_live.py [--process DumperTest] [--value 777] [--baseline <summary.json>]

Written for the verification-register row [LISTINSTANCES-KEEPS-TEMPLATES]. From build 3682 the mailbox's
CMD_LIST_INSTANCES (exact and derived scope) and the pipe's force_field pool (Solide) never hold a TEMPLATE: a class
default object (named Default__*, or RF_ClassDefaultObject 0x10 at UObject+0x08), one of its default subobjects (a
Default__ object up the outer chain; measured flags 0x40021 on DumperTest 5.4) or a Blueprint component template
(RF_ArchetypeObject 0x20). The pipe's find_instances (the Instance Finder) still lists them on purpose.

The template test is the rig's OWN, not the DLL's word: ObjectFlags are read out of the process with
ReadProcessMemory and the outer chain is followed through get_object, the same test template_pick_probe.py applies.
It is deliberately a little broader than the DLL's (an archetype flag anywhere up the chain also counts), so a
disagreement shows up as a template the DLL let through.

Checks (one line each, "<ID> OK|BAD  <detail>"; INFO lines are records, never counted):
  T7  g_mailboxContract (read from the injected module's export): magic right, current >= 8, min == 1
  T1  LIST_INSTANCES exact "CharacterMovementComponent", every page: template entries == 0, live entries >= 1,
      total (parmsSize) == entries returned, and the same total on every page
  T2  LIST_INSTANCES derived, for "CharacterMovementComponent" and for "ActorComponent": template entries == 0, live
      entries >= 1; prints the total and the LI_OUT_TRUNCATED bit
  T4  control: pipe find_instances exact "CharacterMovementComponent" still lists Default__CharacterMovementComponent
      and >= 1 default subobject template
  T4x find_instances' rows that the rig classifies live are exactly T1's entries (nothing live was filtered away)
  T3  force_field CharacterMovementComponent.MaxWalkSpeed numeric --value (777), then reset_field in a finally block:
      T3a held == the derived live count from T2 (Solide's pool is derived; T1's exact count is printed beside it)
      T3b the local pawn's CharacterMovementComponent reads the value (pawn from teleport_get_pose, its CMC and the
          field offset from walk_instance, the float read with ReadProcessMemory)
      T3c a template CMC from T4 (a default subobject preferred over Default__CharacterMovementComponent) keeps its
          own default, before, during and after
      T3d after reset_field the pawn reads what it read before, and get_forced_fields no longer lists the hold
  T5  walk_world limit 200: actor_count / actor_total recorded; with --baseline (a previous run's *_summary.json,
      or a saved walk_world reply) the world name and actor_total must match, without it the line is INFO
  T6  INFO, no threshold: the median of 5 round trips each for LIST_INSTANCES derived "Actor" page 0, a full
      all-pages derived "ActorComponent" listing, and walk_world limit 200

Prerequisites:
  * DumperTest (Shipping, UE 5.4 -- launch_dumpertest.py shipping) on its third-person map with the pawn possessed,
    the DLL injected or a proxy deployed, and the pipe answering a build from dist\ (pc.assert_build() refuses a
    stale DLL). Build 3682 or later; an older DLL fails T7 and lists templates in T1/T2.
  * The UI NOT connected: the rig owns the pipe and the mailbox for the run.
  * No MaxWalkSpeed hold and no Movement-tuning walk-speed knob active; T3 refuses rather than read a held value as
    the pawn's own.

Writes: only the mailbox's input fields, and the game objects only through force_field / reset_field. Every pipe
reply and mailbox page the rig reads is saved as it is read to out\LISTINSTANCES-KEEPS-TEMPLATES\<stamp>_replies.jsonl,
and the verdicts with their numbers to <stamp>_summary.json (working-lessons 1.ad).
Exit code 0 only when every check passed and at least one check ran.
"""
from __future__ import annotations

import argparse
import collections
import json
import pathlib
import statistics
import struct
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from pipe_client import PipeClient  # noqa: E402
import mailbox_poke as MB  # noqa: E402
import mailbox_addr as MA  # noqa: E402
from aa2_class_witness import list_instances, LI_OUT_TRUNCATED  # noqa: E402
from invoke_template_live import data, a2i  # noqa: E402

TAG = "LISTINSTANCES-KEEPS-TEMPLATES"
CMC = "CharacterMovementComponent"
FIELD = "MaxWalkSpeed"
PAWN_CMC_FIELD = "CharacterMovement"   # ACharacter's pointer to its movement component
OFF_UOBJECT_FLAGS = 0x08
RF_CLASS_DEFAULT_OBJECT, RF_ARCHETYPE_OBJECT = 0x10, 0x20
CONTRACT_MAGIC, CONTRACT_WANT, CONTRACT_MIN_WANT = 0x43354555, 8, 1
INJECTED_MODULES = ("ue5dumper.dll", "version.dll", "dxgi.dll", "dinput8.dll", "winmm.dll")
PARAMS_BYTES = 1024          # MailboxData::paramsData
MAX_PAGES = 64               # far above the DLL's 16-page ceiling: a larger count is a garbled reply, not a pool
MAX_OUTER_HOPS = 16
OUTFLAGS_POISON = 0xFFFFFFFF  # what list_instances writes before the trigger
TIMING_SAMPLES = 5
LIVE = "live"
TEMPLATE_KINDS = ("class_default", "default_subobject", "archetype", "archetype_outer")


class Evidence:
    """Every reply as it is read. Stdout is not evidence, and the DLL's own pipe log records requests, not replies."""

    def __init__(self):
        self.dir = ROOT / "out" / TAG
        self.dir.mkdir(parents=True, exist_ok=True)
        self.stamp = time.strftime("%Y%m%d-%H%M%S")
        self.replies = self.dir / f"{self.stamp}_replies.jsonl"
        self.summary = self.dir / f"{self.stamp}_summary.json"
        self._f = self.replies.open("w", encoding="utf-8", newline="\n")
        self.seq = 0

    def add(self, kind, request, reply, **extra):
        self.seq += 1
        now = time.time()
        rec = {"seq": self.seq, "t": time.strftime("%Y-%m-%dT%H:%M:%S", time.localtime(now)) + f".{int(now * 1000) % 1000:03d}",
               "kind": kind, "request": request, "reply": reply}
        rec.update(extra)
        self._f.write(json.dumps(rec, ensure_ascii=False, default=str) + "\n")
        self._f.flush()   # a crash mid-run must still leave every reply read so far

    def close(self):
        self._f.close()


class Rig:
    def __init__(self, pc, m, base, ev):
        self.pc, self.m, self.base, self.ev = pc, m, base, ev
        self.checks: list[dict] = []
        self.objinfo: dict[int, dict] = {}
        self.last_ms = 0.0

    # -- verdicts ---------------------------------------------------------------
    def verdict(self, cid, ok, detail, **numbers):
        v = "OK" if ok else "BAD"
        print(f"{cid} {v}  {detail}", flush=True)
        self.checks.append({"id": cid, "verdict": v, "detail": detail, **numbers})

    def info(self, cid, detail, **numbers):
        print(f"{cid} INFO  {detail}", flush=True)
        self.checks.append({"id": cid, "verdict": "INFO", "detail": detail, **numbers})

    # -- transports, each one recorded --------------------------------------------
    def ask(self, cmd, **params):
        t0 = time.perf_counter()
        r = self.pc.request(cmd, **params)
        self.last_ms = (time.perf_counter() - t0) * 1000.0
        self.ev.add("pipe", {"cmd": cmd, **params}, r, ms=round(self.last_ms, 3))
        return r

    def li_page(self, cls, derived, page):
        t0 = time.perf_counter()
        r = list_instances(self.m, self.base, cls, derived=derived, page=page, timeout=60.0)
        wall = (time.perf_counter() - t0) * 1000.0
        esz = 16 if derived else 8
        # A failed command (SetError) leaves numParms / parmsSize / functionFlags as the PREVIOUS command wrote them,
        # so a failed page's counts are not this page's and its blob holds no entries.
        n = min(r["returned"], PARAMS_BYTES // esz) if r["result"] == 0 else 0
        entries = []
        for i in range(n):
            obj = struct.unpack_from("<Q", r["blob"], i * esz)[0]
            kls = struct.unpack_from("<Q", r["blob"], i * esz + 8)[0] if derived else r["witness"]
            entries.append((obj, kls))
        rec = {k: v for k, v in r.items() if k != "blob"}
        rec["blob_hex"] = r["blob"].hex()
        self.ev.add("mailbox", {"cmd": "LIST_INSTANCES", "class": cls, "derived": derived, "page": page}, rec,
                    wall_ms=round(wall, 3))
        return r, entries, wall

    def li_all(self, cls, derived):
        """Every page of one listing. Each page is a fresh GObjects walk in the DLL, so the totals are compared."""
        r0, e0, wall = self.li_page(cls, derived, 0)
        pages, entries = [r0], list(e0)
        n = r0["pages"] if r0["result"] == 0 else 1   # a failed page 0's page count is a stale one
        if n > MAX_PAGES:
            raise SystemExit(f"LIST_INSTANCES {cls}: {n} pages reported -- not a pool this rig can trust")
        for p in range(1, n):
            r, e, w = self.li_page(cls, derived, p)
            pages.append(r)
            entries += e
            wall += w
        poisoned = any(r["outflags"] == OUTFLAGS_POISON for r in pages)
        return {
            "class": cls, "derived": derived, "pages": n,
            "results": [r["result"] for r in pages],
            "errors": [r["err"] for r in pages if r["err"]],
            "total": r0["total"], "totals": sorted({r["total"] for r in pages}),
            "entries": entries,
            "outflags_poisoned": poisoned,
            "truncated": (not poisoned) and any(r["outflags"] & LI_OUT_TRUNCATED for r in pages),
            "wall_ms": wall,
        }

    # -- the rig's own template test ------------------------------------------------
    def obj(self, addr):
        hit = self.objinfo.get(addr)
        if hit is not None:
            return hit
        g = data(self.ask("get_object", addr=hex(addr)))
        try:
            flags = struct.unpack("<I", self.m.read(addr + OFF_UOBJECT_FLAGS, 4))[0]
        except SystemExit:   # Mem.read fails loudly; an unreadable object is reported, never guessed live
            flags = None
        # The flags decide the template verdict as much as the name does, so they go on disk beside get_object's reply.
        self.ev.add("memory", {"read": "ObjectFlags", "addr": hex(addr + OFF_UOBJECT_FLAGS)},
                    {"flags": flags if flags is None else hex(flags)})
        info ={"addr": hex(addr), "name": g.get("name", "") or "", "class": g.get("class", "") or "",
                "outer": a2i(g.get("outer_addr", "0") or "0"), "flags": flags}
        self.objinfo[addr] = info
        return info

    @staticmethod
    def _is_cdo(info):
        return info["name"].startswith("Default__") or bool((info["flags"] or 0) & RF_CLASS_DEFAULT_OBJECT)

    def template_kind(self, addr):
        """(kind, owner name). kind is "" for a live object, "unreadable" when its flags could not be read."""
        me = self.obj(addr)
        if me["flags"] is None:
            return "unreadable", ""
        if self._is_cdo(me):
            return "class_default", me["name"]
        o, seen, archetype_up = me["outer"], {addr}, False
        for _ in range(MAX_OUTER_HOPS):
            if not o or o in seen:
                break
            seen.add(o)
            up = self.obj(o)
            if self._is_cdo(up):
                return "default_subobject", up["name"]
            archetype_up |= bool((up["flags"] or 0) & RF_ARCHETYPE_OBJECT)
            o = up["outer"]
        if me["flags"] & RF_ARCHETYPE_OBJECT:
            return "archetype", ""
        if archetype_up:
            return "archetype_outer", ""
        return "", ""

    def census(self, addrs):
        kinds, examples = collections.Counter(), {}
        for a in addrs:
            k, owner = self.template_kind(a)
            kinds[k or LIVE] += 1
            if k and k not in examples:
                info = self.obj(a)
                examples[k] = {"addr": hex(a), "name": info["name"], "flags": info["flags"], "owner": owner}
        return kinds, examples


def kinds_str(kinds):
    return ",".join(f"{k}={v}" for k, v in sorted(kinds.items())) or "none"


def bad_kinds(kinds):
    """Templates plus unreadable entries: neither may be counted as a live object."""
    return sum(v for k, v in kinds.items() if k != LIVE)


def close_to(a, b):
    return a is not None and b is not None and abs(a - b) <= 1e-3 * max(1.0, abs(b))


def read_num(m, addr, ftype):
    if ftype == "FloatProperty":
        return struct.unpack("<f", m.read(addr, 4))[0]
    if ftype == "DoubleProperty":
        return struct.unpack("<d", m.read(addr, 8))[0]
    raise SystemExit(f"{FIELD} is a {ftype}, not a float or double -- this rig cannot read it")


# ------------------------------------------------------------------------------------------------ the checks
def check_contract(rig, pid, build):
    rd = MA.Reader(pid)
    addr, module = 0, ""
    for path, mbase in MA.modules(pid):
        if mbase and path.lower().endswith(INJECTED_MODULES):
            try:
                addr = MA.find_export(rd, mbase, "g_mailboxContract") or 0
            except SystemExit:
                addr = 0
            if addr:
                module = path
                break
    if not addr:
        rig.verdict("T7", False, "g_mailboxContract is exported by no injected module")
        return
    magic, cur, lo = struct.unpack("<Iii", rig.m.read(addr, 12))
    rig.ev.add("memory", {"read": "g_mailboxContract", "addr": hex(addr), "module": module},
               {"magic": magic, "current": cur, "minimum": lo})
    rig.verdict("T7", magic == CONTRACT_MAGIC and cur >= CONTRACT_WANT and lo == CONTRACT_MIN_WANT,
                f"magic={magic:#x} current={cur} min={lo} (want current >= {CONTRACT_WANT}, min {CONTRACT_MIN_WANT})"
                f" build={build} module={pathlib.Path(module).name}", current=cur, minimum=lo, magic=magic)


def check_t1(rig):
    t1 = rig.li_all(CMC, derived=False)
    addrs = [o for o, _ in t1["entries"]]
    kinds, ex = rig.census(addrs)
    live, bad, n = kinds[LIVE], bad_kinds(kinds), len(addrs)
    ok = (all(r == 0 for r in t1["results"]) and bad == 0 and live >= 1
          and t1["total"] == n and len(t1["totals"]) == 1)
    rig.verdict("T1", ok, f"exact {CMC}: entries={n} total={t1['total']} totals/page={t1['totals']} "
                f"pages={t1['pages']} kinds[{kinds_str(kinds)}] results={t1['results']}"
                + (f" first-template={ex}" if ex else "") + (f" errors={t1['errors']}" if t1["errors"] else ""),
                entries=n, total=t1["total"], live=live, templates=bad, kinds=dict(kinds), examples=ex)
    return t1, set(addrs)


def check_t2(rig, cls):
    t2 = rig.li_all(cls, derived=True)
    addrs = [o for o, _ in t2["entries"]]
    kinds, ex = rig.census(addrs)
    live, bad = kinds[LIVE], bad_kinds(kinds)
    ok = all(r == 0 for r in t2["results"]) and bad == 0 and live >= 1 and not t2["outflags_poisoned"]
    rig.verdict("T2", ok, f"derived {cls}: entries={len(addrs)} total={t2['total']} totals/page={t2['totals']} "
                f"pages={t2['pages']} LI_OUT_TRUNCATED={'unwritten' if t2['outflags_poisoned'] else int(t2['truncated'])} "
                f"kinds[{kinds_str(kinds)}]" + (f" first-template={ex}" if ex else ""),
                cls=cls, entries=len(addrs), total=t2["total"], live=live, templates=bad,
                truncated=t2["truncated"], kinds=dict(kinds), examples=ex)
    return t2, live


def check_t4(rig, exact_set):
    r = rig.ask("find_instances", class_name=CMC, exact_match=True, limit=2000)
    rows = r.get("instances") or []
    rows_k = []
    for row in rows:
        k, owner = rig.template_kind(a2i(row.get("addr", "0")))
        rows_k.append((row, k, owner))
    kinds = collections.Counter(k or LIVE for _, k, _ in rows_k)
    has_cdo = any(row.get("name") == f"Default__{CMC}" for row, _, _ in rows_k)
    subs = [(row, owner) for row, k, owner in rows_k if k == "default_subobject"]
    rig.verdict("T4", bool(r.get("ok")) and not r.get("truncated") and has_cdo and len(subs) >= 1,
                f"find_instances exact {CMC}: rows={len(rows)} truncated={r.get('truncated')} "
                f"Default__{CMC}={'listed' if has_cdo else 'MISSING'} default_subobjects={len(subs)} "
                f"kinds[{kinds_str(kinds)}] e.g. "
                + "; ".join(f"{row.get('name')} < {owner}" for row, owner in subs[:3]),
                rows=len(rows), kinds=dict(kinds), default_subobjects=[
                    {"addr": row.get("addr"), "name": row.get("name"), "owner": owner} for row, owner in subs])

    # find_instances keeps templates and LIST_INSTANCES drops them, so the LIVE remainder must be the same set: a
    # template test that also caught live objects would shrink the write pool without failing T1.
    live_set = {a2i(row.get("addr", "0")) for row, k, _ in rows_k if not k}
    missing, extra = live_set - exact_set, exact_set - live_set
    rig.verdict("T4x", not r.get("truncated") and not missing and not extra and bool(live_set),
                f"live rows in find_instances={len(live_set)} LIST_INSTANCES exact={len(exact_set)} "
                f"missing-from-LIST_INSTANCES={[hex(a) for a in sorted(missing)][:5]} "
                f"only-in-LIST_INSTANCES={[hex(a) for a in sorted(extra)][:5]}",
                live_rows=len(live_set), listed=len(exact_set), missing=len(missing), extra=len(extra))

    # The template T3 watches: a default subobject is what every spawn copies, and a Blueprint's own is preferred.
    pick = next(((row, "default_subobject", owner) for row, owner in subs if owner.endswith("_C")), None)
    pick = pick or next(((row, "default_subobject", owner) for row, owner in subs), None)
    pick = pick or next(((row, k, owner) for row, k, owner in rows_k if k == "class_default"), None)
    return pick


def find_pawn_cmc(rig):
    """(pawn, cmc, field) of the local pawn, all read-only. field = {offset, type, size} of MaxWalkSpeed."""
    pose = data(rig.ask("teleport_get_pose"))
    pawn = a2i(pose.get("pawn_addr", "0") or "0")
    if not pawn:
        return 0, 0, None, f"teleport_get_pose gave no pawn (code={pose.get('code')})"
    w = data(rig.ask("walk_instance", addr=hex(pawn), array_limit=0, preview_limit=0))
    cmc = next((a2i(f.get("ptr", "0")) for f in w.get("fields") or [] if f.get("name") == PAWN_CMC_FIELD), 0)
    if not cmc:
        return pawn, 0, None, f"the pawn {w.get('name')} has no {PAWN_CMC_FIELD} pointer"
    wc = data(rig.ask("walk_instance", addr=hex(cmc), array_limit=0, preview_limit=0))
    f = next((f for f in wc.get("fields") or [] if f.get("name") == FIELD), None)
    if not f:
        return pawn, cmc, None, f"walk_instance of the pawn's CMC lists no {FIELD}"
    return pawn, cmc, {"offset": int(f["offset"]), "type": f.get("type"), "size": f.get("size"),
                       "pawn_name": w.get("name"), "cmc_name": wc.get("name"), "cmc_class": wc.get("class")}, ""


def check_t3(rig, a, exact_live, derived_live, pick):
    holds = data(rig.ask("get_forced_fields")).get("fields") or []
    if any(h.get("field_name") == FIELD for h in holds):
        rig.verdict("T3", False, f"a {FIELD} hold is already active ({holds}); refusing -- every before-read would "
                    "be that hold's value")
        return
    knob = (data(rig.ask("get_movement_params")).get("knobs") or {}).get("walk_speed") or {}
    if knob.get("active"):
        rig.verdict("T3", False, f"the Movement-tuning walk-speed knob is active ({knob}); refusing -- it writes "
                    f"{FIELD} on the pawn too")
        return
    pawn, cmc, fld, why = find_pawn_cmc(rig)
    if not fld:
        rig.verdict("T3", False, why)
        return
    if not pick:
        rig.verdict("T3", False, f"T4 found no template {CMC} to watch")
        return
    tpl_row, tpl_kind, tpl_owner = pick
    tpl = a2i(tpl_row.get("addr", "0"))
    off, ftype = fld["offset"], fld["type"]
    value = float(a.value)

    def reads(stage):
        p, t = read_num(rig.m, cmc + off, ftype), read_num(rig.m, tpl + off, ftype)
        rig.ev.add("memory", {"read": FIELD, "stage": stage, "pawn_cmc": hex(cmc + off), "template": hex(tpl + off),
                              "type": ftype}, {"pawn": p, "template": t})
        return p, t

    p0, t0 = reads("before")
    print(f"     pawn {fld['pawn_name']} {hex(pawn)}  CMC {fld['cmc_name']} ({fld['cmc_class']}) {hex(cmc)}  "
          f"{FIELD} +{off:#x} {ftype}  before={p0}", flush=True)
    print(f"     template {tpl_row.get('name')} {hex(tpl)} [{tpl_kind}{' < ' + tpl_owner if tpl_owner else ''}]  "
          f"before={t0}", flush=True)
    if close_to(p0, value) or close_to(t0, value):
        rig.verdict("T3", False, f"pawn={p0} template={t0} already read {value}; the hold cannot be told apart")
        return

    armed, fr, st, p1, t1 = False, {}, {}, None, None
    reset, reset_err = {}, ""
    try:
        armed = True   # set before the request: a reply lost on the way back can still leave the hold armed
        fr = data(rig.ask("force_field", class_name=CMC, field_name=FIELD, kind="numeric", value=value))
        time.sleep(a.settle)
        p1, t1 = reads("held")
        st = data(rig.ask("get_forced_fields"))
    finally:
        if armed:
            try:
                reset = data(rig.ask("reset_field", class_name=CMC, field_name=FIELD))
            except Exception as e:   # noqa: BLE001 -- the hold must be named loudly, whatever broke the reset
                reset_err = repr(e)
                print(f"!!! reset_field FAILED ({reset_err}): {CMC}.{FIELD} may still be held at {value}",
                      flush=True)
    time.sleep(a.settle)
    p2, t2 = reads("after")
    left = [h for h in data(rig.ask("get_forced_fields")).get("fields") or []
            if h.get("class_name") == CMC and h.get("field_name") == FIELD]

    held = fr.get("held")
    rig.verdict("T3a", bool(fr.get("ok")) and fr.get("code") == 0 and isinstance(held, int) and held >= 1
                and held == derived_live and not fr.get("truncated"),
                f"force_field held={held} code={fr.get('code')} truncated={fr.get('truncated')} "
                f"want derived live={derived_live} (exact live={exact_live})"
                + (f" error={fr.get('error')!r}" if fr.get("error") else ""),
                held=held, derived_live=derived_live, exact_live=exact_live, forced_state=st.get("fields"))
    rig.verdict("T3b", close_to(p1, value), f"pawn CMC {FIELD}: before={p0} held={p1} (want {value})",
                before=p0, held=p1)
    rig.verdict("T3c", close_to(t1, t0) and not close_to(t1, value) and close_to(t2, t0),
                f"template {tpl_row.get('name')} [{tpl_kind}] {FIELD}: before={t0} held={t1} after={t2} "
                f"(want its own default throughout, never {value})", before=t0, held=t1, after=t2,
                template=tpl_row.get("name"), template_kind=tpl_kind)
    rig.verdict("T3d", not reset_err and reset.get("code") == 0 and close_to(p2, p0) and not left,
                f"reset_field code={reset.get('code')}{' ' + reset_err if reset_err else ''} pawn after={p2} "
                f"(want {p0}) hold still listed={bool(left)}", after=p2, reset_code=reset.get("code"))


def load_baseline(path):
    b = json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
    ww = b.get("walk_world") or b.get("reply") or b
    return {"actor_total": ww.get("actor_total"), "actor_count": ww.get("actor_count"),
            "world_name": ww.get("world_name"), "build": b.get("build")}


def check_t5(rig, a):
    r = rig.ask("walk_world", limit=200)
    ww = {"world_name": r.get("world_name"), "actor_count": r.get("actor_count"),
          "actor_total": r.get("actor_total"), "truncated": r.get("truncated"), "error": r.get("error")}
    detail = (f"walk_world limit 200: world={ww['world_name']} actor_count={ww['actor_count']} "
              f"actor_total={ww['actor_total']} truncated={ww['truncated']}"
              + (f" error={ww['error']!r}" if ww["error"] else ""))
    if not a.baseline:
        rig.info("T5", detail + " (no --baseline: recorded only)", **ww)
        return ww
    base = load_baseline(a.baseline)
    ok = (bool(r.get("ok")) and ww["world_name"] == base["world_name"] and isinstance(ww["actor_total"], int)
          and ww["actor_total"] >= 0 and ww["actor_total"] == base["actor_total"])
    rig.verdict("T5", ok, detail + f" | baseline world={base['world_name']} actor_total={base['actor_total']} "
                f"build={base['build']}", baseline=base, **ww)
    return ww


def check_t6(rig):
    def median_of(fn):
        xs = [fn() for _ in range(TIMING_SAMPLES)]
        return statistics.median(xs), xs

    def actor_p0():
        return rig.li_page("Actor", True, 0)[2]

    def components_all():
        return rig.li_all("ActorComponent", True)["wall_ms"]

    def world():
        rig.ask("walk_world", limit=200)
        return rig.last_ms

    for label, fn in (("LIST_INSTANCES derived Actor page 0", actor_p0),
                      ("LIST_INSTANCES derived ActorComponent all pages", components_all),
                      ("walk_world limit 200", world)):
        med, xs = median_of(fn)
        rig.info("T6", f"{label}: median {med:.1f} ms over {len(xs)} ({', '.join(f'{x:.1f}' for x in xs)})",
                 what=label, median_ms=med, samples_ms=xs)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--process", default="DumperTest", help="game process name prefix, or a PID")
    ap.add_argument("--value", type=float, default=777.0, help="the MaxWalkSpeed T3 holds (default 777)")
    ap.add_argument("--settle", type=float, default=0.5, help="seconds to wait after force_field / reset_field")
    ap.add_argument("--baseline", help="a previous run's *_summary.json (or a saved walk_world reply) for T5")
    a = ap.parse_args()

    pid = int(a.process) if a.process.isdigit() else MB.pid_of(a.process)
    base = MB.mailbox_addr(str(pid))
    m = MB.Mem(pid)
    ev = Evidence()
    summary = {"tag": TAG, "stamp": ev.stamp, "process": a.process, "pid": pid, "mailbox": hex(base),
               "args": vars(a), "replies": str(ev.replies)}
    rig = None
    try:
        with PipeClient(timeout=180) as pc:
            rig = Rig(pc, m, base, ev)
            try:
                # Inside the try: a stale DLL (StaleBuildError) must end as a BAD line and a FAIL, not a traceback
                # that prints no result at all.
                build = pc.assert_build()
                print("build:", build, flush=True)
                summary["build"] = build
                pc.ensure_scanned()
                rig.ask("get_pointers")   # the build and pointers this run saw, on disk beside its replies
                init = m.i32(base + MB.OFF_INITSTATE)
                if init != MB.INIT_READY:
                    raise SystemExit(f"mailbox initState={init}, not READY -- a mailbox timeout would mean nothing")
                check_contract(rig, pid, build)
                t1, exact_set = check_t1(rig)
                _, derived_cmc_live = check_t2(rig, CMC)
                check_t2(rig, "ActorComponent")
                pick = check_t4(rig, exact_set)
                check_t3(rig, a, len(exact_set), derived_cmc_live, pick)
                summary["walk_world"] = check_t5(rig, a)
                check_t6(rig)
            except (Exception, SystemExit) as e:   # SystemExit: the mailbox helpers fail loudly that way
                rig.verdict("ABORT", False, f"{type(e).__name__}: {e}")
    finally:
        if rig is not None:
            summary["checks"] = rig.checks
        ev.close()
        ev.summary.write_text(json.dumps(summary, indent=1, ensure_ascii=False, default=str), encoding="utf-8",
                              newline="\n")
        print(f"evidence: {ev.replies}\nsummary:  {ev.summary}", flush=True)

    counted = [c for c in rig.checks if c["verdict"] in ("OK", "BAD")]
    bad = sum(c["verdict"] == "BAD" for c in counted)
    if not counted:
        print("RESULT: FAIL (no check ran)")
        return 1
    print("RESULT:", f"PASS ({len(counted)} checks)" if bad == 0 else f"FAIL ({bad} of {len(counted)})")
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
