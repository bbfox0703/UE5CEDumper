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
    1. Loads two JSONL dumps. Each dump = meta line + class, struct and
       enum lines + summary line (see DumpAllService.cs schema, same as
       analyze_dumps).
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
             num_parms, parms_size or flags differs, or — when both
             files carry them — the parameters; body content isn't in
             the dump)
    4. Structs the same way, without functions. Enums by path: added /
       removed, and per enum the enumerators added / removed / whose
       value changed. Neither is compared when a dump predates those
       lines (else everything reads as added), nor enums when a dump's
       enum list could not be read; the summary line's flags decide.
    5. Emits a Markdown report:
         - Summary counters
         - Added / Removed classes, structs and enums
         - Per-changed-type breakdown of property + function changes,
           and per changed enum its enumerators
       In `--minimal` mode emits ONLY moved fields, signature changes
       and changed enum values — the subset cheat-table maintainers
       care about because those are the changes that silently break a
       working table.

NOT IN SCOPE
    - Rename detection (renamed class shows as Removed + Added; same
      for renamed field). Documented limitation. Use a manual grep
      pass on the report if you suspect a rename.
    - Function body comparison. Dumps capture only function metadata
      (return_type, num_parms, parms_size, flags) and the parameters — the bytecode +
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
    structs: list[dict] = field(default_factory=list)
    enums: list[dict] = field(default_factory=list)
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

    # [EXTPR-539-540-2026-10-02] What the file can say about structs and enums. Dump All writes struct lines
    # from build 3620 and enum lines from 3621, and its summary names both from then on; a file without
    # either has none to compare, which is not the same as having none.
    @property
    def carries_structs(self) -> bool:
        return bool(self.structs) or "structs_emitted" in self.summary

    @property
    def carries_enums(self) -> bool:
        return bool(self.enums) or "enums_emitted" in self.summary or "enums_listed" in self.summary

    @property
    def enums_listed(self) -> bool:
        return bool(self.summary.get("enums_listed", True))

    @property
    def enum_names_failed(self) -> bool:
        return bool(self.summary.get("enum_names_failed", False))

    @property
    def enums_truncated(self) -> bool:
        return bool(self.summary.get("enums_truncated", False))

    @property
    def params_from_num_parms(self) -> int:
        return int(self.summary.get("params_from_num_parms", 0))


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
            elif kind == "struct":
                d.structs.append(rec)
            elif kind == "enum":
                d.enums.append(rec)
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
class EnumEntryChange:
    name: str
    # 'added' | 'removed' | 'value_changed'
    kind: str
    old_value: int | None = None
    new_value: int | None = None


@dataclass
class EnumDiff:
    name: str
    path: str
    changes: list[EnumEntryChange] = field(default_factory=list)

    @property
    def has_breaking_change(self) -> bool:
        """A table that writes an enum's value breaks when the value moves; an enumerator added or removed
        does not move the others."""
        return any(c.kind == "value_changed" for c in self.changes)


@dataclass
class DumpDiff:
    old_dump: Dump
    new_dump: Dump
    added_classes: list[dict] = field(default_factory=list)
    removed_classes: list[dict] = field(default_factory=list)
    changed: list[ClassDiff] = field(default_factory=list)
    unchanged_count: int = 0
    # [EXTPR-539-540-2026-10-02] D5. A *_skipped reason is "" when that kind was compared.
    added_structs: list[dict] = field(default_factory=list)
    removed_structs: list[dict] = field(default_factory=list)
    changed_structs: list[ClassDiff] = field(default_factory=list)
    unchanged_structs: int = 0
    structs_skipped: str = ""
    added_enums: list[dict] = field(default_factory=list)
    removed_enums: list[dict] = field(default_factory=list)
    changed_enums: list[EnumDiff] = field(default_factory=list)
    unchanged_enums: int = 0
    enums_skipped: str = ""
    enum_notes: list[str] = field(default_factory=list)
    param_notes: list[str] = field(default_factory=list)


# =====================================================================
# Core diff
# =====================================================================

def _index_classes(dump: Dump, include_engine: bool) -> dict[str, dict]:
    return _index_by_path(dump.classes, dump.label, include_engine, "class")


def _index_by_path(records: list[dict], label: str, include_engine: bool, what: str) -> dict[str, dict]:
    """path -> record (a class, struct or enum line). Drops engine types unless requested.

    Duplicate paths in a single dump are highly unusual (would indicate
    a dumper bug — same UClass walked twice). We keep the FIRST and
    log a warning so the diff stays deterministic; analyst can grep
    the source dump if the warning fires."""
    out: dict[str, dict] = {}
    for cls in records:
        if not include_engine and is_engine_class(cls):
            continue
        key = normalize_path(cls.get("path", ""))
        if not key:
            # Fallback: name-only key. Reduces match precision (engine
            # inner classes collide) but better than dropping the row.
            key = "::name::" + cls.get("name", "")
        if key in out:
            print(f"  [warn] duplicate {what} path in {label}: {key} — "
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
    refactors; flags catches Static/Native/BlueprintCallable toggles;
    the parameters catch the rest (a renamed or retyped one) when both
    files carry them."""
    return (a.get("return_type") != b.get("return_type")
            or a.get("num_parms") != b.get("num_parms")
            or a.get("parms_size") != b.get("parms_size")
            or a.get("flags") != b.get("flags")
            or _params_differ(a, b))


def _params_key(f: dict) -> tuple:
    return tuple((p.get("name"), p.get("type"), p.get("struct_type"), p.get("obj_class"),
                  bool(p.get("out")), bool(p.get("ret")), p.get("offset"), p.get("size"))
                 for p in f.get("params", []))


def _params_differ(a: dict, b: dict) -> bool:
    """[EXTPR-539-540-2026-10-02] D5. Only when both sides carry `params` (Dump All from build 3622): an
    older file has none, and that is not a change."""
    return "params" in a and "params" in b and _params_key(a) != _params_key(b)


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


def _diff_types(old_records: list[dict], new_records: list[dict], old_label: str, new_label: str,
                include_engine: bool, what: str):
    """Classes and structs share a shape (a struct has no functions): (added, removed, changed, unchanged)."""
    old_idx = _index_by_path(old_records, old_label, include_engine, what)
    new_idx = _index_by_path(new_records, new_label, include_engine, what)
    added: list[dict] = []
    removed = [rec for path, rec in old_idx.items() if path not in new_idx]
    changed: list[ClassDiff] = []
    unchanged = 0
    for path, new_cls in new_idx.items():
        if path not in old_idx:
            added.append(new_cls)
            continue
        old_cls = old_idx[path]
        prop_changes = _diff_props(old_cls, new_cls)
        func_changes = _diff_funcs(old_cls, new_cls)
        size_delta = new_cls.get("props_size", 0) - old_cls.get("props_size", 0)
        if prop_changes or func_changes or size_delta != 0:
            changed.append(ClassDiff(
                name=new_cls.get("name", old_cls.get("name", "")),
                path=path,
                old=old_cls,
                new=new_cls,
                prop_changes=prop_changes,
                func_changes=func_changes,
            ))
        else:
            unchanged += 1

    # Stable output ordering: alphabetic by path within each bucket.
    added.sort(key=lambda c: normalize_path(c.get("path", "")))
    removed.sort(key=lambda c: normalize_path(c.get("path", "")))
    changed.sort(key=lambda cd: cd.path)
    return added, removed, changed, unchanged


def _diff_enum_entries(old_e: dict, new_e: dict) -> list[EnumEntryChange]:
    old_v = {x["name"]: x.get("value") for x in old_e.get("entries", []) if x.get("name")}
    new_v = {x["name"]: x.get("value") for x in new_e.get("entries", []) if x.get("name")}
    changes: list[EnumEntryChange] = []
    for name, v in old_v.items():
        if name not in new_v:
            changes.append(EnumEntryChange(name=name, kind="removed", old_value=v))
        elif new_v[name] != v:
            changes.append(EnumEntryChange(name=name, kind="value_changed", old_value=v, new_value=new_v[name]))
    for name, v in new_v.items():
        if name not in old_v:
            changes.append(EnumEntryChange(name=name, kind="added", new_value=v))
    return changes


def _skip_reason(old_dump: Dump, new_dump: Dump, what: str) -> str:
    """Why structs or enums cannot be compared, or "" when they can."""
    sides = (("old", old_dump), ("new", new_dump))
    if what == "struct":
        for label, d in sides:
            if not d.carries_structs:
                return f"the {label} dump has no struct lines (Dump All writes them from build 3620)"
        return ""
    for label, d in sides:
        if not d.carries_enums:
            return f"the {label} dump has no enum lines (Dump All writes them from build 3621)"
    for label, d in sides:
        if not d.enums_listed:
            return f"the {label} dump's enum list could not be read (see its list_enums error line)"
    return ""


def _diff_enums(out: DumpDiff, old_dump: Dump, new_dump: Dump, include_engine: bool) -> None:
    """[EXTPR-539-540-2026-10-02] D5. What a list could not say is not reported as a change: with no
    member names an enum's entries are empty, and a cut-short list lacks enums it never reached."""
    names_ok = True
    for label, d in (("old", old_dump), ("new", new_dump)):
        if d.enum_names_failed:
            names_ok = False
            out.enum_notes.append(f"enum member names were unavailable in the {label} dump, so enumerators "
                                  f"are not compared")
    if old_dump.enums_truncated:
        out.enum_notes.append("the old dump's enum list was cut short, so an enum it lacks is not reported as added")
    if new_dump.enums_truncated:
        out.enum_notes.append("the new dump's enum list was cut short, so an enum it lacks is not reported as removed")

    old_idx = _index_by_path(old_dump.enums, old_dump.label, include_engine, "enum")
    new_idx = _index_by_path(new_dump.enums, new_dump.label, include_engine, "enum")
    if not new_dump.enums_truncated:
        out.removed_enums = [e for path, e in old_idx.items() if path not in new_idx]
    for path, ne in new_idx.items():
        if path not in old_idx:
            if not old_dump.enums_truncated:
                out.added_enums.append(ne)
            continue
        changes = _diff_enum_entries(old_idx[path], ne) if names_ok else []
        if changes:
            out.changed_enums.append(EnumDiff(name=ne.get("name", ""), path=path, changes=changes))
        else:
            out.unchanged_enums += 1
    out.added_enums.sort(key=lambda e: normalize_path(e.get("path", "")))
    out.removed_enums.sort(key=lambda e: normalize_path(e.get("path", "")))
    out.changed_enums.sort(key=lambda ed: ed.path)


def diff_dumps(old_dump: Dump, new_dump: Dump,
               include_engine: bool = False) -> DumpDiff:
    out = DumpDiff(old_dump=old_dump, new_dump=new_dump)
    (out.added_classes, out.removed_classes, out.changed,
     out.unchanged_count) = _diff_types(old_dump.classes, new_dump.classes, old_dump.label, new_dump.label,
                                        include_engine, "class")

    out.structs_skipped = _skip_reason(old_dump, new_dump, "struct")
    if not out.structs_skipped:
        (out.added_structs, out.removed_structs, out.changed_structs,
         out.unchanged_structs) = _diff_types(old_dump.structs, new_dump.structs, old_dump.label,
                                              new_dump.label, include_engine, "struct")

    out.enums_skipped = _skip_reason(old_dump, new_dump, "enum")
    if not out.enums_skipped:
        _diff_enums(out, old_dump, new_dump, include_engine)

    for label, d in (("old", old_dump), ("new", new_dump)):
        if d.params_from_num_parms:
            out.param_notes.append(
                f"{d.params_from_num_parms} function(s) in the {label} dump had their parameters taken from "
                f"num_parms (a DLL older than build 3622), so a parameter change there may be that approximation")
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


def _fmt_params(f: dict) -> str:
    """A function's arguments, an out one marked and each with its offset; the return is left out (the
    table's return column has it)."""
    parts = []
    for p in f.get("params", []):
        if p.get("ret"):
            continue
        t = p.get("type", "?")
        if p.get("struct_type"):
            t += f"<{p['struct_type']}>"
        if p.get("obj_class"):
            t += f":{p['obj_class']}"
        out = "out " if p.get("out") else ""
        parts.append(f"{out}{t} {p.get('name', '?')}@{_fmt_offset(p.get('offset'))}")
    return "(" + ", ".join(parts) + ")"


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
    if diff.structs_skipped:
        lines.append(f"- Structs: not compared — {diff.structs_skipped}")
    else:
        scounts = _count_changes(diff.changed_structs)
        lines.append(f"- Structs: **{len(diff.added_structs)}** added, "
                     f"**{len(diff.removed_structs)}** removed, "
                     f"**{len(diff.changed_structs)}** changed, "
                     f"**{diff.unchanged_structs}** unchanged")
        lines.append(f"- Struct fields moved (offset / size): **{scounts['prop_moved']}**, "
                     f"type changed: **{scounts['prop_type_changed']}**, "
                     f"across **{scounts['classes_with_moved_fields']}** struct(s)")
    if diff.enums_skipped:
        lines.append(f"- Enums: not compared — {diff.enums_skipped}")
    else:
        values = sum(1 for ed in diff.changed_enums for c in ed.changes if c.kind == "value_changed")
        lines.append(f"- Enums: **{len(diff.added_enums)}** added, "
                     f"**{len(diff.removed_enums)}** removed, "
                     f"**{len(diff.changed_enums)}** changed, "
                     f"**{diff.unchanged_enums}** unchanged")
        lines.append(f"- Enumerator values changed: **{values}** across "
                     f"**{sum(1 for ed in diff.changed_enums if ed.has_breaking_change)}** enum(s)")
    for note in diff.enum_notes + diff.param_notes:
        lines.append(f"- ⚠ {note}")
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
    for cd in emit:
        _render_type_diff(lines, cd, minimal)

    if not diff.structs_skipped:
        _render_structs(lines, diff, minimal)
    if not diff.enums_skipped:
        _render_enums(lines, diff, minimal)
    return "\n".join(lines)


def _render_listing(lines: list[str], title: str, records: list[dict]) -> None:
    if records:
        lines.append(f"## {title} ({len(records)})")
        lines.append("")
        for rec in records:
            lines.append(f"- `{rec.get('name','')}` — `{normalize_path(rec.get('path',''))}`")
        lines.append("")


def _render_structs(lines: list[str], diff: DumpDiff, minimal: bool) -> None:
    """[EXTPR-539-540-2026-10-02] D5: structs, laid out as the classes are."""
    if not minimal:
        _render_listing(lines, "Added Structs", diff.added_structs)
        _render_listing(lines, "Removed Structs", diff.removed_structs)
        emit = list(diff.changed_structs)
        lines.append(f"## Changed Structs ({len(emit)})")
    else:
        emit = [cd for cd in diff.changed_structs if cd.has_breaking_change]
        lines.append(f"## Breaking Struct Changes ({len(emit)} struct(s))")
    lines.append("")
    if not emit:
        lines.append("_No matching struct diffs to report._")
        lines.append("")
    for cd in emit:
        _render_type_diff(lines, cd, minimal)


def _render_enums(lines: list[str], diff: DumpDiff, minimal: bool) -> None:
    """[EXTPR-539-540-2026-10-02] D5: enums, each changed one with its enumerators."""
    if not minimal:
        _render_listing(lines, "Added Enums", diff.added_enums)
        _render_listing(lines, "Removed Enums", diff.removed_enums)
        emit = list(diff.changed_enums)
        lines.append(f"## Changed Enums ({len(emit)})")
    else:
        emit = [ed for ed in diff.changed_enums if ed.has_breaking_change]
        lines.append(f"## Changed Enum Values ({len(emit)} enum(s))")
    lines.append("")
    if not emit:
        lines.append("_No matching enum diffs to report._")
        lines.append("")
    for ed in emit:
        lines.append(f"### `{ed.name}`")
        lines.append(f"_Path:_ `{ed.path}`")
        lines.append("")
        values = [c for c in ed.changes if c.kind == "value_changed"]
        if values:
            lines.append("| Enumerator | Old value → New |")
            lines.append("|---|---|")
            for c in values:
                lines.append(f"| `{c.name}` | {c.old_value} → {c.new_value} |")
            lines.append("")
        if minimal:
            continue
        for kind, title, attr in (("added", "Added enumerators", "new_value"),
                                  ("removed", "Removed enumerators", "old_value")):
            rows = [c for c in ed.changes if c.kind == kind]
            if rows:
                lines.append(f"**{title} ({len(rows)})**:")
                lines.append("")
                for c in rows:
                    lines.append(f"- `{c.name}` = {getattr(c, attr)}")
                lines.append("")


def _render_type_diff(lines: list[str], cd: ClassDiff, minimal: bool) -> None:
    """One changed class or struct: its moved and retyped fields, its signature changes, and (outside
    --minimal) its added and removed members."""
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
        param_rows = [fc for fc in sig_changed if _params_differ(fc.old or {}, fc.new or {})]
        for fc in param_rows:
            lines.append(f"- `{fc.name}` parameters: `{_fmt_params(fc.old or {})}` → "
                         f"`{_fmt_params(fc.new or {})}`")
        if param_rows:
            lines.append("")

    if minimal:
        # Skip added / removed in minimal mode.
        return

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


# =====================================================================
# Self-test — built-in synthetic fixtures so the diff logic is checkable
# without external dumps. Run via --self-test.
# =====================================================================

def _make_dump(module: str, classes: list[dict], structs: list[dict] | None = None,
               enums: list[dict] | None = None, summary: dict | None = None, complete: bool = True) -> Dump:
    """Construct a fake Dump in-memory for self-test purposes. complete=False: as if cut off before its
    summary line."""
    d = Dump(path=Path(f"<{module}>"))
    d.has_summary = complete
    d.meta = {"module": f"{module}.exe", "ue_version": 505,
              "dumper_build": 999, "dumped_at": "2026-01-01T00:00:00Z"}
    d.classes = classes
    d.structs = structs or []
    d.enums = enums or []
    d.summary = summary or {}
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

    run_self_test_types(errors)
    run_self_test_review(errors)

    if errors:
        print(f"SELF-TEST FAILED ({len(errors)} error(s)):", file=sys.stderr)
        for e in errors:
            print(f"  - {e}", file=sys.stderr)
        return 1
    print("self-test: all assertions passed.")
    return 0


# ---------------------------------------------------------------------
# [EXTPR-539-540-2026-10-02] D5: structs, enums and function params.
# Kept apart from run_self_test so the class fixtures above stay as they were.
# ---------------------------------------------------------------------

def _struct(name: str, path: str, size: int, props: list[dict]) -> dict:
    return {"kind": "struct", "name": name, "addr": "0x5", "path": path, "meta": "ScriptStruct",
            "super": "", "super_addr": "", "props_size": size, "props": props}


def _enum(name: str, path: str, entries: list[tuple[str, int]]) -> dict:
    return {"kind": "enum", "name": name, "addr": "0x6", "path": path,
            "entries": [{"name": n, "value": v} for n, v in entries]}


def _summary(**flags) -> dict:
    """A summary line from build 3622 on: it names structs, enums and the enum list's flags."""
    s = {"kind": "summary", "classes_emitted": 0, "structs_emitted": 0, "enums_emitted": 0,
         "enums_listed": True, "enum_names_failed": False, "enums_truncated": False,
         "params_from_num_parms": 0}
    s.update(flags)
    return s


def _func(name: str, params: list[dict] | None, num_parms: int = 2, parms_size: int = 8) -> dict:
    f = {"name": name, "addr": "0x7", "return_type": "", "num_parms": num_parms,
         "parms_size": parms_size, "flags": "0x10"}
    if params is not None:
        f["params"] = params
    return f


def _cls_with(funcs: list[dict]) -> dict:
    return {"kind": "class", "name": "AHero", "addr": "0x1", "path": "/Game/Heroes/AHero",
            "meta": "BlueprintGeneratedClass", "super": "", "super_addr": "", "is_bpgc": True,
            "props_size": 8, "instance_count": 0, "props": [], "funcs": funcs}


def run_self_test_types(errors: list[str]) -> None:
    # --- Structs: added, removed, and a field that moved ---
    s_old = _make_dump("FakeGame", [], structs=[
        _struct("FHit", "/Script/FakeGame.FHit", 8, [
            {"name": "A", "type": "IntProperty", "offset": 0, "size": 4},
            {"name": "B", "type": "IntProperty", "offset": 4, "size": 4}]),
        _struct("FGone", "/Game/Data/FGone.FGone", 4, []),
    ], summary=_summary(structs_emitted=2))
    s_new = _make_dump("FakeGame", [], structs=[
        _struct("FHit", "/Script/FakeGame.FHit", 12, [
            {"name": "A", "type": "IntProperty", "offset": 0, "size": 4},
            {"name": "B", "type": "IntProperty", "offset": 8, "size": 4}]),
        _struct("FNew", "/Game/Data/FNew.FNew", 4, []),
    ], summary=_summary(structs_emitted=2))
    ds = diff_dumps(s_old, s_new)
    _assert(ds.structs_skipped == "", "structs compared when both dumps carry them", errors)
    _assert([s["name"] for s in ds.added_structs] == ["FNew"], "added struct FNew", errors)
    _assert([s["name"] for s in ds.removed_structs] == ["FGone"], "removed struct FGone", errors)
    _assert(len(ds.changed_structs) == 1 and ds.changed_structs[0].name == "FHit", "changed struct FHit", errors)
    if ds.changed_structs:
        moved = [p.name for p in ds.changed_structs[0].prop_changes if p.kind == "moved"]
        _assert(moved == ["B"], f"FHit.B moved (got {moved})", errors)
        _assert(ds.changed_structs[0].has_breaking_change, "a moved struct field is breaking", errors)
    md = render_report(ds)
    _assert("Changed Structs" in md and "Added Structs" in md and "Removed Structs" in md,
            "report has the struct sections", errors)
    _assert("`FHit`" in md, "report names the changed struct", errors)
    md_min = render_report(ds, minimal=True)
    _assert("`FHit`" in md_min and "Added Structs" not in md_min,
            "minimal report keeps the moved struct, hides the added one", errors)

    # --- Structs: an older dump has none, so nothing is "added" ---
    s_pre = _make_dump("FakeGame", [], summary={"kind": "summary", "classes_emitted": 0})
    dp = diff_dumps(s_pre, s_new)
    _assert(dp.structs_skipped != "" and not dp.added_structs,
            "a dump from before struct lines skips the struct comparison", errors)
    _assert("not compared" in render_report(dp), "the report says structs were not compared", errors)

    # --- Structs: an engine struct is skipped by default ---
    e_old = _make_dump("FakeGame", [], structs=[_struct("Vector", "/Script/CoreUObject.Vector", 12, [])],
                       summary=_summary(structs_emitted=1))
    e_new = _make_dump("FakeGame", [], structs=[_struct("Vector", "/Script/CoreUObject.Vector", 24, [])],
                       summary=_summary(structs_emitted=1))
    _assert(not diff_dumps(e_old, e_new).changed_structs, "engine struct skipped by default", errors)
    _assert(len(diff_dumps(e_old, e_new, include_engine=True).changed_structs) == 1,
            "engine struct diffed with --include-engine", errors)

    # --- Enums: a value changed, an enumerator added, an enum added and one removed ---
    n_old = _make_dump("FakeGame", [], enums=[
        _enum("EKind", "/Script/FakeGame.EKind", [("EKind::A", 0), ("EKind::B", 1)]),
        _enum("EOld", "/Game/Data/EOld.EOld", [("NewEnumerator0", 0)]),
    ], summary=_summary(enums_emitted=2))
    n_new = _make_dump("FakeGame", [], enums=[
        _enum("EKind", "/Script/FakeGame.EKind", [("EKind::A", 0), ("EKind::B", 2), ("EKind::C", 3)]),
        _enum("ENew", "/Game/Data/ENew.ENew", [("NewEnumerator0", 0)]),
    ], summary=_summary(enums_emitted=2))
    dn = diff_dumps(n_old, n_new)
    _assert(dn.enums_skipped == "", "enums compared when both lists were read", errors)
    _assert([e["name"] for e in dn.added_enums] == ["ENew"], "added enum ENew", errors)
    _assert([e["name"] for e in dn.removed_enums] == ["EOld"], "removed enum EOld", errors)
    _assert(len(dn.changed_enums) == 1 and dn.changed_enums[0].name == "EKind", "changed enum EKind", errors)
    if dn.changed_enums:
        kinds = sorted((c.name, c.kind) for c in dn.changed_enums[0].changes)
        _assert(kinds == [("EKind::B", "value_changed"), ("EKind::C", "added")],
                f"EKind's changes (got {kinds})", errors)
    md = render_report(dn)
    _assert("Changed Enums" in md and "EKind::B" in md, "report names the changed enumerator", errors)
    _assert("EKind::B" in render_report(dn, minimal=True), "a changed enum value is breaking", errors)
    _assert(not diff_dumps(n_old, n_old).changed_enums, "enum self-diff is empty", errors)

    # --- Enums: a list that could not be read is not compared ---
    n_failed = _make_dump("FakeGame", [], enums=[], summary=_summary(enums_listed=False))
    df = diff_dumps(n_old, n_failed)
    _assert(df.enums_skipped != "" and not df.removed_enums,
            "a failed enum list skips the comparison, so no enum reads as removed", errors)

    # --- Enums: member names unavailable -> entries not compared, presence still is ---
    n_noname = _make_dump("FakeGame", [], enums=[
        _enum("EKind", "/Script/FakeGame.EKind", []),
        _enum("ENew", "/Game/Data/ENew.ENew", []),
    ], summary=_summary(enums_emitted=2, enum_names_failed=True))
    dnn = diff_dumps(n_old, n_noname)
    _assert(not dnn.changed_enums, "with no member names, an enum's entries are not compared", errors)
    _assert([e["name"] for e in dnn.added_enums] == ["ENew"], "an enum's presence is still compared", errors)
    _assert(any("names" in note for note in dnn.enum_notes), "the report says member names were unavailable", errors)

    # --- Enums: a cut-short list cannot say an enum was removed ---
    n_cut = _make_dump("FakeGame", [], enums=[
        _enum("EKind", "/Script/FakeGame.EKind", [("EKind::A", 0), ("EKind::B", 1)]),
    ], summary=_summary(enums_emitted=1, enums_truncated=True))
    dc = diff_dumps(n_old, n_cut)
    _assert(not dc.removed_enums, "a truncated new list reports no removed enum", errors)
    _assert(any("cut short" in note for note in dc.enum_notes), "the report says the list was cut short", errors)

    # --- Enums: an older dump has none, so nothing is "added" ---
    dpe = diff_dumps(_make_dump("FakeGame", [], summary={"kind": "summary"}), n_new)
    _assert(dpe.enums_skipped != "" and not dpe.added_enums,
            "a dump from before enum lines skips the enum comparison", errors)

    # --- Function params: a parameter change is a signature change; one missing side compares metadata ---
    p_a = [{"name": "Amount", "type": "FloatProperty", "offset": 0, "size": 4},
           {"name": "Source", "type": "ObjectProperty", "offset": 8, "size": 8, "obj_class": "Actor"}]
    p_b = [{"name": "Amount", "type": "FloatProperty", "offset": 0, "size": 4},
           {"name": "Source", "type": "ObjectProperty", "offset": 8, "size": 8, "obj_class": "Pawn"}]
    f_old = _make_dump("FakeGame", [_cls_with([_func("TakeDamage", p_a)])], summary=_summary())
    f_new = _make_dump("FakeGame", [_cls_with([_func("TakeDamage", p_b)])], summary=_summary())
    dfp = diff_dumps(f_old, f_new)
    sig = [f for cd in dfp.changed for f in cd.func_changes if f.kind == "signature_changed"]
    _assert(len(sig) == 1 and sig[0].name == "TakeDamage",
            "a parameter whose class changed is a signature change", errors)
    _assert("Source" in render_report(dfp), "the report shows the parameters that changed", errors)
    f_pre = _make_dump("FakeGame", [_cls_with([_func("TakeDamage", None)])], summary={"kind": "summary"})
    _assert(not diff_dumps(f_pre, f_new).changed,
            "an older dump without params compares the function's metadata only", errors)


def run_self_test_review(errors: list[str]) -> None:
    """[EXTPR-539-540-2026-10-02] Review of 513fdd16: what the first D5 self-test left unpinned."""
    # --- a dump with no summary line was cut off mid-write: what it lacks is not "removed" ---
    full = _make_dump("FakeGame", [_cls_with([]), {**_cls_with([]), "name": "AOther", "path": "/Game/AOther"}],
                      structs=[_struct("FHit", "/Script/FakeGame.FHit", 8, [])],
                      enums=[_enum("EKind", "/Script/FakeGame.EKind", [("EKind::A", 0)])],
                      summary=_summary(structs_emitted=1, enums_emitted=1))
    cut = _make_dump("FakeGame", [_cls_with([])], complete=False)
    cut.meta["dumper_build"] = 3622
    dcut = diff_dumps(full, cut)
    _assert(not dcut.removed_classes, "a cut-off new dump reports no removed class", errors)
    md = render_report(dcut)
    _assert("no summary line" in md, "the report says the new dump was cut off", errors)
    _assert("ends before its struct lines" in md, "a cut-off dump of a build with structs says so", errors)
    dcut_old = diff_dumps(cut, full)
    _assert(not dcut_old.added_classes, "a cut-off old dump reports no added class", errors)

    # --- a type whose walk failed is tagged, not just "removed" ---
    failed = _make_dump("FakeGame", [_cls_with([])], structs=[], summary=_summary(structs_emitted=0))
    failed.errors = [{"kind": "error", "addr": "0x9", "name": "AOther", "msg": "pipe dropped"}]
    md = render_report(diff_dumps(full, failed))
    _assert("walk failed in the new dump" in md, "a removed class with an error line is tagged", errors)
    _assert("error line(s)" in md, "the report counts each dump's error lines", errors)

    # --- the report's own wording, not the notes list ---
    md = render_report(diff_dumps(_make_dump("FakeGame", [], summary={"kind": "summary"}),
                                  _make_dump("FakeGame", [], structs=[_struct("FHit", "/Script/G.FHit", 8, [])],
                                             summary=_summary(structs_emitted=1))))
    _assert("Structs: not compared" in md, "the report says structs were not compared", errors)
    _assert("Enums: not compared" in md, "the report says enums were not compared", errors)
    n_old = _make_dump("FakeGame", [], enums=[_enum("EKind", "/Script/G.EKind", [("EKind::A", 0)])],
                       summary=_summary(enums_emitted=1))
    n_noname = _make_dump("FakeGame", [], enums=[_enum("EKind", "/Script/G.EKind", [])],
                          summary=_summary(enums_emitted=1, enum_names_failed=True))
    md = render_report(diff_dumps(n_old, n_noname))
    _assert("member names were unavailable" in md, "the rendered report carries the names note", errors)
    _assert("entries not compared" in md and "1** unchanged" not in md,
            "enums matched without names are not counted as unchanged", errors)

    # --- an old cut-short list cannot say an enum was added ---
    n_cut_old = _make_dump("FakeGame", [], enums=[_enum("EKind", "/Script/G.EKind", [("EKind::A", 0)])],
                           summary=_summary(enums_emitted=1, enums_truncated=True))
    n_more = _make_dump("FakeGame", [], enums=[_enum("EKind", "/Script/G.EKind", [("EKind::A", 0)]),
                                               _enum("ENew", "/Script/G.ENew", [("ENew::A", 0)])],
                        summary=_summary(enums_emitted=2))
    _assert(not diff_dumps(n_cut_old, n_more).added_enums, "a truncated old list reports no added enum", errors)

    # --- params: offset-only, out-only and return-only changes are signature changes, and the report shows them ---
    base = [{"name": "Amount", "type": "FloatProperty", "offset": 0, "size": 4},
            {"name": "ReturnValue", "type": "ObjectProperty", "offset": 8, "size": 8, "obj_class": "Actor",
             "out": True, "ret": True}]
    for label, change in (("offset", {0: {"offset": 4}}),
                          ("out", {0: {"out": True}}),
                          ("return class", {1: {"obj_class": "Pawn"}})):
        changed = [dict(p, **change.get(i, {})) for i, p in enumerate(base)]
        d = diff_dumps(_make_dump("FakeGame", [_cls_with([_func("Spawn", base)])], summary=_summary()),
                       _make_dump("FakeGame", [_cls_with([_func("Spawn", changed)])], summary=_summary()))
        sig = [f for cd in d.changed for f in cd.func_changes if f.kind == "signature_changed"]
        _assert(len(sig) == 1, f"a {label}-only parameter change is a signature change", errors)
    d = diff_dumps(_make_dump("FakeGame", [_cls_with([_func("Spawn", base)])], summary=_summary()),
                   _make_dump("FakeGame", [_cls_with([_func("Spawn", [base[0], dict(base[1], obj_class="Pawn")])])],
                              summary=_summary()))
    _assert("Pawn" in render_report(d), "the report shows a changed return entry", errors)

    # --- a dump whose params came from num_parms says so in the report ---
    md = render_report(diff_dumps(_make_dump("FakeGame", [], summary=_summary(params_from_num_parms=3)),
                                  _make_dump("FakeGame", [], summary=_summary())))
    _assert("from num_parms" in md, "the report notes params taken from num_parms", errors)

    # --- a struct that only grew is breaking: its size is the stride of every array of it ---
    g_old = _make_dump("FakeGame", [], structs=[_struct("FSlot", "/Script/G.FSlot", 0x18, [])],
                       summary=_summary(structs_emitted=1))
    g_new = _make_dump("FakeGame", [], structs=[_struct("FSlot", "/Script/G.FSlot", 0x20, [])],
                       summary=_summary(structs_emitted=1))
    _assert("`FSlot`" in render_report(diff_dumps(g_old, g_new), minimal=True),
            "a struct whose size changed is in the minimal report", errors)
    _assert("changed enum values" in render_report(diff_dumps(g_old, g_new), minimal=True),
            "the minimal banner names what it keeps", errors)

    # --- the object index is not a class dump ---
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        idx = Path(tmp) / "game.objects.jsonl"
        idx.write_text('{"kind":"meta","file":"objects","class_dump":"game.jsonl"}\n'
                       '{"kind":"object","index":0,"addr":"0x1","name":"A","class":"Class","outer":"","path":"/A"}\n',
                       encoding="utf-8")
        try:
            load_dump(idx)
            _assert(False, "loading an object index is refused", errors)
        except ObjectIndexFile as e:
            _assert("game.jsonl" in str(e), "the refusal names the class dump to use instead", errors)


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
