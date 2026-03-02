# ExtractAOBContext.ghidra.py  (v3)
#
# Extracts byte patterns around all code references to GObjects / GNames / GWorld.
# Works in Ghidra Script Manager (GUI) or via pyghidra (headless).
#
# Output: JSON file per game in <PROJECT_DIR>/aob_export/
#         Typical size: 50-300 KB per game
#
# GNames strategy:
#   NamePoolData is often a static local (no PDB symbol), so when label search
#   fails, we fall back to finding FName functions by name and extracting their
#   data references.  The most-referenced writable address = NamePoolData.

import json
import os

# ---------------------------------------------------------------------------
# Ghidra program access
# ---------------------------------------------------------------------------
if 'currentProgram' not in globals():
    currentProgram = getState().getCurrentProgram()

prog     = currentProgram
listing  = prog.getListing()
mem      = prog.getMemory()
symTable = prog.getSymbolTable()
funcMgr  = prog.getFunctionManager()
refMgr   = prog.getReferenceManager()
addrFact = prog.getAddressFactory()
imgBase  = prog.getImageBase().getOffset()

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

LABEL_EXACT = {
    "GObjects": ["GUObjectArray"],
    "GNames":   ["NamePoolData", "GNames"],
    "GWorld":   ["GWorld"],
}

LABEL_CONTAINS = {
    "GObjects": ["GUObjectArray", "GObjectArray"],
    "GNames":   ["NamePoolData", "NamePool", "FNamePool"],
    "GWorld":   ["GWorld", "UWorldProxy"],
}

LABEL_EXCLUDE = [
    "dynamic_initializer", "dynamic_atexit", "initializer$",
    "ForDebugVisualizers", "OwningWorld", "InstancingWorld",
    "ArchiveReader", "ArchiveWriter", "NewProp_", "s_OwningWorld",
    "Debugger", "DebugVisualizers",
    "::vtable", "vftable",
    ".field", ".Opaque", ".CriticalSection", ".AllocatorInstance",
    ".ArrayNum", ".ArrayMax", ".Counter", ".MasterSerial",
    "ObjAvailableList",
]

XREF_TYPE_EXCLUDE = ["PARAM", "THUNK"]

# FName function name patterns for GNames fallback (Tier 4).
# When NamePoolData has no label, we find it by scanning FName function bodies
# for RIP-relative data references to writable memory.
FNAME_FUNCTION_PATTERNS = [
    "FName::ToString",
    "FName::GetPlainNameString",
    "FName::GetDisplayNameEntry",
    "FNamePool::Resolve",
    "FNamePool::Store",
    "FName::Init",
    "FName::FName",
    "FNameEntry::GetAnsiName",
    "FNameEntry::GetWideName",
    "FNameHelper",
    "FNamePool::Find",
]

# Exclude these labeled addresses from Tier 4 voting (compiler/runtime globals
# that appear in many functions but are NOT NamePoolData).
VOTING_EXCLUDE_LABELS = [
    "__security_cookie",
    "__guard",
    "__GSHandler",
    "__ImageBase",
    "__xl_",
    "_tls_",
    "__dyn_tls",
    "_Init_thread",
    "GUObjectArray",    # already found separately
    "GWorld",           # already found separately
]

MANUAL_OVERRIDES = {
    "Octopath_Traveler-Win64-Shipping.exe": {
        "GObjects": [0x29E5C20],
        "GNames":   [0x29DCF08],
    },
}

CONTEXT_BEFORE = 64
CONTEXT_AFTER  = 48
MAX_XREFS      = 100

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def make_addr(offset):
    return addrFact.getDefaultAddressSpace().getAddress(offset)

def rva_val(offset):
    return offset - imgBase

def hex_rva(offset):
    return "0x{:X}".format(rva_val(offset))

def is_excluded(name):
    low = name.lower()
    return any(ex.lower() in low for ex in LABEL_EXCLUDE)


def read_hex(offset, length):
    """Read raw bytes as hex string, byte-by-byte for pyghidra compatibility."""
    try:
        addr = make_addr(offset)
        result = []
        for i in range(length):
            b = mem.getByte(addr.add(i))
            result.append("{:02X}".format(b & 0xFF))
        return " ".join(result)
    except Exception:
        return None


def find_globals_exact(exact_names):
    """Tier 1: Find symbols whose name exactly matches (case-insensitive)."""
    targets = set(n.lower() for n in exact_names)
    found = []
    seen = set()
    for sym in symTable.getAllSymbols(True):
        name = sym.getName()
        if is_excluded(name):
            continue
        if name.lower() not in targets:
            continue
        addr = sym.getAddress()
        key = addr.getOffset()
        if key in seen:
            continue
        seen.add(key)
        found.append({"label": name, "rva": hex_rva(key), "offset": key})
    return found


def find_globals_contains(contain_patterns):
    """Tier 2: Find symbols containing any pattern, excluding noise/sub-fields."""
    found = []
    seen = set()
    for sym in symTable.getAllSymbols(True):
        name = sym.getName()
        if is_excluded(name):
            continue
        name_lower = name.lower()
        for pat in contain_patterns:
            if pat.lower() in name_lower and "." not in name:
                addr = sym.getAddress()
                key = addr.getOffset()
                if key in seen:
                    continue
                seen.add(key)
                found.append({"label": name, "rva": hex_rva(key), "offset": key})
                break
    return found


def find_globals_override(rva_list):
    """Tier 3: Create entries from manual RVA overrides."""
    found = []
    for r in rva_list:
        offset = imgBase + r
        addr = make_addr(offset)
        syms = symTable.getSymbols(addr)
        label = syms[0].getName() if syms and len(syms) > 0 else "manual_0x{:X}".format(r)
        found.append({"label": label, "rva": "0x{:X}".format(r), "offset": offset})
    return found


def get_full_symbol_name(sym):
    """Get namespace-qualified name like 'FName::ToString'."""
    parts = [sym.getName()]
    ns = sym.getParentNamespace()
    while ns is not None:
        ns_name = ns.getName()
        # Stop at global namespace or program name
        if ns_name in ("Global", "", prog_name):
            break
        parts.insert(0, ns_name)
        ns = ns.getParentNamespace()
    return "::".join(parts)


def find_gnames_via_functions():
    """Tier 4 (GNames only): Find NamePoolData by scanning FName function bodies.

    Strategy:
      1. Find FName/FNamePool functions by PDB name (namespace-qualified)
      2. Walk each function's instructions, collect RIP-relative refs to writable memory
      3. Vote: the writable address referenced by the most functions = NamePoolData
      4. Return it as a global entry for normal xref extraction
    """
    print("  [*] Tier 4: Scanning FName function bodies for NamePoolData...")

    # Find FName functions via symbol table (must build full qualified name)
    found_funcs = []
    seen_funcs = set()
    for sym in symTable.getAllSymbols(True):
        full_name = get_full_symbol_name(sym)
        full_lower = full_name.lower()
        for pat in FNAME_FUNCTION_PATTERNS:
            if pat.lower() in full_lower:
                func = funcMgr.getFunctionContaining(sym.getAddress())
                if func and func.getEntryPoint().getOffset() not in seen_funcs:
                    seen_funcs.add(func.getEntryPoint().getOffset())
                    found_funcs.append(func)
                break

    if not found_funcs:
        print("  [!] No FName functions found in symbol table")
        return []

    print("  [*] Found {} FName functions".format(len(found_funcs)))
    for f in found_funcs[:8]:
        print("      {} @ RVA {}".format(f.getName(True), hex_rva(f.getEntryPoint().getOffset())))
    if len(found_funcs) > 8:
        print("      ... and {} more".format(len(found_funcs) - 8))

    # Walk each function, collect data references to writable memory.
    # Track which functions reference each address (vote counting).
    addr_voters = {}  # data_offset -> set of function names

    for func in found_funcs:
        body = func.getBody()
        entry = func.getEntryPoint()
        func_name = func.getName(True)

        inst = listing.getInstructionAt(entry)
        limit = 500  # max instructions per function to avoid very long functions
        count = 0
        while inst is not None and count < limit:
            addr = inst.getAddress()
            if not body.contains(addr):
                break
            count += 1

            for ref in inst.getReferencesFrom():
                to_addr = ref.getToAddress()
                block = mem.getBlock(to_addr)
                # Is it writable non-executable memory? (.data section)
                if block and block.isWrite() and not block.isExecute():
                    doff = to_addr.getOffset()
                    if doff not in addr_voters:
                        addr_voters[doff] = set()
                    addr_voters[doff].add(func_name)

            inst = inst.getNext()

    if not addr_voters:
        print("  [!] No writable data references found in FName functions")
        return []

    # Filter out known false positives (compiler/runtime globals)
    def is_voting_excluded(doff):
        addr = make_addr(doff)
        syms = symTable.getSymbols(addr)
        if syms:
            for s in syms:
                sname = s.getName().lower()
                for excl in VOTING_EXCLUDE_LABELS:
                    if excl.lower() in sname:
                        return True
        return False

    filtered = [(doff, voters) for doff, voters in addr_voters.items()
                if not is_voting_excluded(doff)]

    if not filtered:
        print("  [!] All candidates excluded (all are compiler globals)")
        return []

    # Sort by number of unique functions that reference each address
    sorted_addrs = sorted(filtered, key=lambda x: -len(x[1]))

    # Show top candidates
    print("  [*] Top data reference candidates (after filtering):")
    for doff, voters in sorted_addrs[:5]:
        lbl = ""
        syms = symTable.getSymbols(make_addr(doff))
        if syms and len(syms) > 0:
            lbl = " ({})".format(syms[0].getName())
        print("      RVA {} - referenced by {} functions{}".format(
            hex_rva(doff), len(voters), lbl))

    # Take the top candidate (most functions reference it = NamePoolData)
    best_offset, best_voters = sorted_addrs[0]

    if len(best_voters) < 2:
        print("  [!] Top candidate only referenced by 1 function, might not be NamePoolData")

    # Check if there's an existing label at this address
    best_addr = make_addr(best_offset)
    syms = symTable.getSymbols(best_addr)
    label = "NamePoolData_detected"
    if syms and len(syms) > 0:
        label = syms[0].getName()

    print("  [+] Auto-detected: {} @ RVA {} (voted by {} functions)".format(
        label, hex_rva(best_offset), len(best_voters)))

    return [{
        "label": label,
        "rva": hex_rva(best_offset),
        "offset": best_offset,
        "detected_via": "fname_function_scan",
        "voter_count": len(best_voters),
    }]


def get_xrefs(addr_offset):
    """Get code xrefs TO the address, filtering out PARAM/THUNK types."""
    addr = make_addr(addr_offset)
    refs = []
    for ref in refMgr.getReferencesTo(addr):
        rtype = str(ref.getReferenceType())
        if any(ex in rtype for ex in XREF_TYPE_EXCLUDE):
            continue
        fa = ref.getFromAddress()
        refs.append((fa.getOffset(), rtype))
        if len(refs) >= MAX_XREFS:
            break
    return refs


def get_disasm(start_offset, end_offset):
    """Compact disassembly: list of [rva_hex, bytes_hex, asm_text]."""
    result = []
    try:
        addr = make_addr(start_offset)
        inst = listing.getInstructionContaining(addr)
        if inst is None:
            inst = listing.getInstructionAfter(addr)
        while inst is not None and inst.getAddress().getOffset() < end_offset:
            ia = inst.getAddress().getOffset()
            ib = inst.getBytes()
            bh = " ".join("{:02X}".format(b & 0xFF) for b in ib)
            result.append([hex_rva(ia), bh, str(inst)])
            inst = inst.getNext()
    except Exception:
        pass
    return result


def extract_context(xref_offset, ref_type):
    """Extract byte context and disassembly around one xref point."""
    start = xref_offset - CONTEXT_BEFORE
    end   = xref_offset + CONTEXT_AFTER

    raw = read_hex(start, CONTEXT_BEFORE + CONTEXT_AFTER)
    dis = get_disasm(start, end)

    # Find xref instruction index
    xi = None
    x_rva = rva_val(xref_offset)
    for i, d in enumerate(dis):
        try:
            d_rva = int(d[0], 16)
            if d_rva == x_rva:
                xi = i
                break
            elif d_rva > x_rva:
                xi = max(0, i - 1)
                break
        except Exception:
            pass

    func = funcMgr.getFunctionContaining(make_addr(xref_offset))

    return {
        "rva": hex_rva(xref_offset),
        "ref": ref_type,
        "func": func.getName(True) if func else None,
        "xi": xi,
        "raw": raw,
        "dis": dis,
    }


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
prog_name = prog.getName()
# PROJECT_NAME is injected by run_all_aob_export.py; fall back to binary name for GUI mode
project_name = globals().get('PROJECT_NAME', None)
if project_name:
    print("[*] Project: {}".format(project_name))
print("[*] Binary: {}".format(prog_name))
print("[*] Image base: 0x{:X}".format(imgBase))

output = {
    "project": project_name or prog_name,
    "program": prog_name,
    "image_base": "0x{:X}".format(imgBase),
    "context_bytes": [CONTEXT_BEFORE, CONTEXT_AFTER],
    "targets": {},
}

total_xrefs = 0

for target in ["GObjects", "GNames", "GWorld"]:
    print("\n[*] === {} ===".format(target))

    # Tier 1: exact match
    globals_found = find_globals_exact(LABEL_EXACT[target])

    # Tier 2: contains match (broader)
    if not globals_found:
        print("  [*] No exact match, trying contains search...")
        globals_found = find_globals_contains(LABEL_CONTAINS[target])

    # Tier 3: manual overrides
    if not globals_found and prog_name in MANUAL_OVERRIDES:
        overrides = MANUAL_OVERRIDES[prog_name]
        if target in overrides:
            print("  [*] Using manual override")
            globals_found = find_globals_override(overrides[target])

    # Tier 4 (GNames only): find via FName function body analysis
    if not globals_found and target == "GNames":
        globals_found = find_gnames_via_functions()

    if not globals_found:
        # Diagnostic: show what symbols contain the keywords
        print("  [!] No labels found. Nearby symbols:")
        count = 0
        for sym in symTable.getAllSymbols(True):
            name = sym.getName()
            for pat in LABEL_CONTAINS[target]:
                if pat.lower() in name.lower() and count < 10:
                    print("      {} @ {}".format(name, sym.getAddress()))
                    count += 1
        output["targets"][target] = {"globals": [], "count": 0, "xrefs": []}
        continue

    for g in globals_found:
        extra = ""
        if "detected_via" in g:
            extra = " (auto-detected via {}, {} voters)".format(
                g["detected_via"], g.get("voter_count", "?"))
        print("  [+] {} @ RVA {}{}".format(g["label"], g["rva"], extra))

    xref_list = []
    for g in globals_found:
        xrefs = get_xrefs(g["offset"])
        print("  [*] {}: {} xrefs".format(g["label"], len(xrefs)))

        for xoff, rtype in xrefs:
            try:
                ctx = extract_context(xoff, rtype)
                ctx["global"] = g["label"]
                xref_list.append(ctx)
                total_xrefs += 1
            except Exception as e:
                print("  [!] Error at 0x{:X}: {}".format(xoff, e))

    out_globals = [{"label": g["label"], "rva": g["rva"]} for g in globals_found]
    if globals_found and "detected_via" in globals_found[0]:
        out_globals[0]["detected_via"] = globals_found[0]["detected_via"]
        out_globals[0]["voter_count"] = globals_found[0].get("voter_count", 0)

    output["targets"][target] = {
        "globals": out_globals,
        "count": len(xref_list),
        "xrefs": xref_list,
    }

# ---------------------------------------------------------------------------
# Write output
# ---------------------------------------------------------------------------
# Output directory: alongside this script, or PROJECT_DIR/aob_export if injected
_script_dir = globals().get('_SCRIPT_DIR', os.path.dirname(os.path.abspath(__file__)) if '__file__' in dir() else ".")
out_dir = globals().get('_OUTPUT_DIR', os.path.join(_script_dir, "aob_export"))
if not os.path.exists(out_dir):
    os.makedirs(out_dir)

# Use project name for filename (unique across projects with same binary)
file_base = project_name if project_name else prog_name.replace(".exe", "")
safe_name = file_base.replace(" ", "_").replace(".", "_")
out_path = os.path.join(out_dir, "{}_aob_context.json".format(safe_name))

with open(out_path, "w", encoding="utf-8") as f:
    json.dump(output, f, indent=2, ensure_ascii=False)

size_kb = os.path.getsize(out_path) / 1024.0
print("\n" + "=" * 60)
print("[+] Done! {} xrefs ({:.1f} KB)".format(total_xrefs, size_kb))
print("[+] Output: {}".format(out_path))
print("=" * 60)
