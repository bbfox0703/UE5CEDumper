r"""Check an exported SDK header's CLASS-VALUED members against the engine SOURCE that declared them.

    py tools/verify/sdk_source_oracle.py --header out/sdk-live/es2_3587.h ^
        --engine "C:/Program Files/Epic Games/UE_5.6"  [--json out/sdk-live/oracle.json]

WHY THIS EXISTS
  Every SDK-export row before [SDK-METACLASS] was checked against Dumper-7's conventions, i.e.
  against another dumper. That proves agreement, not correctness: two dumpers can share a wrong
  reading. The engine source is the fact. `TSubclassOf<APawn> DefaultPawnClass;` in
  GameModeBase.h is what UHT turned into the ClassProperty the DLL walks, so a header line for
  that member can be checked against the declaration it came from -- for every native class the
  game ships whose module is in the engine tree. Commercial game modules (e.g. `/Script/ES2`) and
  Blueprint classes have no source here; they are counted as NO-SOURCE, never as a pass.

WHAT "CLASS-VALUED" MEANS, AND WHAT THE HEADER MUST SAY
  A member is checked when EITHER side makes it class-valued: the header spells TSubclassOf /
  TSoftClassPtr / UClass* or tags it ClassProperty / SoftClassProperty, OR the source type holds
  TSubclassOf / TSoftClassPtr / a pointer to UClass or to a UClass subclass. Both directions,
  because a check selected from the header alone cannot see a class-valued member the header got
  wrong the other way. The UHT mapping the export has to reproduce:
      TSubclassOf<X>                 -> TSubclassOf<class X>      (X = UObject -> UClass*)
      TSoftClassPtr<X>               -> TSoftClassPtr<class X>    (X = UObject -> TSoftClassPtr<UObject>)
      UClass* / TObjectPtr<UClass>   -> UClass*
      TObjectPtr<UXxxClass>          -> class XxxClass*           (a UClass SUBCLASS is the PropertyClass;
                                                                    its MetaClass is Object)
  Only the class-valued slots of a container are compared, so a TMap's non-class key or value is
  never judged here. Type names are compared without the U/A prefix; the header's outer-qualified
  spelling (`Name_Qualifier`) of an ambiguous name counts as the same name.

FAILING LOUDLY (working-lessons Sec.1)
  A header or engine root that does not exist, or a header with no native struct, is an error.
  Exit 0 = every located member matched. Exit 1 = at least one MISMATCH. Exit 2 = could not check.
"""
from __future__ import annotations

import argparse
import collections
import json
import os
import pathlib
import re
import sys

SKIP_DIRS = {"Intermediate", "ThirdParty", "Binaries", "Content", "Resources", "Shaders", "Build",
             "DerivedDataCache", "Saved", "Documentation", "Extras"}
SOURCE_ROOTS = ("Source/Runtime", "Source/Developer", "Source/Editor", "Plugins")

# ---------------------------------------------------------------------------------------------
# a tiny C++ type-expression parser: Name<args...> with trailing '*'s
# ---------------------------------------------------------------------------------------------

class TypeNode:
    __slots__ = ("name", "args", "ptr")

    def __init__(self, name: str, args: list["TypeNode"], ptr: int):
        self.name, self.args, self.ptr = name, args, ptr

    def render(self) -> str:
        s = self.name
        if self.args:
            s += "<" + ", ".join(a.render() for a in self.args) + ">"
        return s + "*" * self.ptr


def parse_type(text: str) -> TypeNode | None:
    toks = re.findall(r"[A-Za-z_]\w*(?:::\w+)*|[<>,*&]", text)
    pos = 0

    def node() -> TypeNode | None:
        nonlocal pos
        # drop cv / elaborated keywords wherever they appear inside the expression
        while pos < len(toks) and toks[pos] in ("class", "struct", "enum", "const", "volatile",
                                                 "mutable", "typename"):
            pos += 1
        if pos >= len(toks) or not re.match(r"[A-Za-z_]", toks[pos]):
            return None
        name = toks[pos]
        pos += 1
        args: list[TypeNode] = []
        if pos < len(toks) and toks[pos] == "<":
            pos += 1
            while pos < len(toks) and toks[pos] != ">":
                a = node()
                if a is None:
                    return None
                args.append(a)
                if pos < len(toks) and toks[pos] == ",":
                    pos += 1
            pos += 1  # '>'
        ptr = 0
        while pos < len(toks) and toks[pos] in ("*", "&", "const"):
            ptr += toks[pos] == "*"
            pos += 1
        n = TypeNode(name, args, ptr)
        if name == "TObjectPtr" and len(args) == 1:  # TObjectPtr<T> is T* to UHT
            inner = args[0]
            n = TypeNode(inner.name, inner.args, inner.ptr + 1 + ptr)
        return n

    n = node()
    return n if n is not None and pos >= len(toks) else None


def bare(name: str) -> str:
    """UE C++ name -> the pool name the header uses (UPawn -> Pawn, APawn -> Pawn)."""
    name = name.split("::")[-1]
    if len(name) > 1 and name[0] in "UAFI" and name[1].isupper():
        return name[1:]
    return name


def same_name(header: str, source_bare: str) -> bool:
    return header == source_bare or header.startswith(source_bare + "_")


# ---------------------------------------------------------------------------------------------
# the SDK header
# ---------------------------------------------------------------------------------------------

PATH_RE = re.compile(r"^// //Script/([^/]+)/(\S+)$")
OPEN_RE = re.compile(r"^struct (\w+)(?: : public (\w+))?$")
MEMBER_RE = re.compile(r"^    (?P<decl>.+?) (?P<name>\w+)(?: : \d+)?; // 0x[0-9A-F]+ \(0x[0-9A-F]+\) (?P<kind>\w+)")


def read_header(path: pathlib.Path):
    structs = []  # (module, uname, header_name, super, [(decl, name, kind)])
    cur = None
    for line in path.read_text(encoding="utf-8").splitlines():
        m = PATH_RE.match(line)
        if m:
            cur = {"module": m.group(1), "uname": m.group(2), "hname": None, "super": None, "members": []}
            structs.append(cur)
            continue
        if cur is None:
            continue
        m = OPEN_RE.match(line)
        if m and cur["hname"] is None:
            cur["hname"], cur["super"] = m.group(1), m.group(2)
            continue
        m = MEMBER_RE.match(line)
        if m and m.group("kind") != "PADDING":
            cur["members"].append((m.group("decl"), m.group("name"), m.group("kind")))
        elif line.startswith("}"):
            cur = None
    return structs


def class_subclasses(structs) -> set[str]:
    """Header names whose super chain reaches Class: UClass and every UClass subclass."""
    sup = {s["hname"]: s["super"] for s in structs if s["hname"]}
    out = set()
    for n in sup:
        k, depth = n, 0
        while k and depth < 32:
            if k == "Class":
                out.add(n)
                break
            k, depth = sup.get(k), depth + 1
    return out | {"Class"}


# ---------------------------------------------------------------------------------------------
# the engine source
# ---------------------------------------------------------------------------------------------

def strip_comments(src: str) -> str:
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c == '"':
            j = i + 1
            while j < n and src[j] != '"':
                j += 2 if src[j] == "\\" else 1
            out.append(src[i:j + 1])
            i = j + 1
        elif src.startswith("//", i):
            j = src.find("\n", i)
            i = n if j < 0 else j
        elif src.startswith("/*", i):
            j = src.find("*/", i + 2)
            i = n if j < 0 else j + 2
            out.append(" ")
        else:
            out.append(c)
            i += 1
    return "".join(out)


BAL = r"\((?:[^()]|\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\))*\)"
DECL_RE = re.compile(r"\b(UCLASS|USTRUCT|UINTERFACE)\s*" + BAL + r"\s*(?:class|struct)\s+(?:\w+_API\s+)?(?:DEPRECATED\w*\s*\([^)]*\)\s*)?(\w+)")
PROP_RE = re.compile(r"\bUPROPERTY\s*" + BAL + r"\s*(?P<decl>[^;{}]+?)\s*(?:=[^;]*|\{[^;{}]*\})?;")


def module_of(path: str) -> str | None:
    parts = path.replace("\\", "/").split("/")
    for i in range(len(parts) - 1, 0, -1):
        if parts[i] in ("Classes", "Public", "Private", "Internal"):
            return parts[i - 1]
    return None


def index_engine(engine: pathlib.Path, wanted: set[str]):
    """bare class name -> [(module, file, body_text)] for every UCLASS/USTRUCT/UINTERFACE."""
    idx: dict[str, list] = collections.defaultdict(list)
    files = 0
    for root in SOURCE_ROOTS:
        base = engine / "Engine" / root
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
            for fn in filenames:
                if not fn.endswith(".h"):
                    continue
                p = os.path.join(dirpath, fn)
                try:
                    raw = open(p, encoding="utf-8", errors="replace").read()
                except OSError:
                    continue
                files += 1
                if "UCLASS" not in raw and "USTRUCT" not in raw and "UINTERFACE" not in raw:
                    continue
                text = strip_comments(raw)
                hits = list(DECL_RE.finditer(text))
                for k, m in enumerate(hits):
                    name = bare(m.group(2))
                    if name not in wanted:
                        continue
                    end = hits[k + 1].start() if k + 1 < len(hits) else len(text)
                    idx[name].append((module_of(p), p, text[m.end():end]))
    return idx, files


def source_members(body: str) -> dict[str, str]:
    out: dict[str, str] = {}
    for m in PROP_RE.finditer(body):
        decl = re.sub(r"\s+", " ", m.group("decl")).strip()
        decl = re.sub(r"\s*:\s*\d+$", "", decl)            # bitfield
        decl = re.sub(r"\s*\[[^\]]*\]$", "", decl)          # C array
        mm = re.match(r"(?:UE_DEPRECATED\w*\s*\([^)]*\)\s*)?(.*\S)\s+(\w+)$", decl)
        if mm and mm.group(2) not in out:
            out[mm.group(2)] = mm.group(1)
    return out


# ---------------------------------------------------------------------------------------------
# comparison
# ---------------------------------------------------------------------------------------------

def is_class_node(n: TypeNode, classy: set[str]) -> bool:
    if n.name in ("TSubclassOf", "TSoftClassPtr"):
        return True
    return n.ptr >= 1 and bare(n.name) in classy


def expected(n: TypeNode) -> str:
    if n.name in ("TSubclassOf", "TSoftClassPtr") and n.args:
        meta = bare(n.args[0].name)
        if meta == "Object":
            return "UClass*" if n.name == "TSubclassOf" else "TSoftClassPtr<UObject>"
        return f"{n.name}<class {meta}>"
    b = bare(n.name)
    return "UClass*" if b == "Class" else f"class {b}*"


def header_matches(h: TypeNode, n: TypeNode) -> bool:
    want = expected(n)
    if want in ("UClass*", "TSoftClassPtr<UObject>"):
        return h.render() == want
    if n.name in ("TSubclassOf", "TSoftClassPtr"):
        return h.name == n.name and len(h.args) == 1 and h.args[0].ptr == 0 \
            and same_name(h.args[0].name, bare(n.args[0].name))
    return h.ptr == 1 and not h.args and same_name(h.name, bare(n.name))


def compare(h: TypeNode | None, n: TypeNode | None, classy: set[str], slots: list):
    """Walk both trees in step; record (ok, header, expected) for every class-valued SOURCE slot,
    and a failure for every class-valued HEADER slot whose source slot is not class-valued."""
    if n is not None and is_class_node(n, classy):
        slots.append((h is not None and header_matches(h, n), h.render() if h else "?", expected(n)))
        return
    if h is not None and (h.name in ("TSubclassOf", "TSoftClassPtr", "UClass")):
        slots.append((False, h.render(), n.render() if n else "?"))
        return
    if h is None or n is None:
        return
    if n.name in ("TArray", "TSet", "TMap", "TOptional") and len(h.args) == len(n.args):
        for a, b in zip(h.args, n.args):
            compare(a, b, classy, slots)


def header_looks_classy(decl: str, kind: str) -> bool:
    return kind in ("ClassProperty", "SoftClassProperty") or bool(
        re.search(r"TSubclassOf<|TSoftClassPtr<|\bUClass\*", decl))


def has_class_node(n: TypeNode | None, classy: set[str]) -> bool:
    """Parsed, not pattern-matched: USoundClass / FMetasoundFrontendClass end in "Class" and are
    not UClass types -- a name regex selected eleven of them on EVERSPACE 2."""
    if n is None:
        return False
    return is_class_node(n, classy) or any(has_class_node(a, classy) for a in n.args)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--header", required=True, help="an exported SDK header (.h)")
    ap.add_argument("--engine", required=True, help="a UE install root, e.g. .../Epic Games/UE_5.6")
    ap.add_argument("--json", help="write every checked row here")
    ap.add_argument("--show", type=int, default=25, help="print at most this many rows per category")
    a = ap.parse_args()

    header, engine = pathlib.Path(a.header), pathlib.Path(a.engine)
    if not header.is_file():
        print(f"ERROR: header {header} does not exist"); return 2
    if not (engine / "Engine" / "Source" / "Runtime").is_dir():
        print(f"ERROR: {engine} is not a UE install root (no Engine/Source/Runtime)"); return 2

    structs = read_header(header)
    native = [s for s in structs if s["hname"]]
    if not native:
        print("ERROR: no //Script/ struct in the header -- nothing to check"); return 2
    classy = class_subclasses(structs)
    wanted = {s["uname"] for s in native}
    print(f"header : {header} -- {len(native)} native structs, {len(classy)} UClass types")
    idx, nfiles = index_engine(engine, wanted)
    print(f"engine : {engine} -- {nfiles} headers read, {sum(1 for w in wanted if w in idx)} of {len(wanted)} native types located")

    rows, cats = [], collections.Counter()
    for s in native:
        cands = idx.get(s["uname"], [])
        same_mod = [c for c in cands if c[0] == s["module"]]
        cands = same_mod or cands
        for decl, name, kind in s["members"]:
            src_type = None
            for _mod, path, body in cands:
                members = source_members(body)
                # UHT strips the _DEPRECATED suffix from the property's FName, so the runtime
                # name the header carries is the C++ member name WITHOUT it.
                t = members.get(name, members.get(name + "_DEPRECATED"))
                if t is not None:
                    src_type, src_file = t, path
                    break
            hclassy = header_looks_classy(decl, kind)
            sclassy = src_type is not None and has_class_node(parse_type(src_type), classy)
            if not hclassy and not sclassy:
                continue
            row = {"module": s["module"], "class": s["uname"], "member": name, "kind": kind,
                   "header": decl, "source": src_type}
            if not cands:
                row["result"] = "NO-SOURCE-CLASS"
            elif src_type is None:
                row["result"] = "NO-SOURCE-MEMBER"
            else:
                h, n = parse_type(decl), parse_type(src_type)
                slots = []
                compare(h, n, classy, slots)
                if not slots:
                    row["result"] = "NOT-CLASS-VALUED"
                else:
                    row["expected"] = "; ".join(x[2] for x in slots)
                    row["result"] = "MATCH" if all(x[0] for x in slots) else "MISMATCH"
                row["file"] = os.path.relpath(src_file, engine).replace("\\", "/")
            rows.append(row)
            cats[row["result"]] += 1

    print()
    for k in ("MATCH", "MISMATCH", "NOT-CLASS-VALUED", "NO-SOURCE-MEMBER", "NO-SOURCE-CLASS"):
        print(f"  {k:<17} {cats[k]}")
    for k in ("MISMATCH", "NOT-CLASS-VALUED", "NO-SOURCE-MEMBER"):
        sel = [r for r in rows if r["result"] == k]
        if sel:
            print(f"\n-- {k} ({len(sel)}; first {min(len(sel), a.show)})")
            for r in sel[:a.show]:
                print(f"  {r['module']}/{r['class']}::{r['member']}  [{r['kind']}]")
                print(f"      header  : {r['header']}")
                print(f"      source  : {r['source']}" + (f"   -> expected {r['expected']}" if r.get("expected") else ""))
    nosrc = collections.Counter(r["module"] for r in rows if r["result"] == "NO-SOURCE-CLASS")
    if nosrc:
        print("\n-- NO-SOURCE-CLASS by module:", nosrc.most_common(12))
    if a.json:
        pathlib.Path(a.json).write_text(json.dumps(rows, indent=1), encoding="utf-8")
        print(f"\nrows -> {a.json}")
    return 1 if cats["MISMATCH"] else 0


if __name__ == "__main__":
    sys.exit(main())
