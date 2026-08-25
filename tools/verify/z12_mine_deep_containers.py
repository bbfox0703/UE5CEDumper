"""Z12 — mine an offline SDK export for objects whose address can ONLY be attributed by a DEEP
container descent, so the live half of the row has a fixture list instead of a guess.

    py tools/verify/z12_mine_deep_containers.py [path-to-SDK.h]     (default out/DumperTest_SDK.h)

WHAT Z12 NEEDS. Instance Finder's "Address -> Instance" reports a DEEP hit when the address it is
given lives inside a container ELEMENT that is itself a struct holding a container — the
`SaveSlotList[].MsTuneData...` shape the descent was written for. The deep-MISS caveat has been
verified; the deep-HIT half has not, because staging one by hand means guessing which live object
has that shape. This turns the guess into a shortlist, offline, from a complete census of the build.

WHY OFFLINE. The header is the same walk the DLL does, already done and written down. Zero pipe
traffic, no game running, and it can be re-run after any rebuild.

⚠ THREE PARSING TRAPS, each of which silently under-reports rather than erroring:

  1. **28.1% of structs have NO base** (2,215 of 7,886, measured). A pattern anchored as
     `^struct NAME ` with a trailing space matches only the `: public` form, so every base-less
     struct reads as absent. `assert_parser_sane()` fails the run if that regression returns.
  2. **The header stores STRIPPED names** — `Actor`, not `AActor`; `Object`, not `UObject`. A
     lookup written with UE's prefixes finds nothing and reports a clean, wrong zero.
  3. **A container-of-struct is not always one level deep.** A struct field can hold a struct that
     holds the container, so flattening only the top level misses candidates. Depth 3 mirrors
     `CollectContainersRecursive`; the run prints what each depth contributes so the choice is
     visible rather than asserted.
"""
import collections
import pathlib
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HDR = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "out/DumperTest_SDK.h")
OUT = pathlib.Path("out/z12/candidates.tsv")
MAX_DEPTH = 3

CONTAINER_KINDS = ("ArrayProperty", "MapProperty", "SetProperty")

# `struct Name` or `struct Name : public Base` — the optional-base group is the point.
RE_STRUCT = re.compile(r"^struct\s+([A-Za-z0-9_]+)(?:\s*:\s*public\s+([A-Za-z0-9_]+))?\s*$")
# `    <type...> <Name>; // 0xOFF (0xSIZE) <Kind>`
RE_FIELD = re.compile(
    r"^\s+(?P<type>.+?)\s+(?P<name>[A-Za-z0-9_]+)(?:\[\d+\])?;\s*//\s*"
    r"0x(?P<off>[0-9A-Fa-f]+)\s*\(0x(?P<size>[0-9A-Fa-f]+)\)\s*(?P<kind>\w+)\s*$")
# struct payloads mentioned anywhere inside a generic argument list
RE_STRUCT_ARG = re.compile(r"\bstruct\s+([A-Za-z0-9_]+)")


class Struct:
    __slots__ = ("name", "base", "fields")

    def __init__(self, name, base):
        self.name, self.base, self.fields = name, base, []


def parse(text):
    structs, cur = {}, None
    for line in text.split(chr(10)):
        m = RE_STRUCT.match(line)
        if m:
            cur = Struct(m.group(1), m.group(2))
            structs[cur.name] = cur
            continue
        if line.startswith("};"):
            cur = None
            continue
        if cur is None:
            continue
        f = RE_FIELD.match(line)
        if f and f.group("kind") != "PADDING":
            cur.fields.append((f.group("type").strip(), f.group("name"),
                               int(f.group("off"), 16), int(f.group("size"), 16),
                               f.group("kind")))
    return structs


def assert_parser_sane(structs, text):
    """Fail loudly on the three traps rather than reporting a clean, wrong zero."""
    problems = []

    # (2) stripped names
    for stripped, prefixed in (("Actor", "AActor"), ("Object", "UObject")):
        if stripped not in structs:
            problems.append("the header should declare the STRIPPED name %r and does not"
                            % stripped)
        if prefixed in structs:
            problems.append("found the UE-prefixed name %r — the header format changed, and every"
                            " prefixed lookup elsewhere is now silently wrong" % prefixed)

    # (1) base-less structs must be parsed
    baseless = [s for s in structs.values() if s.base is None]
    if len(baseless) < 100:
        problems.append("only %d base-less structs parsed — the optional-base group is broken, and"
                        " ~28%% of the file is invisible" % len(baseless))
    # and the naive pattern really does miss them (the control for the control).
    # ⚠ The trailing space must be a LITERAL space, not `\s+`: in Python `\s` matches the
    # NEWLINE, so `^struct NAME\s+` happily matches a base-less declaration too and the control
    # silently stops discriminating. Caught by this very assertion on its first run.
    naive_hits = len(re.findall("(?m)^struct [A-Za-z0-9_]+ ", text))
    anchored_hits = len(re.findall(r"(?m)^struct [A-Za-z0-9_]+", text))
    if naive_hits >= anchored_hits:
        problems.append("the naive trailing-space pattern did not under-count (%d vs %d) — this"
                        " control has stopped discriminating and proves nothing"
                        % (naive_hits, anchored_hits))

    if problems:
        print("PARSER SANITY FAILED:")
        for p in problems:
            print("   ", p)
        raise SystemExit(2)
    print("parser sanity OK  (%d structs, %d base-less = %.1f%%; naive pattern sees %d of %d)"
          % (len(structs), len(baseless), 100.0 * len(baseless) / len(structs),
             naive_hits, anchored_hits))


def all_fields(structs, name, _seen=None):
    """Own fields plus every inherited one, super chain walked, self-loop guarded."""
    _seen = _seen or set()
    if name in _seen or name not in structs:
        return []
    _seen.add(name)
    s = structs[name]
    out = list(s.fields)
    if s.base:
        out += all_fields(structs, s.base, _seen)
    return out


def declares_container(structs, name, depth, _seen=None):
    """Does `name`, or a struct it embeds within `depth` levels, declare a container field?"""
    if depth <= 0 or name not in structs:
        return None
    _seen = _seen or set()
    if name in _seen:
        return None
    _seen.add(name)
    for ftype, fname, off, size, kind in all_fields(structs, name):
        if kind in CONTAINER_KINDS:
            return (name, fname, ftype, kind)
        if kind == "StructProperty":
            for inner in RE_STRUCT_ARG.findall(ftype):
                hit = declares_container(structs, inner, depth - 1, _seen)
                if hit:
                    return hit
    return None


text = HDR.read_text(encoding="utf-8", errors="replace")
structs = parse(text)
assert_parser_sane(structs, text)

rows, by_depth = [], collections.Counter()
for owner in sorted(structs):
    for ftype, fname, off, size, kind in all_fields(structs, owner):
        if kind not in CONTAINER_KINDS:
            continue
        # element / value / KEY struct payloads — a TMap's key is a payload too, and skipping it
        # is a quiet 1-in-N loss on map-heavy classes.
        for payload in dict.fromkeys(RE_STRUCT_ARG.findall(ftype)):
            for d in range(1, MAX_DEPTH + 1):
                hit = declares_container(structs, payload, d)
                if hit:
                    by_depth[d] += 1
                    rows.append((owner, fname, kind, ftype.strip(), payload,
                                 hit[0], hit[1], hit[3], d))
                    break

# runtime-likely owners first: the row needs a LIVE instance, and most structs never get one.
RUNTIME_HINTS = ("CollisionProfile", "GameEngine", "InputMappingContext", "EnhancedPlayerInput",
                 "World", "SkinnedMeshComponent", "PlayerCameraManager", "GameInstance",
                 "PlayerController", "Character", "Actor", "Level", "GameMode", "HUD")
rows.sort(key=lambda r: (0 if any(h in r[0] for h in RUNTIME_HINTS) else 1, r[0], r[1]))

OUT.parent.mkdir(parents=True, exist_ok=True)
with OUT.open("w", encoding="utf-8", newline=chr(10)) as f:
    f.write("owner\tfield\tkind\tdeclared_type\tpayload_struct\tinner_owner\tinner_field\t"
            "inner_kind\tdepth" + chr(10))
    for r in rows:
        f.write(chr(9).join(str(x) for x in r) + chr(10))

owners = {r[0] for r in rows}
print("candidates: %d triples over %d distinct owners" % (len(rows), len(owners)))
print("by nesting depth:", dict(sorted(by_depth.items())))
print("written:", OUT)
print()
print("top runtime-likely owners:")
seen = set()
for r in rows:
    if r[0] in seen or not any(h in r[0] for h in RUNTIME_HINTS):
        continue
    seen.add(r[0])
    print("   %-34s %-28s -> %s::%s (%s, depth %d)" % (r[0], r[1], r[5], r[6], r[7], r[8]))
    if len(seen) >= 18:
        break
