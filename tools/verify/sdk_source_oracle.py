r"""Check an exported SDK header's CLASS-VALUED members against the engine SOURCE that declared them.

    py tools/verify/sdk_source_oracle.py --header out/sdk-live/es2_3587.h ^
        --engine "C:/Program Files/Epic Games/UE_5.6"  [--json out/sdk-live/oracle.json]
    py tools/verify/sdk_source_oracle.py --selftest   # a synthetic engine tree + header; no install needed

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
  Only the CLASS-VALUED slots are judged: a MATCH says those slots are right, not that the whole
  line is (a TMap's non-class key or value is never compared here). A container of a different
  KIND around a right slot is a MISMATCH. Type names are compared without the U/A prefix and
  UHT's DEPRECATED_ prefix; a header name the export outer-qualified (`Name_Qualifier`) is
  resolved to its UE name through the header's own path comment, never by a prefix test.
  A member is found by the name the header carries -- its `[UE name: X]` tag when the export
  renamed it -- exactly, then as `Foo_DEPRECATED` (UHT strips that suffix from the FName), then
  case-insensitively (the runtime name pool keeps the FIRST casing registered).

CATEGORIES
  MATCH / MISMATCH      a pair was found and its class slots compared.
  NOT-CLASS-VALUED      the header says class-valued, the source slot is not.          (fails)
  NO-SOURCE-MEMBER      a class-valued header member of a LOCATED class was not found. (fails)
  BLOB                  the source is class-valued, the header writes raw bytes: the layout is
                        kept and the type is not expressed (every TOptional today).
  UNPAIRED              a member of a located class, not class-valued in the header, whose
                        source was not found: counted so a lookup miss is never silent.
  NO-SOURCE-CLASS       the owning class is not in this engine tree.

FAILING LOUDLY (working-lessons Sec.1)
  A header or engine root that does not exist, or a header with no native struct, is an error.
  Exit 0 = every pair that could be judged matched. Exit 1 = a MISMATCH, NOT-CLASS-VALUED or
  NO-SOURCE-MEMBER row. Exit 2 = could not check. --selftest exits 1 on any unexpected verdict.
"""
from __future__ import annotations

import argparse
import collections
import json
import os
import pathlib
import re
import sys
import tempfile

SKIP_DIRS = {"Intermediate", "ThirdParty", "Binaries", "Content", "Resources", "Shaders", "Build",
             "DerivedDataCache", "Saved", "Documentation", "Extras"}
SOURCE_ROOTS = ("Source/Runtime", "Source/Developer", "Source/Editor", "Plugins")
CONTAINERS = ("TArray", "TSet", "TMap", "TOptional")
FAILING = ("MISMATCH", "NOT-CLASS-VALUED", "NO-SOURCE-MEMBER")
REPORTED = ("MATCH", "MISMATCH", "NOT-CLASS-VALUED", "NO-SOURCE-MEMBER", "BLOB", "UNPAIRED", "NO-SOURCE-CLASS")

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
    """UE C++ name -> the pool name the header uses (UPawn -> Pawn, APawn -> Pawn). UHT also strips
    the DEPRECATED_ prefix a `UCLASS(Deprecated)` class carries (UDEPRECATED_Foo -> Foo)."""
    name = name.split("::")[-1]
    if len(name) > 1 and name[0] in "UAFI" and name[1].isupper():
        name = name[1:]
    if name.startswith("DEPRECATED_"):
        name = name[len("DEPRECATED_"):]
    return name


# header type name -> the UE name its //Script/ path comment carries; filled by run() from the header
HEADER_UNAME: dict[str, str] = {}


def same_name(header: str, source_bare: str) -> bool:
    """The header names a type by its pool name, which is the UE name unless an ambiguity made the
    export qualify it (`Name_Qualifier`). Resolved through the header's OWN path comments, not a
    prefix test: a prefix would also accept a different class that happens to be called `Pawn_X`."""
    return HEADER_UNAME.get(header, header) == source_bare


# ---------------------------------------------------------------------------------------------
# the SDK header
# ---------------------------------------------------------------------------------------------

PATH_RE = re.compile(r"^// //Script/([^/]+)/(\S+)$")
OPEN_RE = re.compile(r"^struct (\w+)(?: : public (\w+))?$")
# A raw-bytes member (`uint8_t Foo[0x18];`) is parsed too: skipping it hid every TOptional from both
# sides. `[UE name: X]` is the name the export had to rename away from (a keyword, a type clash).
MEMBER_RE = re.compile(r"^    (?P<decl>.+?) (?P<name>\w+)(?P<blob>\[0x[0-9A-Fa-f]+\])?(?: : \d+)?; "
                       r"// 0x[0-9A-F]+ \(0x[0-9A-F]+\) (?P<kind>\w+)(?: \[UE name: (?P<uename>[^\]]+)\])?")


def read_header(path: pathlib.Path):
    structs = []
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
            cur["members"].append({"decl": m.group("decl"), "name": m.group("name"), "kind": m.group("kind"),
                                   "lookup": m.group("uename") or m.group("name"), "blob": bool(m.group("blob"))})
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

def strip_comments_and_strings(src: str) -> str:
    """Comments go; a string literal keeps its quotes but its body becomes 'x's -- a specifier such
    as ToolTip="a (b" or a UE_DEPRECATED message holding `X::Y` must not steer the paren balance or
    end a declaration early."""
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c == '"':
            j = i + 1
            while j < n and src[j] != '"':
                j += 2 if src[j] == "\\" else 1
            out.append('"' + "x" * max(0, min(j, n) - i - 1) + '"')
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
DECL_HEAD_RE = re.compile(r"\b(UCLASS|USTRUCT|UINTERFACE)\s*" + BAL + r"\s*(?:class|struct)\b")
# `UPROPERTY(...);` with a stray semicolon compiles and UHT accepts it (692 of them in UE 5.6).
PROP_RE = re.compile(r"\bUPROPERTY\s*" + BAL + r"\s*;?\s*(?P<decl>[^;{}]+?)\s*(?:=[^;]*|\{[^;{}]*\})?;")
IDENT_RE = re.compile(r"[A-Za-z_]\w*")


def declared_name(text: str, pos: int) -> tuple[str | None, int]:
    """The name after `class` / `struct`: the LAST identifier before the base-clause colon or the
    body brace, skipping parenthesised groups and [[attributes]]. So `class ENGINE_VTABLE UNetDriver`,
    `class UE_DEPRECATED(5.3, "...") MOD_API UFoo` and `struct alignas(8) FBar` all name the type,
    where a fixed `\\w+_API` slot captured the macro."""
    i, n, last = pos, len(text), None
    while i < n:
        c = text[i]
        if c in ":{;":
            break
        if c == "(":
            depth = 0
            while i < n:
                if text[i] == "(":
                    depth += 1
                elif text[i] == ")":
                    depth -= 1
                    if depth == 0:
                        break
                i += 1
            i += 1
            continue
        if text.startswith("[[", i):
            j = text.find("]]", i)
            i = n if j < 0 else j + 2
            continue
        m = IDENT_RE.match(text, i)
        if m:
            if m.group(0) != "final":
                last = m.group(0)
            i = m.end()
            continue
        i += 1
    return last, i


_MODULE_CACHE: dict[str, str | None] = {}


def module_of(path: str) -> str | None:
    """The module is the nearest directory that holds `<dir>.Build.cs` -- not the directory in front
    of Public/Private, which names `Core` for IrisCore and `Import` for the Interchange modules."""
    d = os.path.dirname(os.path.abspath(path))
    walked = []
    result = None
    while True:
        if d in _MODULE_CACHE:
            result = _MODULE_CACHE[d]
            break
        walked.append(d)
        base = os.path.basename(d)
        if base and os.path.isfile(os.path.join(d, base + ".Build.cs")):
            result = base
            break
        parent = os.path.dirname(d)
        if parent == d:
            break
        d = parent
    for w in walked:
        _MODULE_CACHE[w] = result
    return result


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
                text = strip_comments_and_strings(raw)
                heads = list(DECL_HEAD_RE.finditer(text))
                for k, m in enumerate(heads):
                    cname, body_start = declared_name(text, m.end())
                    if not cname:
                        continue
                    name = bare(cname)
                    if name not in wanted:
                        continue
                    end = heads[k + 1].start() if k + 1 < len(heads) else len(text)
                    idx[name].append((module_of(p), p, text[body_start:end]))
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


def find_member(members: dict[str, str], name: str) -> str | None:
    t = members.get(name, members.get(name + "_DEPRECATED"))
    if t is not None:
        return t
    low = name.lower()
    for k, v in members.items():
        if k.lower() in (low, low + "_deprecated"):
            return v
    return None


# ---------------------------------------------------------------------------------------------
# comparison
# ---------------------------------------------------------------------------------------------

def is_class_node(n: TypeNode, classy: set[str]) -> bool:
    if n.name in ("TSubclassOf", "TSoftClassPtr"):
        return True
    return n.ptr >= 1 and bare(n.name) in classy


def has_class_node(n: TypeNode | None, classy: set[str]) -> bool:
    """Parsed, not pattern-matched: USoundClass / FMetasoundFrontendClass end in "Class" and are
    not UClass types -- a name regex selected eleven of them on EVERSPACE 2."""
    if n is None:
        return False
    return is_class_node(n, classy) or any(has_class_node(a, classy) for a in n.args)


def expected(n: TypeNode, classy: set[str]) -> str:
    if n.name in ("TSubclassOf", "TSoftClassPtr") and n.args:
        meta = bare(n.args[0].name)
        if meta == "Object":
            return "UClass*" if n.name == "TSubclassOf" else "TSoftClassPtr<UObject>"
        return f"{n.name}<class {meta}>"
    if n.name in CONTAINERS:
        return n.name + "<" + ", ".join(expected(a, classy) if has_class_node(a, classy) else a.render()
                                        for a in n.args) + ">"
    b = bare(n.name)
    return "UClass*" if b == "Class" else f"class {b}*"


def header_matches(h: TypeNode, n: TypeNode, classy: set[str]) -> bool:
    want = expected(n, classy)
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
        slots.append((h is not None and header_matches(h, n, classy), h.render() if h else "?",
                      expected(n, classy)))
        return
    if h is not None and (h.name in ("TSubclassOf", "TSoftClassPtr", "UClass")):
        slots.append((False, h.render(), n.render() if n else "?"))
        return
    if h is None or n is None:
        return
    if n.name in CONTAINERS and has_class_node(n, classy):
        # The container around a right class slot must be the right container too.
        if h.name != n.name or len(h.args) != len(n.args):
            slots.append((False, h.render(), expected(n, classy)))
            return
        for a, b in zip(h.args, n.args):
            compare(a, b, classy, slots)


def header_looks_classy(decl: str, kind: str) -> bool:
    return kind in ("ClassProperty", "SoftClassProperty") or bool(
        re.search(r"TSubclassOf<|TSoftClassPtr<|\bUClass\*", decl))


# ---------------------------------------------------------------------------------------------
# the run
# ---------------------------------------------------------------------------------------------

def exit_code(cats: collections.Counter) -> int:
    return 1 if any(cats[k] for k in FAILING) else 0


def run(header: pathlib.Path, engine: pathlib.Path, show: int = 25, json_path: str | None = None,
        quiet: bool = False):
    """(rows, category counter, exit code). Prints the report unless quiet."""
    say = (lambda *a, **k: None) if quiet else print
    if not header.is_file():
        say(f"ERROR: header {header} does not exist")
        return [], collections.Counter(), 2
    if not (engine / "Engine" / "Source" / "Runtime").is_dir():
        say(f"ERROR: {engine} is not a UE install root (no Engine/Source/Runtime)")
        return [], collections.Counter(), 2

    structs = read_header(header)
    native = [s for s in structs if s["hname"]]
    if not native:
        say("ERROR: no //Script/ struct in the header -- nothing to check")
        return [], collections.Counter(), 2
    classy = class_subclasses(structs)
    HEADER_UNAME.clear()
    HEADER_UNAME.update({s["hname"]: s["uname"] for s in native})
    wanted = {s["uname"] for s in native}
    say(f"header : {header} -- {len(native)} native structs, {len(classy)} UClass types")
    idx, nfiles = index_engine(engine, wanted)
    say(f"engine : {engine} -- {nfiles} headers read, {sum(1 for w in wanted if w in idx)} of "
        f"{len(wanted)} native types located")

    rows, cats = [], collections.Counter()
    for s in native:
        cands = idx.get(s["uname"], [])
        same_mod = [c for c in cands if c[0] == s["module"]]
        cands = same_mod or cands
        parsed = [(path, source_members(body)) for _mod, path, body in cands]
        for mem in s["members"]:
            decl, name, kind = mem["decl"], mem["name"], mem["kind"]
            src_type = src_file = None
            for path, members in parsed:
                t = find_member(members, mem["lookup"])
                if t is not None:
                    src_type, src_file = t, path
                    break
            hclassy = header_looks_classy(decl, kind) and not mem["blob"]
            n = parse_type(src_type) if src_type is not None else None
            sclassy = has_class_node(n, classy)
            row = {"module": s["module"], "class": s["uname"], "member": name, "kind": kind,
                   "header": decl + (" [blob]" if mem["blob"] else ""), "source": src_type}
            if src_file:
                row["file"] = os.path.relpath(src_file, engine).replace("\\", "/")
            if not cands:
                if not (hclassy or kind in ("ClassProperty", "SoftClassProperty")):
                    continue
                row["result"] = "NO-SOURCE-CLASS"
            elif src_type is None:
                if hclassy:
                    row["result"] = "NO-SOURCE-MEMBER"
                else:
                    row["result"] = "UNPAIRED"
            elif mem["blob"]:
                if not sclassy:
                    continue
                row["result"] = "BLOB"
            elif not hclassy and not sclassy:
                continue
            else:
                slots = []
                compare(parse_type(decl), n, classy, slots)
                if not slots:
                    row["result"] = "NOT-CLASS-VALUED"
                else:
                    row["expected"] = "; ".join(x[2] for x in slots)
                    row["result"] = "MATCH" if all(x[0] for x in slots) else "MISMATCH"
            rows.append(row)
            cats[row["result"]] += 1

    say()
    say("  (MATCH = the class-valued slots are right; the rest of a line is not judged)")
    for k in REPORTED:
        say(f"  {k:<17} {cats[k]}" + ("   <- fails" if k in FAILING and cats[k] else ""))
    for k in FAILING + ("BLOB",):
        sel = [r for r in rows if r["result"] == k]
        if sel:
            say(f"\n-- {k} ({len(sel)}; first {min(len(sel), show)})")
            for r in sel[:show]:
                say(f"  {r['module']}/{r['class']}::{r['member']}  [{r['kind']}]")
                say(f"      header  : {r['header']}")
                say(f"      source  : {r['source']}" + (f"   -> expected {r['expected']}" if r.get("expected") else ""))
    unp = [r for r in rows if r["result"] == "UNPAIRED"]
    if unp:
        say(f"\n-- UNPAIRED ({len(unp)}; not class-valued in the header, source not found): "
            + ", ".join(f"{r['class']}::{r['member']}" for r in unp[:min(len(unp), show)]))
    nosrc = collections.Counter(r["module"] for r in rows if r["result"] == "NO-SOURCE-CLASS")
    if nosrc:
        say("\n-- NO-SOURCE-CLASS by module:", nosrc.most_common(12))
    if json_path:
        pathlib.Path(json_path).write_text(json.dumps(rows, indent=1), encoding="utf-8")
        say(f"\nrows -> {json_path}")
    return rows, cats, exit_code(cats)


# ---------------------------------------------------------------------------------------------
# --selftest: every blind spot the review of 2026-09-27 (wf_63e981ac-5e4) found, as a fixture
# ---------------------------------------------------------------------------------------------

SELFTEST_SOURCE = r'''
#pragma once
#include "Thing.generated.h"

UCLASS()
class MOD_API UThing : public UObject
{
	GENERATED_BODY()
public:
	UPROPERTY(EditAnywhere, meta=(ToolTip="a (b"))
	TSubclassOf<AActor> ActorClass;

	UPROPERTY();
	TArray<TSubclassOf<AActor>> ActorClasses;

	UPROPERTY()
	TSubclassOf<UObject> AnyClass_DEPRECATED;

	UPROPERTY()
	TObjectPtr<UBlueprintGeneratedClass> Director;

	UPROPERTY()
	TSoftClassPtr<APawn> SoftPawn;

	UPROPERTY()
	TSubclassOf<APawn> Decal;

	UPROPERTY()
	TSubclassOf<AActor> Class;

	UPROPERTY()
	TSet<TSubclassOf<AActor>> WrongKind;

	UPROPERTY()
	TOptional<TSubclassOf<AActor>> Maybe;

	UPROPERTY()
	TObjectPtr<USoundClass> SoundClassObject;
};

UCLASS()
class ENGINE_VTABLE UE_DEPRECATED(5.3, "Use UOther::Thing instead") MOD_API UMacroed : public UObject
{
	GENERATED_BODY()
	UPROPERTY()
	TSubclassOf<AActor> Spawn;
};

UCLASS(Deprecated)
class UDEPRECATED_Old : public UObject
{
	GENERATED_BODY()
	UPROPERTY()
	TSubclassOf<AActor> Legacy;
};
'''

SELFTEST_HEADER = """// Auto-generated by UE5CEDumper

// //Script/CoreUObject/Class
struct Class : public Struct
{
}; // Size: 0x0230

// //Script/Engine/BlueprintGeneratedClass
struct BlueprintGeneratedClass : public Class
{
}; // Size: 0x0300

// //Script/Mod/Pawn
struct Pawn_Mod : public Object
{
}; // Size: 0x0028

// //Script/Mod/Pawn_X
struct Pawn_X : public Object
{
}; // Size: 0x0028

// //Script/Mod/Thing
struct Thing : public Object
{
    TSubclassOf<class Actor> ActorClass; // 0x0028 (0x0008) ClassProperty
    TArray<TSubclassOf<class Actor>> ActorClasses; // 0x0030 (0x0010) ArrayProperty
    UClass* AnyClass; // 0x0040 (0x0008) ClassProperty
    class BlueprintGeneratedClass* Director; // 0x0048 (0x0008) ClassProperty
    TSoftClassPtr<class Pawn_Mod> SoftPawn; // 0x0050 (0x0028) SoftClassProperty
    TSubclassOf<class Pawn_X> decal; // 0x0078 (0x0008) ClassProperty
    TSubclassOf<class Actor> Class_0; // 0x0080 (0x0008) ClassProperty [UE name: Class]
    TArray<TSubclassOf<class Actor>> WrongKind; // 0x0088 (0x0050) SetProperty
    uint8_t Maybe[0x10]; // 0x00D8 (0x0010) OptionalProperty
    class SoundClass* SoundClassObject; // 0x00E8 (0x0008) ObjectProperty
    int32_t Unpaired; // 0x00F0 (0x0004) IntProperty
    UClass* Ghost; // 0x00F8 (0x0008) ClassProperty
}; // Size: 0x0100

// //Script/Mod/Macroed
struct Macroed : public Object
{
    TSubclassOf<class Actor> Spawn; // 0x0028 (0x0008) ClassProperty
}; // Size: 0x0030

// //Script/Mod/Old
struct Old : public Object
{
    TSubclassOf<class Actor> Legacy; // 0x0028 (0x0008) ClassProperty
}; // Size: 0x0030

// //Script/Game/GameOnly
struct GameOnly : public Object
{
    TSubclassOf<class Actor> Anything; // 0x0028 (0x0008) ClassProperty
}; // Size: 0x0030
"""

SELFTEST_EXPECT = {
    ("Thing", "ActorClass"): "MATCH",          # a '(' inside a specifier string
    ("Thing", "ActorClasses"): "MATCH",        # `UPROPERTY();`
    ("Thing", "AnyClass"): "MATCH",            # _DEPRECATED; TSubclassOf<UObject> -> UClass*
    ("Thing", "Director"): "MATCH",            # TObjectPtr<a UClass subclass>
    ("Thing", "SoftPawn"): "MATCH",            # a qualified header name, resolved by its path
    ("Thing", "decal"): "MISMATCH",            # case-insensitive lookup; Pawn_X is NOT Pawn
    ("Thing", "Class_0"): "MATCH",             # found by its [UE name: Class] tag
    ("Thing", "WrongKind"): "MISMATCH",        # TArray around a TSet's right slot
    ("Thing", "Maybe"): "BLOB",                # raw bytes for a class-valued TOptional
    ("Thing", "Unpaired"): "UNPAIRED",         # counted, never silent
    ("Thing", "Ghost"): "NO-SOURCE-MEMBER",    # class-valued and not found -> fails the run
    ("Macroed", "Spawn"): "MATCH",             # ENGINE_VTABLE / UE_DEPRECATED("...::...") before the name
    ("Old", "Legacy"): "MATCH",                # UDEPRECATED_Old is `Old` at runtime
    ("GameOnly", "Anything"): "NO-SOURCE-CLASS",
}


def selftest() -> int:
    with tempfile.TemporaryDirectory() as tmp:
        root = pathlib.Path(tmp)
        mod = root / "UE" / "Engine" / "Source" / "Runtime" / "Mod"
        (mod / "Public").mkdir(parents=True)
        (mod / "Mod.Build.cs").write_text("// module marker\n", encoding="utf-8")
        (mod / "Public" / "Thing.h").write_text(SELFTEST_SOURCE, encoding="utf-8")
        header = root / "sdk.h"
        header.write_text(SELFTEST_HEADER, encoding="utf-8")
        rows, cats, rc = run(header, root / "UE", quiet=True)
    got = {(r["class"], r["member"]): r["result"] for r in rows}
    bad = [(k, want, got.get(k, "(no row)")) for k, want in SELFTEST_EXPECT.items() if got.get(k) != want]
    extra = sorted(set(got) - set(SELFTEST_EXPECT))
    if extra:
        bad.append(("unexpected rows", "none", ", ".join(f"{c}::{m}={got[(c, m)]}" for c, m in extra)))
    if rc != 1:
        bad.append(("exit code", 1, rc))
    # The fixture fails on MISMATCH already, so the exit rule is pinned on its own as well.
    for cats_, want in ((collections.Counter({"NO-SOURCE-MEMBER": 1}), 1),
                        (collections.Counter({"NOT-CLASS-VALUED": 1}), 1),
                        (collections.Counter({"BLOB": 3, "UNPAIRED": 5, "NO-SOURCE-CLASS": 2, "MATCH": 9}), 0)):
        if exit_code(cats_) != want:
            bad.append((f"exit code for {dict(cats_)}", want, exit_code(cats_)))
    for k, want, g in bad:
        print(f"SELFTEST FAIL  {k}: expected {want}, got {g}")
    if bad:
        return 1
    print(f"sdk_source_oracle selftest OK: {len(SELFTEST_EXPECT)} verdicts, exit code {rc}")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--header", help="an exported SDK header (.h)")
    ap.add_argument("--engine", help="a UE install root, e.g. .../Epic Games/UE_5.6")
    ap.add_argument("--json", help="write every checked row here")
    ap.add_argument("--show", type=int, default=25, help="print at most this many rows per category")
    ap.add_argument("--selftest", action="store_true", help="run the synthetic fixture only")
    a = ap.parse_args()
    if a.selftest:
        return selftest()
    if not a.header or not a.engine:
        ap.error("--header and --engine are required (or --selftest)")
    _rows, _cats, rc = run(pathlib.Path(a.header), pathlib.Path(a.engine), a.show, a.json)
    return rc


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main())
