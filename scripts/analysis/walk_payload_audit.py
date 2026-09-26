#!/usr/bin/env python3
"""
walk_payload_audit.py — how much of a `walk_instance` payload does the CE export
actually read?

WHY
---
`docs/multipipe-eval.md` §10.4 measured a Copy CE XML as **59-73% pure IPC**, and
build 2329 collapsed the round-trips by batching. What batching does NOT change is
the number of BYTES on the wire: every batched instance still carries the full
`walk_instance` object — every field's raw `hex`, its decoded `value`, resolved
pointer NAMES, per-instance `name`/`class`/`outer_*`, and so on.

The CE XML exporter reads almost none of that. Its output is structural
(description + offset + CE type + drill-down children); the live VALUES only ever
reach it as copy-through in a struct-flatten. So the question this script answers,
with real bytes rather than reasoning, is:

    if `walk_instance` grew a "lean" mode for the export, how much of the payload
    would it be allowed to drop?

The answer is a RATIO, so a truncated sample is still informative — see SAMPLING.

INPUT
-----
UI-side pipe logs (`%LOCALAPPDATA%\\UE5CEDumper\\Logs\\<proc>\\pipe-*.log`), which
carry the response bodies as `Pipe RX: {...}`. The DLL-side logs record only the
REQUEST line, so they are useless here.

    python scripts/analysis/walk_payload_audit.py                  # auto-discover logs
    python scripts/analysis/walk_payload_audit.py path/to/pipe-0.log ...
    python scripts/analysis/walk_payload_audit.py --self-test

SAMPLING (read before quoting a number)
---------------------------------------
`PipeClient.LogBody` caps a logged body at 1024 chars, so most sampled responses
are PREFIXES. Two consequences, both of which bias the result in a KNOWN
direction:

1. Only complete `"key": value` pairs are counted; a pair cut by the cap is
   dropped entirely, never half-counted.
2. The DLL emits object keys in ALPHABETICAL order (nlohmann `json` is a sorted
   map). Within one field object the cap therefore eats the alphabetically-late
   keys first, and across the `fields[]` array it eats the later fields. Big
   container fields (`elements`, `map_elements`, `set_elements` — all USED by the
   export) are the most likely to be cut, so a truncated sample **understates the
   used share**. Treat the reported figure as a lower bound on "used".

To remove the bias entirely, set `UE5DUMP_PIPE_LOG_FULL=1` before launching
UE5DumpUI (lifts the 1024-char cap), do ONE Copy CE XML, and re-run this script.
The pipe log rolls at 8 MiB and every rolled part is kept for the normal
age-based retention window, so the WHOLE export is captured as complete,
untruncated payloads. `--coverage` reports how much of the sampled payload the
cap hid.

  Corrected 2026-08-04. This used to promise "log rotation (4 x 8 MiB) keeps the
  LAST ~32 MiB ... an unbiased one". Neither half was true: the sink was
  configured with a size limit but no roll point, so it did not rotate at all —
  it stopped writing at the first 8 MiB, and this script silently measured the
  export's OPENING PREFIX, reintroducing exactly the bias the flag exists to
  remove. Fixed in build 2585 (audit #4 B31); there is also no 4-file cap now,
  because a generation COUNT limit is the retention policy this project
  deliberately replaced with an age-based one.

The `per scope` table is the reading to trust under truncation: sampling within
one scope is uniform, so `field`'s used/unused ratio is sound even at 6% overall
coverage. Only the mix BETWEEN scopes (an envelope is always logged whole, a
field array is not) needs the full capture.

CLASSIFICATION
--------------
Every key is tagged against what `CeXmlExportService` actually reads, with the
consuming line cited in KEYS below. Tags:

  used   — read on the CE XML export path.
  cond   — read only under a documented condition (the whole value can still be
           dead weight when the condition fails).
  csx    — dead for CE XML, but read by `CsxExportService` (Structure Dissect).
  unused — never read by either exporter. Pure wire weight for an export.

"Read" is deliberately strict: a property that is only COPIED into a synthesized
`LiveFieldValue` during the struct-flatten (CeXmlExportService.cs:987-1039) and
never consumed afterwards counts as unused — copying a value is not using it.

ACTED ON (build 2351): the `unused` keys this script found are what
`walk_instance`'s `lean: true` flag now omits for the CE XML export path (drop
list in docs/pipe-protocol.md). Re-running the audit on a CURRENT log therefore
reports a much smaller unused share for lean responses — that is the fix working,
not the measurement changing. To re-measure the FULL shape, sample a CSX export or
a Live Walker session, both of which still request full payloads.
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import sys
from collections import defaultdict

# ── The classification table ────────────────────────────────────────────────
# tag, why (with the consuming site, so a reviewer can re-derive the tag)

INSTANCE_KEYS = {
    # The export reads `result.Fields` and NOTHING else from a walked instance:
    # CeXmlExportService.cs's ResolvePointerInstancesRecursiveAsync / WalkAndRecurseAsync / PrefetchStructTreeAsync /
# ResolveStructRecursiveAsync all touch `.Fields` only.
    # The requester already knows the address it asked for, so even `addr` is
    # redundant on the wire (the batch reply is positional).
    "addr":          ("unused", "batch reply is positional; caller already has the addr"),
    "name":          ("unused", "export never reads InstanceWalkResult.Name"),
    "class":         ("unused", "export never reads .ClassName"),
    "class_addr":    ("unused", "export never reads .ClassAddr"),
    "outer":         ("unused", "export never reads .OuterAddr"),
    "outer_name":    ("unused", "export never reads .OuterName"),
    "outer_class":   ("unused", "export never reads .OuterClassName"),
    "is_definition": ("unused", "export never reads .IsDefinition"),
    "stale":         ("unused", "export tests Fields.Count == 0 instead (:440)"),
    "props_size":    ("unused", "LiveWalker-only (fill_gaps auto-enable)"),
}

# Response envelope — per RESPONSE, not per instance, so batching already
# amortises it. Listed so the accounting adds up to the whole payload.
ENVELOPE_KEYS = {
    "id":                  ("used",   "PipeClient pairs the reply to its request"),
    "ok":                  ("used",   "CheckResponse gate"),
    "count":               ("unused", "redundant with instances.length; nothing reads it"),
    "game_thread_stalled": ("used",   "app-wide game-thread liveness indicator"),
    "error":               ("used",   "failure text"),
}

FIELD_KEYS = {
    # -- structural: what a CE entry is actually made of --
    "name":    ("used", "DecorateDesc(field.Name, ...) :2211"),
    "type":    ("used", "MapCeField / IsStringProperty / the whole emit switch"),
    "offset":  ("used", "every leaf's '+{Offset:X}' address expression"),
    "size":    ("used", "CeWidthForSize(field.Size) :3815"),
    "guessed": ("used", "EmitFields skips guessed fields :2069"),

    # -- pointer drill-down --
    "ptr":            ("used", "ResolvePointerInstancesRecursiveAsync :421"),
    "ptr_class_addr": ("used", "batch item's class_addr :425"),
    "ptr_class":      ("used", "type label + noise filter :1966 / :2079"),
    "ptr_name":       ("unused", "copy-through only (:979); desc uses PtrClassName"),

    # -- bool bitfield --
    "bool_bit":         ("used", "bit index reaches the CE/CSX bit leaf"),
    "bool_mask":        ("csx",  "CSX description only (CsxExportService.cs:200)"),
    "bool_byte_offset": ("csx",  "CE XML ignores it; CSX Binary needs it (:1000 comment)"),

    # -- values: the export emits structure, never the live value --
    "hex":        ("csx",    "CSX BuildStrChildStructure(:209); CE XML copy-through only"),
    "value":      ("unused", "copy-through only (:994)"),
    "str_value":  ("unused", "copy-through only (:1016); string leaf is emitted structurally"),
    "enum_value": ("unused", "copy-through only (:1013)"),
    "enum_name":  ("unused", "copy-through only (:1012); dropdowns use element 'en'"),

    # -- array --
    "count":                   ("used", "ArrayCount gates the array emit"),
    "array_inner_type":        ("used", "element CE type"),
    "array_struct_type":       ("used", "struct-array element label"),
    "array_elem_size":         ("used", "element stride"),
    "array_data_addr":         ("used", "ParseHexAddr(field.ArrayDataAddr) :698/:2857"),
    "array_struct_class_addr": ("used", "struct element navigation"),
    "array_inner_addr":        ("unused", "read_array_elements only; no exporter reads it"),
    "soft_fname_size":            ("used", "Phase G soft-array leaf layout"),
    "soft_top_level_asset_path":  ("used", "Phase G soft-array leaf layout"),
    "elements":     ("used", "element labels + dropdown pairs + struct sub-fields"),
    "enum_addr":    ("used", "DropDownList sharing key :2280"),
    "enum_entries": ("cond", "DropDownList ONLY when Count <= maxDropDownEntries (:2260) "
                             "over the cap the whole array is discarded"),

    # -- map / set --
    "map_count":             ("used", "gates the map emit"),
    "map_key_type":          ("used", "key leaf type"),
    "map_value_type":        ("used", "value leaf type"),
    "map_key_size":          ("used", "TPair layout"),
    "map_value_size":        ("used", "TPair layout"),
    "map_value_offset":      ("used", "TPair value alignment"),
    "map_data_addr":         ("used", "TSparseArray base"),
    "map_key_struct_addr":   ("used", "struct key expansion"),
    "map_key_struct_type":   ("used", "struct key label"),
    "map_value_struct_addr": ("used", "struct value expansion"),
    "map_value_struct_type": ("used", "struct value label"),
    "map_elements":          ("used", "per-entry description + dropdown pairs :3240"),
    "set_count":             ("used", "gates the set emit"),
    "set_elem_type":         ("used", "element leaf type"),
    "set_elem_size":         ("used", "element stride"),
    "set_data_addr":         ("used", "TSparseArray base"),
    "set_elem_struct_addr":  ("used", "struct element expansion"),
    "set_elem_struct_type":  ("used", "struct element label"),
    "set_elements":          ("used", "per-entry description :3374"),

    # -- struct --
    "struct_data_addr":  ("used", "struct recursion root :962"),
    "struct_class_addr": ("used", "struct recursion class :961"),
    "struct_type":       ("used", "struct label / FDateTime decode"),
}

# ── inline container ELEMENTS ───────────────────────────────────────────────
# The single biggest bucket, so it is broken down per sub-key instead of being
# charged wholesale to `elements` / `map_elements` / `set_elements`.

ELEM_KEYS = {   # ArrayElementValue — field "elements"
    "i":  ("used",   "element index -> '[i]' label and i*stride offset"),
    "v":  ("used",   "element label + FName/enum dropdown pairs :2726/:2966"),
    "h":  ("unused", "ArrayElementValue.Hex - no exporter reads it"),
    "en": ("used",   "enum dropdown pair :2709"),
    "rv": ("used",   "dropdown key (enum value / FName ComparisonIndex)"),
    "pa": ("used",   "pointer element -> emitted pointer leaf :2646/:2887"),
    "pn": ("csx",    "CSX '[i] Name' label :690; CE XML copy-through"),
    "pc": ("used",   "element type label :2777/:2909"),
    "sf": ("used",   "struct sub-fields (broken down below)"),
}

SF_KEYS = {     # StructSubFieldValue — inside "sf"
    "n":   ("used",   "sub-field description :3090"),
    "t":   ("used",   "sub-field CE type :3086"),
    "o":   ("used",   "sub-field offset :3092"),
    "s":   ("used",   "CeWidthForSize(sf.Size) :3085"),
    "v":   ("unused", "sub-field VALUE - neither exporter reads it"),
    "pa":  ("csx",    "CSX copies it into the emitted field :777"),
    "pn":  ("csx",    "CSX label :778"),
    "pc":  ("used",   "type label :3091"),
    "pca": ("csx",    "CSX drill-down class :780"),
}

CONT_KEYS = {   # ContainerElementValue — "map_elements" / "set_elements"
    "i":  ("used",   "'[i]' label + i*stride offset :3240"),
    "k":  ("used",   "key label :3268/:3377"),
    "v":  ("used",   "value label + dropdown pair :3210"),
    "kh": ("unused", "KeyHex - Live Walker display only, no exporter reads it"),
    "vh": ("used",   "ParseHexLeInt(e.ValueHex) dropdown key :3209"),
    "kn": ("used",   "pointer-key label :3265"),
    "ka": ("used",   "pointer-key leaf :3393"),
    "kc": ("used",   "pointer-key type label :3266"),
    "vn": ("used",   "pointer-value label :3285"),
    "va": ("used",   "pointer-value leaf :3285"),
    "vc": ("used",   "pointer-value type label :3285"),
}

ENUM_ENTRY_KEYS = {   # "enum_entries" — same condition as the parent key
    "v": ("cond", "DropDownList pair, only under the entry cap"),
    "n": ("cond", "DropDownList pair, only under the entry cap"),
}

# field key -> (bucket prefix, sub-key table)
DESCEND = {
    "elements":     ("elem", ELEM_KEYS),
    "map_elements": ("cont", CONT_KEYS),
    "set_elements": ("cont", CONT_KEYS),
    "enum_entries": ("enum", ENUM_ENTRY_KEYS),
}
SUB_TABLES = {"elem": ELEM_KEYS, "sf": SF_KEYS, "cont": CONT_KEYS, "enum": ENUM_ENTRY_KEYS}

TAGS = ("used", "cond", "csx", "unused")

RX_MARK = "Pipe RX: "
TRUNC_MARK = "… ("          # "… (12,345 chars)"

_DEC = json.JSONDecoder()


# ── tolerant JSON scanning ──────────────────────────────────────────────────

def _skip_ws(text: str, i: int) -> int:
    while i < len(text) and text[i] in " \t\r\n":
        i += 1
    return i


def scan_pairs(text: str, pos: int):
    """Walk the object starting at text[pos] == '{', yielding one entry per
    `"key": value` pair as (key, start, value_end, value_complete).

    Returns (pairs, end, complete). A scalar value cut off by the log cap is
    dropped whole — never half-counted. A CONTAINER value cut off mid-way is
    still yielded (complete=False) so the caller can descend into the elements
    that ARE whole: with alphabetically-ordered keys the `fields` array of a
    single-instance response is nearly always the truncated one, and refusing to
    look inside it would throw away the entire sample."""
    pairs = []
    if pos >= len(text) or text[pos] != "{":
        return pairs, pos, False
    i = pos + 1
    while True:
        i = _skip_ws(text, i)
        if i >= len(text):
            return pairs, i, False
        if text[i] == "}":
            return pairs, i + 1, True
        start = i
        try:
            key, j = _DEC.raw_decode(text, i)
        except ValueError:
            return pairs, i, False
        j = _skip_ws(text, j)
        if j >= len(text) or text[j] != ":":
            return pairs, j, False
        k = _skip_ws(text, j + 1)
        try:
            _, m = _DEC.raw_decode(text, k)
        except ValueError:
            if k < len(text) and text[k] in "[{":
                pairs.append((key, start, len(text), False))
            return pairs, k, False
        pairs.append((key, start, m, True))
        i = _skip_ws(text, m)
        if i < len(text) and text[i] == ",":
            i += 1


def scan_array_objects(text: str, pos: int):
    """Yield (start, pairs, end, complete) for each object element of the array
    starting at text[pos] == '['. Stops at the first truncated element."""
    out = []
    if pos >= len(text) or text[pos] != "[":
        return out, pos, False
    i = _skip_ws(text, pos + 1)
    if i < len(text) and text[i] == "]":
        return out, i + 1, True
    while True:
        i = _skip_ws(text, i)
        if i >= len(text) or text[i] != "{":
            return out, i, False
        pairs, end, complete = scan_pairs(text, i)
        out.append((i, pairs, end, complete))
        if not complete:
            return out, end, False
        i = _skip_ws(text, end)
        if i >= len(text):
            return out, i, False
        if text[i] == "]":
            return out, i + 1, True
        if text[i] != ",":
            return out, i, False
        i += 1


# ── the audit ───────────────────────────────────────────────────────────────

class Audit:
    def __init__(self):
        self.bytes = defaultdict(int)          # "instance.name" / "field.hex" -> bytes
        self.structural = defaultdict(int)     # scope -> braces/brackets/separators
        self.instances = 0
        self.fields = 0
        self.responses = defaultdict(int)      # shape -> count
        self.wire_bytes = 0                    # real payload size when known
        self.sampled_bytes = 0                 # what we could actually read
        self.truncated = 0

    # -- one response body --------------------------------------------------
    def add_response(self, body: str, declared_len: int | None):
        self.sampled_bytes += len(body)
        self.wire_bytes += declared_len if declared_len is not None else len(body)
        if declared_len is not None:
            self.truncated += 1

        if '"instances":' in body:
            self.responses["walk_instance_batch"] += 1
            pairs, _, _ = scan_pairs(body, 0)
            for key, kstart, vend, whole in pairs:
                if key == "instances":
                    arr = _skip_ws(body, body.index(":", kstart) + 1)
                    elems, _, _ = scan_array_objects(body, arr)
                    for start, ipairs, end, complete in elems:
                        # A truncated instance is NOT dropped: its whole field
                        # objects are the bulk of the sample, and its own
                        # instance-level keys are skipped pair-by-pair inside.
                        self._add_instance(body, start, ipairs, end, complete)
                    # `"instances":[` + the per-object braces/commas are charged
                    # by _add_instance's own remainder; only the key itself here.
                    self.structural["envelope"] += len('"instances":[')
                elif whole:
                    self.bytes["envelope." + key] += vend - kstart + 1
            return True

        # single walk_instance: the instance object IS the response envelope
        if body.startswith('{"addr":') and '"fields":' in body:
            self.responses["walk_instance"] += 1
            pairs, end, complete = scan_pairs(body, 0)
            self._add_instance(body, 0, pairs, end, complete)
            return True
        return False

    # -- one instance object ------------------------------------------------
    def _add_instance(self, text, start, pairs, end, complete):
        self.instances += 1
        self.structural["instance"] += 1       # the instance's own '{'
        for key, kstart, vend, whole in pairs:
            span = vend - kstart + (1 if whole else 0)   # + the ',' or '}' that follows
            if key == "fields":
                self._add_fields(text, kstart, whole)
            elif not whole:
                continue                       # truncated non-field container: skip
            elif key in INSTANCE_KEYS:
                self.bytes["instance." + key] += span
            else:
                # response envelope (id / ok / count / game_thread_stalled) or a
                # key this table does not know yet
                self.bytes["envelope." + key] += span

    def _add_fields(self, text, kstart, array_complete):
        """Account the `fields` array. Only whole field objects are counted, and
        the structural remainder is measured against the span those objects
        actually cover — so a truncated tail contributes nothing to either side
        of the ratio rather than landing in `structural`."""
        arr = _skip_ws(text, text.index(":", kstart) + 1)
        elems, arr_end, _ = scan_array_objects(text, arr)
        attributed = 0
        covered_end = arr + 1                  # just past '['
        for _, pairs, elem_end, elem_complete in elems:
            if not elem_complete:
                break                          # the log cap landed inside this object
            self.fields += 1
            covered_end = elem_end
            for key, ks, ve, whole in pairs:
                if not whole:
                    continue                   # truncated nested container
                cost = ve - ks + 1
                attributed += cost
                if key in DESCEND:
                    prefix, table = DESCEND[key]
                    self._add_elements(text, ks, cost, prefix, table)
                    continue
                bucket = "field." + key if key in FIELD_KEYS else "field?." + key
                self.bytes[bucket] += cost
        # `"fields":[` + the closing `]` (only when the array really ended) +
        # per-object braces + separators.
        span = covered_end - kstart + (1 if array_complete else 0)
        self.structural["field"] += max(0, span - attributed)

    def _add_elements(self, text, kstart, span, prefix, table):
        """Split an inline element array (`elements` / `map_elements` /
        `set_elements` / `enum_entries`) into its per-sub-key bytes. It is the
        largest single bucket, and its sub-keys do NOT share a verdict: an
        element's `v` drives a CE label while its `h` is never read."""
        arr = _skip_ws(text, text.index(":", kstart) + 1)
        elems, _, _ = scan_array_objects(text, arr)
        attributed = 0
        for _, pairs, _, complete in elems:
            if not complete:
                break
            for key, ks, ve, whole in pairs:
                if not whole:
                    continue
                cost = ve - ks + 1
                attributed += cost
                if prefix == "elem" and key == "sf":
                    self._add_elements(text, ks, cost, "sf", SF_KEYS)
                    continue
                bucket = f"{prefix}.{key}" if key in table else f"{prefix}?.{key}"
                self.bytes[bucket] += cost
        self.structural[prefix] += max(0, span - attributed)

    # -- reporting ----------------------------------------------------------
    def tag_of(self, bucket: str):
        scope, _, key = bucket.partition(".")
        if scope == "instance" and key in INSTANCE_KEYS:
            return scope, INSTANCE_KEYS[key]
        if scope == "field" and key in FIELD_KEYS:
            return scope, FIELD_KEYS[key]
        if scope == "envelope" and key in ENVELOPE_KEYS:
            return scope, ENVELOPE_KEYS[key]
        if scope in SUB_TABLES and key in SUB_TABLES[scope]:
            return scope, SUB_TABLES[scope][key]
        return scope, (None, "key not in the classification table")

    def totals(self):
        per_tag = dict.fromkeys(TAGS, 0)
        unknown = 0
        for bucket, n in self.bytes.items():
            _, (tag, _) = self.tag_of(bucket)
            if tag is None:
                unknown += n
            else:
                per_tag[tag] += n
        return per_tag, unknown

    def by_scope(self):
        """Per-scope tag split. The scope table is what survives a truncated
        sample: within one scope the sampling is uniform, so `field`'s used /
        unused ratio holds even when the whole-payload mix does not."""
        out = defaultdict(lambda: dict.fromkeys(TAGS + ("struct", "?"), 0))
        for bucket, n in self.bytes.items():
            scope, (tag, _) = self.tag_of(bucket)
            out[scope][tag or "?"] += n
        for scope, n in self.structural.items():
            out[scope]["struct"] += n
        return out

    def report(self, top: int, coverage: bool) -> str:
        per_tag, unknown = self.totals()
        keyed = sum(per_tag.values())
        structural_total = sum(self.structural.values())
        total = keyed + unknown + structural_total
        if total == 0:
            return "no walk_instance payloads found in the sampled logs"

        def pct(n):
            return f"{n * 100.0 / total:5.1f}%"

        L = []
        L.append("walk_instance payload audit -- what the CE XML export actually reads")
        L.append("=" * 72)
        L.append(f"responses : " + ", ".join(
            f"{k} x{v}" for k, v in sorted(self.responses.items())) or "none")
        L.append(f"sampled   : {self.instances:,} instances, {self.fields:,} fields, "
                 f"{total:,} accounted bytes")
        if coverage or self.truncated:
            hidden = max(0, self.wire_bytes - self.sampled_bytes)
            L.append(f"coverage  : {self.sampled_bytes:,} of {self.wire_bytes:,} wire bytes read "
                     f"({self.sampled_bytes * 100.0 / max(1, self.wire_bytes):.1f}%); "
                     f"{self.truncated:,} responses were cut by the 1024-char log cap "
                     f"({hidden:,} bytes unseen)")
        L.append("")
        L.append("bucket        bytes      share   meaning")
        L.append("-" * 72)
        rows = [
            ("used",       "read by the CE XML export"),
            ("cond",       "read only under a condition (enum dropdown cap)"),
            ("csx",        "dead for CE XML; read by CSX (Structure Dissect)"),
            ("unused",     "read by NEITHER exporter -- pure wire weight"),
        ]
        for tag, meaning in rows:
            L.append(f"{tag:<12} {per_tag[tag]:>10,}  {pct(per_tag[tag])}   {meaning}")
        L.append(f"{'structural':<12} {structural_total:>10,}  {pct(structural_total)}   "
                 f"braces / brackets / separators")
        if unknown:
            L.append(f"{'unclassified':<12} {unknown:>10,}  {pct(unknown)}   "
                     f"keys not in the table")
        L.append("-" * 72)
        droppable = per_tag["unused"]
        L.append(f"CE XML only : {droppable:,} bytes ({pct(droppable)}) are droppable outright; "
                 f"+{per_tag['csx']:,} ({pct(per_tag['csx'])}) if CSX opts out too")
        L.append("")

        # Per-scope split. With a truncated sample the WHOLE-payload mix is
        # skewed (an envelope is always fully logged, a field array is not), but
        # sampling within one scope is uniform — so these ratios hold even when
        # coverage is low, and `field`+`elem` are where the payload actually is.
        scopes = self.by_scope()
        L.append("per scope (sampling is uniform WITHIN a scope -- trust these under truncation)")
        L.append("-" * 72)
        L.append(f"{'scope':<10} {'bytes':>10} {'share':>7}   {'used':>6} {'cond':>6} "
                 f"{'csx':>6} {'unused':>7} {'struct':>7}")
        order = ["envelope", "instance", "field", "elem", "sf", "cont", "enum"]
        for scope in order + [s for s in sorted(scopes) if s not in order]:
            row = scopes.get(scope)
            if not row:
                continue
            tot = sum(row.values())
            if tot == 0:
                continue
            def sp(tag):
                return f"{row[tag] * 100.0 / tot:5.1f}%"
            L.append(f"{scope:<10} {tot:>10,} {tot * 100.0 / total:6.1f}%   "
                     f"{sp('used'):>6} {sp('cond'):>6} {sp('csx'):>6} "
                     f"{sp('unused'):>7} {sp('struct'):>7}")
        L.append("")

        # Unit costs — what it takes to correct the scope MIX by hand when the
        # sample is truncated. A response carries `instances x (header + F x
        # field)`, and truncation inflates the header/envelope share because
        # those are always inside the logged prefix while the field array is not.
        by = self.by_scope()
        def scope_total(s):
            return sum(by.get(s, {}).values())
        field_bytes = sum(scope_total(s) for s in ("field", "elem", "sf", "cont", "enum"))
        if self.fields and self.instances:
            L.append(f"unit costs : {field_bytes / self.fields:.0f} B per field, "
                     f"{scope_total('instance') / self.instances:.0f} B per instance header, "
                     f"{scope_total('envelope') / max(1, sum(self.responses.values())):.0f} B "
                     f"per response envelope "
                     f"({self.fields / self.instances:.1f} fields/instance SAMPLED)")
            L.append("")

        L.append(f"top {top} keys by bytes")
        L.append("-" * 72)
        for bucket, n in sorted(self.bytes.items(), key=lambda kv: -kv[1])[:top]:
            _, (tag, why) = self.tag_of(bucket)
            L.append(f"{bucket:<34} {n:>9,}  {pct(n)}  {(tag or '-'):<6} {why}")
        return "\n".join(L)


# ── log reading ─────────────────────────────────────────────────────────────

def iter_rx_bodies(path: str):
    """Yield (body, declared_full_len_or_None) for every `Pipe RX:` line."""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            i = line.find(RX_MARK)
            if i < 0:
                continue
            body = line[i + len(RX_MARK):].rstrip("\r\n")
            declared = None
            t = body.rfind(TRUNC_MARK)
            if t >= 0 and body.endswith("chars)"):
                digits = body[t + len(TRUNC_MARK):-len(" chars)")].replace(",", "")
                if digits.isdigit():
                    declared = int(digits)
                    body = body[:t]
            yield body, declared


def default_logs():
    base = os.environ.get("LOCALAPPDATA", "")
    if not base:
        return []
    return sorted(glob.glob(os.path.join(base, "UE5CEDumper", "Logs", "*", "pipe-*.log")))


# ── self-test ───────────────────────────────────────────────────────────────

def self_test() -> int:
    # Keys are emitted alphabetically by the DLL (nlohmann sorted map); mirror that.
    one = ('{"addr":"0xA","class":"AActor","class_addr":"0xC","fields":['
           '{"hex":"0000803F","name":"Health","offset":16,"size":4,"type":"FloatProperty"},'
           '{"name":"Target","offset":24,"ptr":"0xB","ptr_class":"APawn",'
           '"ptr_name":"BP_Enemy_C_1","size":8,"type":"ObjectProperty"}],'
           '"name":"Obj","outer":"0x0","outer_class":"","outer_name":""}')
    a = Audit()
    assert a.add_response(one, None), "single walk_instance not recognised"
    assert a.instances == 1 and a.fields == 2, (a.instances, a.fields)
    assert a.bytes["field.hex"] == len('"hex":"0000803F",')
    assert a.bytes["field.ptr_name"] == len('"ptr_name":"BP_Enemy_C_1",')
    assert a.bytes["instance.class"] == len('"class":"AActor",')
    per_tag, _ = a.totals()
    assert per_tag["unused"] > 0 and per_tag["used"] > 0

    # Batch shape + a body cut mid-value: the partial pair must be dropped whole.
    batch = ('{"count":2,"game_thread_stalled":false,"id":7,"instances":['
             '{"addr":"0xA","fields":[{"name":"A","offset":0,"size":4,"type":"IntProperty"}]},'
             '{"addr":"0xB","fields":[{"name":"B","offset":4,"size":4,"type":"IntProp')
    b = Audit()
    assert b.add_response(batch, 999)
    assert b.responses["walk_instance_batch"] == 1
    assert b.instances == 2, b.instances
    # Instance 2's only field object is cut by the cap. It is dropped WHOLE:
    # counting its early keys ("name"/"offset") but not its late ones ("type")
    # would bias the ratio toward alphabetically-early keys.
    assert b.fields == 1, b.fields
    assert b.bytes["field.name"] == len('"name":"A",'), b.bytes["field.name"]
    assert b.bytes["field.type"] == len('"type":"IntProperty"}')

    # Truncation bookkeeping
    assert b.truncated == 1 and b.wire_bytes == 999

    # Every key the DLL can emit must be classified, or the audit silently
    # under-reports. (Guards against a new SerializeField key landing untagged.)
    for k in ("hex", "value", "elements", "map_elements", "set_elements",
              "enum_entries", "struct_type", "guessed"):
        assert k in FIELD_KEYS, k

    print("self-test OK")
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("logs", nargs="*", help="UI pipe-*.log files (default: auto-discover)")
    ap.add_argument("--top", type=int, default=25, help="how many keys to list (default 25)")
    ap.add_argument("--coverage", action="store_true", help="always show the sampling coverage line")
    ap.add_argument("--self-test", action="store_true", help="run the built-in fixture tests")
    args = ap.parse_args(argv)

    if args.self_test:
        return self_test()

    logs = args.logs or default_logs()
    if not logs:
        print("no logs given and none found under %LOCALAPPDATA%\\UE5CEDumper\\Logs", file=sys.stderr)
        return 2

    audit = Audit()
    used_files = 0
    for path in logs:
        hit = False
        for body, declared in iter_rx_bodies(path):
            if audit.add_response(body, declared):
                hit = True
        if hit:
            used_files += 1

    print(audit.report(args.top, args.coverage))
    print(f"\n({used_files} of {len(logs)} log files contained walk_instance responses)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
