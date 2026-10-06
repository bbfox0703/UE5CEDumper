#!/usr/bin/env python3
"""
diff_dumps.py — diff two `Dump All Metadata` JSONL files from the same
game across patches. Surfaces the per-UClass / per-UProperty changes
that silently break cheat tables when a game ships an update.

USAGE
    python diff_dumps.py <old.jsonl> <new.jsonl>
    python diff_dumps.py <old.jsonl> <new.jsonl> -o diff-report.md
    python diff_dumps.py <old.jsonl> <new.jsonl> --minimal
    python diff_dumps.py <old.jsonl> <new.jsonl> --include-engine
    python diff_dumps.py --self-test

WHAT IT DOES
    1. Loads two JSONL dumps. Each dump = meta line + class lines +
       summary line (see DumpAllService.cs schema, same as analyze_dumps).
       Struct and enum lines are not compared yet.
    2. Matches classes by `path` (UClass*'s `addr` is session-local so
       useless across runs). Game classes only by default: Blueprint
       classes and the game's own C++ modules. `--include-engine` adds the
       engine's modules (engine_paths.py, the DLL's list).
    3. For each pair of matching classes, computes:
         - props_size delta
         - per-property change set:
             added / removed / moved (offset OR size delta)
             type changed (e.g. FloatProperty -> DoubleProperty)
         - per-function change set:
             added / removed / signature changed (return_type,
             num_parms, or parms_size differs — body content isn't in
             the dump)
    4. Emits a Markdown report:
         - Summary counters
         - Added / Removed classes
         - Per-changed-class breakdown of property + function changes
       In `--minimal` mode emits ONLY MovedFields and
       FunctionSignatureChanges — the subset cheat-table maintainers
       care about because those are the changes that silently break a
       working table.

NOT IN SCOPE
    - Rename detection (renamed class shows as Removed + Added; same
      for renamed field). Documented limitation. Use a manual grep
      pass on the report if you suspect a rename.
    - Function body comparison. Dumps only capture function metadata
      (return_type, num_parms, parms_size, flags) — the bytecode +
      machine code aren't dumped. parms_size delta catches param-shape
      changes; body-internal logic changes are invisible.
    - Cross-game diffing (different `module`). The two dumps must come
      from the same game across patches.

DESIGN NOTES
    - `path` is the match key because it's the canonical UE identifier
      and survives recompile / re-link. `name` alone collides for
      nested classes ('Inner' in different outers).
    - The dumper emits paths as `//Script/Module/Class` (note the
      double-leading slash from Ubel::GetFullName's leading '/'); the
      diff treats path-with-or-without-leading-slash as equivalent so
      we're robust to a dumper format tweak.
    - Property match within a class is by `name` — offset is what we're
      diffing FOR, so it can't be part of the match key. If a class
      shadows an inherited prop with a same-named field at a different
      offset (rare; reflection forbids it for UPROPERTY), only one will
      be visible per side; treated as a normal Moved entry.
    - All counts use real `len()`; no estimate fields. The report's
      header counters are the ground truth.
"""

from __future__ import annotations
import argparse
import json
import sys
from collections import OrderedDict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterator

from engine_paths import is_engine_path


# =====================================================================
# I/O — read a JSONL dump (same loader shape as analyze_dumps.py)
# =====================================================================

@dataclass
class Dump:
    path: Path
    meta: dict = field(default_factory=dict)
    classes: list[dict] = field(default_factory=list)
    errors: list[dict] = field(default_factory=list)
    summary: dict = field(default_factory=dict)

    @property
    def label(self) -> str:
        m = self.meta.get("module", "")
        if not m:
            return self.path.stem
        return m.replace("-Win64-Shipping.exe", "").replace(".exe", "")

    @property
    def dumper_build(self) -> int:
        return int(self.meta.get("dumper_build", 0))

    @property
    def ue_version(self) -> int:
        return int(self.meta.get("ue_version", 0))


def load_dump(path: Path) -> Dump:
    d = Dump(path=path)
    with path.open(encoding="utf-8") as f:
        for lineno, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                rec = json.loads(line)
            except json.JSONDecodeError as e:
                print(f"  [warn] {path.name}:{lineno} bad JSON: {e}", file=sys.stderr)
                continue
            kind = rec.get("kind")
            if kind == "meta":
                d.meta = rec
            elif kind == "class":
                d.classes.append(rec)
            elif kind == "error":
                d.errors.append(rec)
            elif kind == "summary":
                d.summary = rec
    return d


# =====================================================================
# Path normalization + engine filter
# =====================================================================

def normalize_path(p: str) -> str:
    """Strip leading slashes so '//Script/X/Y' and '/Script/X/Y' match.
    Lowercase comparison would be wrong — UE paths preserve case. We
    only collapse leading-slash runs."""
    if not p:
        return ""
    return p.lstrip("/")


def is_engine_class(cls: dict) -> bool:
    return is_engine_path(cls.get("path", ""))


# =====================================================================
# Diff data shapes
# =====================================================================

@dataclass
class PropChange:
    name: str
    # 'added' | 'removed' | 'moved' | 'type_changed'
    kind: str
    old: dict | None = None
    new: dict | None = None

    @property
    def offset_changed(self) -> bool:
        if not (self.old and self.new):
            return False
        return self.old.get("offset") != self.new.get("offset")

    @property
    def size_changed(self) -> bool:
        if not (self.old and self.new):
            return False
        return self.old.get("size") != self.new.get("size")


@dataclass
class FuncChange:
    name: str
    # 'added' | 'removed' | 'signature_changed'
    kind: str
    old: dict | None = None
    new: dict | None = None


@dataclass
class ClassDiff:
    name: str
    path: str
    old: dict
    new: dict
    prop_changes: list[PropChange] = field(default_factory=list)
    func_changes: list[FuncChange] = field(default_factory=list)

    @property
    def props_size_delta(self) -> int:
        return self.new.get("props_size", 0) - self.old.get("props_size", 0)

    @property
    def has_any_change(self) -> bool:
        return bool(self.prop_changes) or bool(self.func_changes) or self.props_size_delta != 0

    @property
    def has_breaking_change(self) -> bool:
        """`--minimal` mode emits ONLY classes with breaking changes —
        prop moves (offset/size) or function signature changes. Added /
        removed fields aren't 'breaking' a working table (the table
        just references an offset that's still there or no longer
        there)."""
        for pc in self.prop_changes:
            if pc.kind == "moved" or pc.kind == "type_changed":
                return True
        for fc in self.func_changes:
            if fc.kind == "signature_changed":
                return True
        return False


@dataclass
class DumpDiff:
    old_dump: Dump
    new_dump: Dump
    added_classes: list[dict] = field(default_factory=list)
    removed_classes: list[dict] = field(default_factory=list)
    changed: list[ClassDiff] = field(default_factory=list)
    unchanged_count: int = 0


# =====================================================================
# Core diff
# =====================================================================

def _index_classes(dump: Dump, include_engine: bool) -> dict[str, dict]:
    """path -> class record. Drops engine classes unless requested.

    Duplicate paths in a single dump are highly unusual (would indicate
    a dumper bug — same UClass walked twice). We keep the FIRST and
    log a warning so the diff stays deterministic; analyst can grep
    the source dump if the warning fires."""
    out: dict[str, dict] = {}
    for cls in dump.classes:
        if not include_engine and is_engine_class(cls):
            continue
        key = normalize_path(cls.get("path", ""))
        if not key:
            # Fallback: name-only key. Reduces match precision (engine
            # inner classes collide) but better than dropping the row.
            key = "::name::" + cls.get("name", "")
        if key in out:
            print(f"  [warn] duplicate class path in {dump.label}: {key} — "
                  f"keeping first", file=sys.stderr)
            continue
        out[key] = cls
    return out


def _diff_props(old_cls: dict, new_cls: dict) -> list[PropChange]:
    """Per-property change set. Match by name within the class."""
    old_props = {p["name"]: p for p in old_cls.get("props", []) if p.get("name")}
    new_props = {p["name"]: p for p in new_cls.get("props", []) if p.get("name")}

    changes: list[PropChange] = []

    # Preserve old insertion order for stable report output.
    for name, op in old_props.items():
        np = new_props.get(name)
        if np is None:
            changes.append(PropChange(name=name, kind="removed", old=op))
            continue
        # Existing prop — check for offset, size, type changes.
        # Type change is its own kind (more disruptive than a pure move
        # because the value semantics may differ — FloatProperty ->
        # DoubleProperty isn't binary-compatible).
        type_changed = (op.get("type") != np.get("type")
                        or op.get("inner_type") != np.get("inner_type")
                        or op.get("struct_type") != np.get("struct_type")
                        or op.get("obj_class") != np.get("obj_class")
                        or op.get("enum") != np.get("enum"))
        offset_changed = op.get("offset") != np.get("offset")
        size_changed = op.get("size") != np.get("size")
        if type_changed:
            changes.append(PropChange(name=name, kind="type_changed",
                                      old=op, new=np))
        elif offset_changed or size_changed:
            changes.append(PropChange(name=name, kind="moved",
                                      old=op, new=np))

    for name, np in new_props.items():
        if name not in old_props:
            changes.append(PropChange(name=name, kind="added", new=np))

    return changes


def _funcs_signature_differ(a: dict, b: dict) -> bool:
    """Functions' bodies aren't dumped, so 'signature' here means the
    metadata that's actually captured. parms_size + num_parms catch
    almost every param-shape change; return_type catches return-type
    refactors; flags catches Static/Native/BlueprintCallable toggles."""
    return (a.get("return_type") != b.get("return_type")
            or a.get("num_parms") != b.get("num_parms")
            or a.get("parms_size") != b.get("parms_size")
            or a.get("flags") != b.get("flags"))


def _diff_funcs(old_cls: dict, new_cls: dict) -> list[FuncChange]:
    old_funcs = {f["name"]: f for f in old_cls.get("funcs", []) if f.get("name")}
    new_funcs = {f["name"]: f for f in new_cls.get("funcs", []) if f.get("name")}

    changes: list[FuncChange] = []
    for name, of in old_funcs.items():
        nf = new_funcs.get(name)
        if nf is None:
            changes.append(FuncChange(name=name, kind="removed", old=of))
            continue
        if _funcs_signature_differ(of, nf):
            changes.append(FuncChange(name=name, kind="signature_changed",
                                      old=of, new=nf))
    for name, nf in new_funcs.items():
        if name not in old_funcs:
            changes.append(FuncChange(name=name, kind="added", new=nf))
    return changes


def diff_dumps(old_dump: Dump, new_dump: Dump,
               include_engine: bool = False) -> DumpDiff:
    old_idx = _index_classes(old_dump, include_engine)
    new_idx = _index_classes(new_dump, include_engine)

    out = DumpDiff(old_dump=old_dump, new_dump=new_dump)

    for path, old_cls in old_idx.items():
        if path not in new_idx:
            out.removed_classes.append(old_cls)

    for path, new_cls in new_idx.items():
        if path not in old_idx:
            out.added_classes.append(new_cls)
            continue
        old_cls = old_idx[path]
        prop_changes = _diff_props(old_cls, new_cls)
        func_changes = _diff_funcs(old_cls, new_cls)
        size_delta = new_cls.get("props_size", 0) - old_cls.get("props_size", 0)
        if prop_changes or func_changes or size_delta != 0:
            out.changed.append(ClassDiff(
                name=new_cls.get("name", old_cls.get("name", "")),
                path=path,
                old=old_cls,
                new=new_cls,
                prop_changes=prop_changes,
                func_changes=func_changes,
            ))
        else:
            out.unchanged_count += 1

    # Stable output ordering: alphabetic by path within each bucket.
    out.added_classes.sort(key=lambda c: normalize_path(c.get("path", "")))
    out.removed_classes.sort(key=lambda c: normalize_path(c.get("path", "")))
    out.changed.sort(key=lambda cd: cd.path)
    return out


# =====================================================================
# Markdown report
# =====================================================================

def _fmt_offset(v: int | None) -> str:
    if v is None:
        return "?"
    return f"0x{v:X}"


def _fmt_prop_typestr(p: dict) -> str:
    """Compact type label for a prop — includes inner / struct / object
    qualifier so type-changed entries are unambiguous."""
    t = p.get("type", "?")
    extras = []
    if p.get("inner_type"):
        extras.append(f"inner={p['inner_type']}")
    if p.get("struct_type"):
        extras.append(f"struct={p['struct_type']}")
    if p.get("obj_class"):
        extras.append(f"obj={p['obj_class']}")
    if p.get("enum"):
        extras.append(f"enum={p['enum']}")
    if extras:
        return f"{t} ({', '.join(extras)})"
    return t


def _count_changes(changed: list[ClassDiff]) -> dict[str, int]:
    out = {"prop_added": 0, "prop_removed": 0, "prop_moved": 0,
           "prop_type_changed": 0,
           "func_added": 0, "func_removed": 0, "func_signature_changed": 0,
           "classes_with_moved_fields": 0,
           "classes_with_sig_changes": 0,
           "classes_with_size_delta": 0}
    for cd in changed:
        had_move = False
        had_sig = False
        for pc in cd.prop_changes:
            if pc.kind == "added":         out["prop_added"] += 1
            elif pc.kind == "removed":     out["prop_removed"] += 1
            elif pc.kind == "moved":       out["prop_moved"] += 1; had_move = True
            elif pc.kind == "type_changed":out["prop_type_changed"] += 1; had_move = True
        for fc in cd.func_changes:
            if fc.kind == "added":              out["func_added"] += 1
            elif fc.kind == "removed":          out["func_removed"] += 1
            elif fc.kind == "signature_changed":out["func_signature_changed"] += 1; had_sig = True
        if had_move: out["classes_with_moved_fields"] += 1
        if had_sig:  out["classes_with_sig_changes"] += 1
        if cd.props_size_delta != 0: out["classes_with_size_delta"] += 1
    return out


def render_report(diff: DumpDiff, minimal: bool = False) -> str:
    old_d = diff.old_dump
    new_d = diff.new_dump
    counts = _count_changes(diff.changed)

    lines: list[str] = []
    lines.append("# Game Version Diff Report")
    lines.append("")
    lines.append(f"- **Old**: `{old_d.path.name}` "
                 f"(module={old_d.meta.get('module','?')}, "
                 f"UE={old_d.ue_version}, dumper build={old_d.dumper_build}, "
                 f"dumped {old_d.meta.get('dumped_at','?')})")
    lines.append(f"- **New**: `{new_d.path.name}` "
                 f"(module={new_d.meta.get('module','?')}, "
                 f"UE={new_d.ue_version}, dumper build={new_d.dumper_build}, "
                 f"dumped {new_d.meta.get('dumped_at','?')})")
    lines.append("")
    if minimal:
        lines.append("> **Minimal mode** — showing only the changes that "
                     "break existing cheat tables (moved fields + "
                     "signature changes). Added / removed entries are "
                     "hidden; full report omits the `--minimal` flag.")
        lines.append("")

    # Summary
    lines.append("## Summary")
    lines.append("")
    lines.append(f"- Classes: **{len(diff.added_classes)}** added, "
                 f"**{len(diff.removed_classes)}** removed, "
                 f"**{len(diff.changed)}** changed, "
                 f"**{diff.unchanged_count}** unchanged")
    lines.append(f"- Properties moved (offset / size): **{counts['prop_moved']}** "
                 f"across **{counts['classes_with_moved_fields']}** class(es)")
    lines.append(f"- Property type changed: **{counts['prop_type_changed']}**")
    lines.append(f"- Properties added: **{counts['prop_added']}**, "
                 f"removed: **{counts['prop_removed']}**")
    lines.append(f"- Function signatures changed: **{counts['func_signature_changed']}** "
                 f"across **{counts['classes_with_sig_changes']}** class(es)")
    lines.append(f"- Functions added: **{counts['func_added']}**, "
                 f"removed: **{counts['func_removed']}**")
    lines.append(f"- Classes with props_size delta: **{counts['classes_with_size_delta']}**")
    lines.append("")

    if not minimal:
        # Added classes
        if diff.added_classes:
            lines.append(f"## Added Classes ({len(diff.added_classes)})")
            lines.append("")
            for cls in diff.added_classes:
                lines.append(f"- `{cls.get('name','')}` — `{normalize_path(cls.get('path',''))}`")
            lines.append("")
        # Removed classes
        if diff.removed_classes:
            lines.append(f"## Removed Classes ({len(diff.removed_classes)})")
            lines.append("")
            for cls in diff.removed_classes:
                lines.append(f"- `{cls.get('name','')}` — `{normalize_path(cls.get('path',''))}`")
            lines.append("")

    # Per-class changes
    if minimal:
        emit = [cd for cd in diff.changed if cd.has_breaking_change]
        lines.append(f"## Breaking Changes ({len(emit)} class(es))")
    else:
        emit = list(diff.changed)
        lines.append(f"## Changed Classes ({len(emit)})")
    lines.append("")

    if not emit:
        lines.append("_No matching class diffs to report._")
        lines.append("")
        return "\n".join(lines)

    for cd in emit:
        lines.append(f"### `{cd.name}`")
        lines.append(f"_Path:_ `{cd.path}`")
        if cd.props_size_delta != 0:
            old_size = cd.old.get("props_size", 0)
            new_size = cd.new.get("props_size", 0)
            sign = "+" if cd.props_size_delta > 0 else ""
            lines.append(f"_props_size:_ {old_size} → {new_size} "
                         f"({sign}{cd.props_size_delta})")
        lines.append("")

        moved = [p for p in cd.prop_changes if p.kind == "moved"]
        type_changed = [p for p in cd.prop_changes if p.kind == "type_changed"]
        added_props = [p for p in cd.prop_changes if p.kind == "added"]
        removed_props = [p for p in cd.prop_changes if p.kind == "removed"]
        sig_changed = [f for f in cd.func_changes if f.kind == "signature_changed"]
        added_funcs = [f for f in cd.func_changes if f.kind == "added"]
        removed_funcs = [f for f in cd.func_changes if f.kind == "removed"]

        if moved:
            lines.append(f"**Moved fields ({len(moved)})** — *these break "
                         f"existing cheat tables*:")
            lines.append("")
            lines.append("| Field | Old offset → New | Old size → New |")
            lines.append("|---|---|---|")
            for pc in moved:
                o = pc.old or {}
                n = pc.new or {}
                lines.append(f"| `{pc.name}` | "
                             f"{_fmt_offset(o.get('offset'))} → {_fmt_offset(n.get('offset'))} | "
                             f"{o.get('size','?')} → {n.get('size','?')} |")
            lines.append("")

        if type_changed:
            lines.append(f"**Property type changed ({len(type_changed)})**:")
            lines.append("")
            for pc in type_changed:
                o = pc.old or {}
                n = pc.new or {}
                lines.append(f"- `{pc.name}` @ {_fmt_offset(o.get('offset'))}: "
                             f"{_fmt_prop_typestr(o)} → {_fmt_prop_typestr(n)}")
            lines.append("")

        if sig_changed:
            lines.append(f"**Function signatures changed ({len(sig_changed)})**:")
            lines.append("")
            lines.append("| Func | return | num_parms | parms_size | flags |")
            lines.append("|---|---|---|---|---|")
            for fc in sig_changed:
                o = fc.old or {}
                n = fc.new or {}
                def cell(k, fmt=lambda x: str(x) if x is not None else "?"):
                    a = o.get(k); b = n.get(k)
                    return f"{fmt(a)} → {fmt(b)}" if a != b else fmt(a)
                lines.append(f"| `{fc.name}` | {cell('return_type')} | "
                             f"{cell('num_parms')} | {cell('parms_size')} | "
                             f"{cell('flags')} |")
            lines.append("")

        if minimal:
            # Skip added / removed in minimal mode.
            continue

        if added_props:
            lines.append(f"**Added properties ({len(added_props)})**:")
            lines.append("")
            for pc in added_props:
                n = pc.new or {}
                lines.append(f"- `{pc.name}` ({_fmt_prop_typestr(n)}) "
                             f"@ {_fmt_offset(n.get('offset'))} "
                             f"({n.get('size','?')}B)")
            lines.append("")
        if removed_props:
            lines.append(f"**Removed properties ({len(removed_props)})**:")
            lines.append("")
            for pc in removed_props:
                o = pc.old or {}
                lines.append(f"- `{pc.name}` ({_fmt_prop_typestr(o)}) "
                             f"@ {_fmt_offset(o.get('offset'))}")
            lines.append("")
        if added_funcs:
            lines.append(f"**Added functions ({len(added_funcs)})**:")
            lines.append("")
            for fc in added_funcs:
                n = fc.new or {}
                lines.append(f"- `{fc.name}` (return={n.get('return_type','') or 'void'}, "
                             f"num_parms={n.get('num_parms','?')}, "
                             f"parms_size={n.get('parms_size','?')})")
            lines.append("")
        if removed_funcs:
            lines.append(f"**Removed functions ({len(removed_funcs)})**:")
            lines.append("")
            for fc in removed_funcs:
                o = fc.old or {}
                lines.append(f"- `{fc.name}` (return={o.get('return_type','') or 'void'}, "
                             f"num_parms={o.get('num_parms','?')}, "
                             f"parms_size={o.get('parms_size','?')})")
            lines.append("")

    return "\n".join(lines)


# =====================================================================
# Self-test — built-in synthetic fixtures so the diff logic is checkable
# without external dumps. Run via --self-test.
# =====================================================================

def _make_dump(module: str, classes: list[dict]) -> Dump:
    """Construct a fake Dump in-memory for self-test purposes."""
    d = Dump(path=Path(f"<{module}>"))
    d.meta = {"module": f"{module}.exe", "ue_version": 505,
              "dumper_build": 999, "dumped_at": "2026-01-01T00:00:00Z"}
    d.classes = classes
    return d


def _assert(cond: bool, label: str, errors: list[str]) -> None:
    if not cond:
        errors.append(label)


def run_self_test() -> int:
    errors: list[str] = []

    # Fixture: a class where Health moved + IsDead removed + NewField added.
    old = _make_dump("FakeGame", [
        {"kind": "class", "name": "AHero", "addr": "0x1",
         "path": "/Game/Heroes/AHero", "meta": "BlueprintGeneratedClass",
         "super": "ACharacter", "super_addr": "0x99", "is_bpgc": True,
         "props_size": 64, "instance_count": 1,
         "props": [
             {"name": "Health", "type": "FloatProperty", "offset": 0x40, "size": 4},
             {"name": "IsDead", "type": "BoolProperty", "offset": 0x44, "size": 1},
             {"name": "Mana",   "type": "FloatProperty", "offset": 0x48, "size": 4},
         ],
         "funcs": [
             {"name": "TakeDamage", "addr": "0xA", "return_type": "",
              "num_parms": 3, "parms_size": 12, "flags": "0x10"},
             {"name": "Die", "addr": "0xB", "return_type": "",
              "num_parms": 0, "parms_size": 0, "flags": "0x10"},
         ]},
        {"kind": "class", "name": "AOldThing", "addr": "0x2",
         "path": "/Game/Removed/AOldThing", "meta": "Class",
         "super": "", "super_addr": "0x0", "is_bpgc": False,
         "props_size": 16, "instance_count": 0,
         "props": [], "funcs": []},
    ])
    new = _make_dump("FakeGame", [
        {"kind": "class", "name": "AHero", "addr": "0xAA",
         "path": "/Game/Heroes/AHero", "meta": "BlueprintGeneratedClass",
         "super": "ACharacter", "super_addr": "0xBB", "is_bpgc": True,
         "props_size": 72, "instance_count": 1,
         "props": [
             # Health moved 0x40 -> 0x48
             {"name": "Health", "type": "FloatProperty", "offset": 0x48, "size": 4},
             # IsDead removed
             # Mana stayed (size matches), but its offset shifted 0x48 -> 0x4C
             {"name": "Mana",   "type": "FloatProperty", "offset": 0x4C, "size": 4},
             # NewField added
             {"name": "NewField", "type": "Int32Property", "offset": 0x50, "size": 4},
         ],
         "funcs": [
             # TakeDamage gained a param
             {"name": "TakeDamage", "addr": "0xCC", "return_type": "",
              "num_parms": 4, "parms_size": 16, "flags": "0x10"},
             # Die unchanged
             {"name": "Die", "addr": "0xDD", "return_type": "",
              "num_parms": 0, "parms_size": 0, "flags": "0x10"},
             # NewFunc added
             {"name": "Heal", "addr": "0xEE", "return_type": "",
              "num_parms": 1, "parms_size": 4, "flags": "0x10"},
         ]},
        {"kind": "class", "name": "ABrandNew", "addr": "0x3",
         "path": "/Game/NewStuff/ABrandNew", "meta": "Class",
         "super": "", "super_addr": "0x0", "is_bpgc": False,
         "props_size": 8, "instance_count": 0,
         "props": [], "funcs": []},
    ])

    diff = diff_dumps(old, new)

    # Class-level expectations
    _assert(len(diff.added_classes) == 1, "added_classes count", errors)
    _assert(diff.added_classes[0]["name"] == "ABrandNew",
            "added class name", errors)
    _assert(len(diff.removed_classes) == 1, "removed_classes count", errors)
    _assert(diff.removed_classes[0]["name"] == "AOldThing",
            "removed class name", errors)
    _assert(len(diff.changed) == 1, "changed count == 1", errors)

    cd = diff.changed[0]
    _assert(cd.name == "AHero", "changed class name", errors)
    _assert(cd.props_size_delta == 8, "props_size delta == 8", errors)

    # Prop-level expectations
    moved = [p for p in cd.prop_changes if p.kind == "moved"]
    added = [p for p in cd.prop_changes if p.kind == "added"]
    removed = [p for p in cd.prop_changes if p.kind == "removed"]
    type_changed = [p for p in cd.prop_changes if p.kind == "type_changed"]

    _assert(len(moved) == 2, f"2 moved props (got {len(moved)})", errors)
    moved_names = {p.name for p in moved}
    _assert(moved_names == {"Health", "Mana"},
            f"moved names = {moved_names}", errors)
    _assert(len(added) == 1 and added[0].name == "NewField",
            "1 added prop NewField", errors)
    _assert(len(removed) == 1 and removed[0].name == "IsDead",
            "1 removed prop IsDead", errors)
    _assert(len(type_changed) == 0, "no type_changed", errors)

    # Func-level expectations
    sig = [f for f in cd.func_changes if f.kind == "signature_changed"]
    f_added = [f for f in cd.func_changes if f.kind == "added"]
    f_removed = [f for f in cd.func_changes if f.kind == "removed"]
    _assert(len(sig) == 1 and sig[0].name == "TakeDamage",
            "TakeDamage signature changed", errors)
    _assert(len(f_added) == 1 and f_added[0].name == "Heal",
            "Heal added", errors)
    _assert(len(f_removed) == 0, "no removed funcs", errors)

    # has_breaking_change predicate: AHero qualifies (moved props)
    _assert(cd.has_breaking_change, "AHero has breaking change", errors)

    # --- Engine-class filter ---
    old_eng = _make_dump("FakeGame", [
        {"kind": "class", "name": "Object", "addr": "0x1",
         "path": "//Script/CoreUObject/Object", "meta": "Class",
         "super": "", "super_addr": "0x0", "is_bpgc": False,
         "props_size": 40, "instance_count": 1, "props": [], "funcs": []}
    ])
    new_eng = _make_dump("FakeGame", [
        {"kind": "class", "name": "Object", "addr": "0x1",
         "path": "//Script/CoreUObject/Object", "meta": "Class",
         "super": "", "super_addr": "0x0", "is_bpgc": False,
         "props_size": 48,  # size delta
         "instance_count": 1, "props": [], "funcs": []}
    ])
    d_skip_eng = diff_dumps(old_eng, new_eng, include_engine=False)
    _assert(d_skip_eng.unchanged_count == 0 and not d_skip_eng.changed
            and not d_skip_eng.added_classes and not d_skip_eng.removed_classes,
            "engine class skipped by default", errors)
    d_with_eng = diff_dumps(old_eng, new_eng, include_engine=True)
    _assert(len(d_with_eng.changed) == 1
            and d_with_eng.changed[0].props_size_delta == 8,
            "engine class diffed when --include-engine", errors)

    # --- A game's own C++ module is not engine ---
    # The game's native classes live under /Script/<GameModule> too. A default diff must report
    # them; only the engine's modules are skipped. A module whose name merely starts like an
    # engine module's is the game's as well.
    def _sized(name: str, path: str, size: int) -> dict:
        return {"kind": "class", "name": name, "addr": "0x1", "path": path, "meta": "Class",
                "super": "", "super_addr": "0x0", "is_bpgc": False, "props_size": size,
                "instance_count": 0, "props": [], "funcs": []}

    for label, path, engine in (
        ("game native module", "//Script/FakeGame/AHeroBase", False),
        ("module named like an engine one", "//Script/EngineOverride/Foo", False),
        ("engine module, single slash", "/Script/Engine/Actor", True),
        ("engine module, dotted", "//Script/UMG.UserWidget", True),
    ):
        d_mod = diff_dumps(_make_dump("FakeGame", [_sized("C", path, 16)]),
                           _make_dump("FakeGame", [_sized("C", path, 24)]),
                           include_engine=False)
        _assert(len(d_mod.changed) == (0 if engine else 1),
                f"{label}: {'skipped' if engine else 'diffed'} by default", errors)

    # --- Path normalization (//Script vs /Script) ---
    old_path = _make_dump("FakeGame", [
        {"kind": "class", "name": "X", "addr": "0x1",
         "path": "//Script/CoreUObject/X", "meta": "Class",
         "super": "", "super_addr": "0x0", "is_bpgc": False,
         "props_size": 16, "instance_count": 0, "props": [], "funcs": []}
    ])
    new_path = _make_dump("FakeGame", [
        {"kind": "class", "name": "X", "addr": "0x1",
         "path": "/Script/CoreUObject/X", "meta": "Class",
         "super": "", "super_addr": "0x0", "is_bpgc": False,
         "props_size": 16, "instance_count": 0, "props": [], "funcs": []}
    ])
    dp = diff_dumps(old_path, new_path, include_engine=True)
    _assert(dp.unchanged_count == 1 and not dp.changed
            and not dp.added_classes and not dp.removed_classes,
            "path normalization (//Script == /Script)", errors)

    # --- Type-changed detection (FloatProperty -> DoubleProperty) ---
    old_t = _make_dump("FakeGame", [
        {"kind": "class", "name": "AHP", "addr": "0x1",
         "path": "/Game/X/AHP", "meta": "BlueprintGeneratedClass",
         "super": "", "super_addr": "0x0", "is_bpgc": True,
         "props_size": 16, "instance_count": 0,
         "props": [{"name": "Val", "type": "FloatProperty",
                    "offset": 0x10, "size": 4}],
         "funcs": []}
    ])
    new_t = _make_dump("FakeGame", [
        {"kind": "class", "name": "AHP", "addr": "0x1",
         "path": "/Game/X/AHP", "meta": "BlueprintGeneratedClass",
         "super": "", "super_addr": "0x0", "is_bpgc": True,
         "props_size": 20, "instance_count": 0,
         "props": [{"name": "Val", "type": "DoubleProperty",
                    "offset": 0x10, "size": 8}],
         "funcs": []}
    ])
    dt = diff_dumps(old_t, new_t)
    tc = [p for p in dt.changed[0].prop_changes if p.kind == "type_changed"]
    _assert(len(tc) == 1 and tc[0].name == "Val",
            "type_changed detected", errors)

    # --- Self-diff produces zero changes (identity property) ---
    self_diff = diff_dumps(old, old)
    _assert(not self_diff.added_classes and not self_diff.removed_classes
            and not self_diff.changed,
            "self-diff returns empty", errors)

    # --- Minimal-mode predicate filters out add-only classes ---
    add_only = _make_dump("FakeGame", [
        {"kind": "class", "name": "C", "addr": "0x1",
         "path": "/Game/C/C", "meta": "Class",
         "super": "", "super_addr": "0x0", "is_bpgc": False,
         "props_size": 8, "instance_count": 0,
         "props": [{"name": "A", "type": "Int32Property", "offset": 0, "size": 4}],
         "funcs": []}
    ])
    add_only_new = _make_dump("FakeGame", [
        {"kind": "class", "name": "C", "addr": "0x1",
         "path": "/Game/C/C", "meta": "Class",
         "super": "", "super_addr": "0x0", "is_bpgc": False,
         "props_size": 16, "instance_count": 0,
         "props": [
             {"name": "A", "type": "Int32Property", "offset": 0, "size": 4},
             {"name": "B", "type": "Int32Property", "offset": 4, "size": 4},
         ],
         "funcs": []}
    ])
    da = diff_dumps(add_only, add_only_new)
    _assert(len(da.changed) == 1 and not da.changed[0].has_breaking_change,
            "add-only class is NOT breaking", errors)

    # --- Markdown rendering doesn't crash on edge cases ---
    md_full = render_report(diff, minimal=False)
    md_min  = render_report(diff, minimal=True)
    _assert("Game Version Diff Report" in md_full, "report header", errors)
    _assert("Moved fields" in md_full, "Moved-fields section", errors)
    _assert("Minimal mode" in md_min, "Minimal-mode banner", errors)
    _assert("Added Classes" not in md_min,
            "Minimal mode hides Added Classes", errors)

    if errors:
        print(f"SELF-TEST FAILED ({len(errors)} error(s)):", file=sys.stderr)
        for e in errors:
            print(f"  - {e}", file=sys.stderr)
        return 1
    print("self-test: all assertions passed.")
    return 0


# =====================================================================
# CLI
# =====================================================================

def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(
        description="Diff two `Dump All Metadata` JSONL files from the same game.")
    ap.add_argument("old", nargs="?", help="Older dump (.jsonl)")
    ap.add_argument("new", nargs="?", help="Newer dump (.jsonl)")
    ap.add_argument("-o", "--output", default=None,
                    help="Output Markdown path (default: stdout)")
    ap.add_argument("--minimal", action="store_true",
                    help="Emit only breaking changes (moved fields + "
                         "signature changes); skip added/removed lists.")
    ap.add_argument("--include-engine", action="store_true",
                    help="Include the engine's own modules in the diff "
                         "(default: skip — they rarely change between "
                         "game patches and dominate noise). The game's own "
                         "C++ classes are always included.")
    ap.add_argument("--self-test", action="store_true",
                    help="Run built-in synthetic-fixture tests and exit.")
    args = ap.parse_args(argv)

    if args.self_test:
        return run_self_test()

    if not args.old or not args.new:
        ap.error("old and new dump paths are required (or pass --self-test).")

    old_path = Path(args.old)
    new_path = Path(args.new)
    if not old_path.is_file():
        print(f"error: old dump not found: {old_path}", file=sys.stderr)
        return 2
    if not new_path.is_file():
        print(f"error: new dump not found: {new_path}", file=sys.stderr)
        return 2

    print(f"loading old: {old_path.name}", file=sys.stderr)
    old_dump = load_dump(old_path)
    print(f"  {len(old_dump.classes)} classes, {len(old_dump.errors)} errors",
          file=sys.stderr)
    print(f"loading new: {new_path.name}", file=sys.stderr)
    new_dump = load_dump(new_path)
    print(f"  {len(new_dump.classes)} classes, {len(new_dump.errors)} errors",
          file=sys.stderr)

    # Sanity: warn if modules differ. Diffing across games is technically
    # supported but the output is mostly noise — almost every class will
    # be added/removed.
    om = old_dump.meta.get("module", "")
    nm = new_dump.meta.get("module", "")
    if om and nm and om != nm:
        print(f"  [warn] module names differ: {om!r} vs {nm!r}. "
              f"This diff tool is intended for same-game patch comparison; "
              f"output may be mostly noise.", file=sys.stderr)

    diff = diff_dumps(old_dump, new_dump, include_engine=args.include_engine)
    md = render_report(diff, minimal=args.minimal)

    if args.output:
        Path(args.output).write_text(md, encoding="utf-8")
        print(f"wrote: {args.output}", file=sys.stderr)
    else:
        sys.stdout.write(md)
        sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
