// find_gobjects.java — Ghidra headless (Java / Ghidra 12)
//
// Anchors on the UE log string "Unable to add more objects to disregard for GC pool"
// (which lives in FUObjectArray::AllocateUObjectIndex), finds the functions that
// reference it, and decompiles them + lists the writable-.data globals they touch — so
// you can read off GUObjectArray and the FUObjectArray field layout. Pass a different
// substring as the first arg to anchor on another string.
//
// [VND583-17] The string is searched in BOTH encodings. UE <= 5.7 logs it through TEXT() (UTF-16LE);
// 5.8 made it a UE_CLOGF narrow literal (UObjectArray.cpp @5.8.3-release:267), and a UTF-16-only
// search found 0 occurrences on StackOBot 5.8 Shipping. The 8-byte absolute-address slots that
// hold a hit are searched too, and a reference to a slot counts as a reference to the string:
// a structured-log record can keep its format as a pointer in .rdata.
//
// Usage (read-only):
//   analyzeHeadless <projLoc> <projName> -process -noanalysis -readOnly \
//       -scriptPath <thisDir> -postScript find_gobjects.java
//
// CAVEAT: this relies on Ghidra's auto-analysis having created the code->string
// reference. On large stripped shipping EXEs that reference may be absent (a partial
// analysis won't have it). If this prints "found 0 xrefs", instead resolve the candidate
// functions with patternsleuth's CLI:
//     patternsleuth scan --path game.exe --resolver FUObjectArrayAllocateUObjectIndex --disassemble-merged
// then feed those addresses to decompile_functions.java + find_callers.java.
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceManager;
import java.nio.charset.StandardCharsets;
import java.util.LinkedHashSet;
import java.util.Set;

public class find_gobjects extends GhidraScript {
    public void run() throws Exception {
        Memory mem = currentProgram.getMemory();
        ReferenceManager refmgr = currentProgram.getReferenceManager();
        FunctionManager fm = currentProgram.getFunctionManager();
        println("PROGRAM: " + currentProgram.getName() + "  imageBase=" + currentProgram.getImageBase());

        String[] args = getScriptArgs();
        String needle = args.length > 0 ? args[0] : "Unable to add more objects to disregard for GC pool";

        // Find the string in memory, in both encodings (see the header).
        byte[] utf16 = new byte[needle.length() * 2];
        for (int i = 0; i < needle.length(); i++) { utf16[i * 2] = (byte) (needle.charAt(i) & 0xFF); utf16[i * 2 + 1] = 0; }
        byte[][] pats = { utf16, needle.getBytes(StandardCharsets.US_ASCII) };
        String[] encs = { "UTF-16LE", "narrow" };
        Set<Address> strAddrs = new LinkedHashSet<>();
        for (int k = 0; k < pats.length; k++) {
            int before = strAddrs.size();
            Address a = mem.findBytes(mem.getMinAddress(), pats[k], null, true, monitor);
            while (a != null && strAddrs.size() < 32) { strAddrs.add(a); a = mem.findBytes(a.add(1), pats[k], null, true, monitor); }
            println("found " + (strAddrs.size() - before) + " " + encs[k] + " occurrence(s) of: " + needle);
        }
        // ...and every 8-byte absolute-address slot that holds one of them.
        Set<Address> targets = new LinkedHashSet<>(strAddrs);
        for (Address sa : strAddrs) {
            long v = sa.getOffset();
            byte[] ptr = new byte[8];
            for (int i = 0; i < 8; i++) ptr[i] = (byte) ((v >>> (8 * i)) & 0xFF);
            Address s = mem.findBytes(mem.getMinAddress(), ptr, null, true, monitor);
            for (int n = 0; s != null && n < 16; n++) {
                targets.add(s);
                println("  pointer slot " + s + " -> " + sa);
                s = mem.findBytes(s.add(1), ptr, null, true, monitor);
            }
        }

        // Functions that reference the string (DB refs, then a raw instruction scan fallback).
        Set<Function> funcs = new LinkedHashSet<>();
        for (Address sa : targets)
            for (Reference ref : refmgr.getReferencesTo(sa)) {
                Function f = fm.getFunctionContaining(ref.getFromAddress());
                if (f != null && funcs.add(f)) println("  xref(db) " + ref.getFromAddress() + " -> " + f.getName() + " @ " + f.getEntryPoint());
            }
        if (funcs.isEmpty()) {
            println("(no DB xrefs — scanning instructions...)");
            InstructionIterator all = currentProgram.getListing().getInstructions(true);
            while (all.hasNext() && !monitor.isCancelled()) {
                Instruction ins = all.next();
                for (Reference r : ins.getReferencesFrom())
                    if (targets.contains(r.getToAddress())) {
                        Function f = fm.getFunctionContaining(ins.getAddress());
                        if (f != null && funcs.add(f)) println("  xref(scan) " + ins.getAddress() + " -> " + f.getName() + " @ " + f.getEntryPoint());
                    }
            }
        }
        if (funcs.isEmpty()) { println("found 0 xrefs — see the CAVEAT in this script's header."); return; }

        DecompInterface decomp = new DecompInterface();
        decomp.openProgram(currentProgram);
        for (Function f : funcs) {
            println("\n================ DECOMPILE " + f.getName() + " @ " + f.getEntryPoint() + " ================");
            DecompileResults res = decomp.decompileFunction(f, 90, monitor);
            println(res.decompileCompleted() ? res.getDecompiledFunction().getC() : ("  <decompile failed: " + res.getErrorMessage() + ">"));
            println("---- writable-data globals referenced ----");
            InstructionIterator it = currentProgram.getListing().getInstructions(f.getBody(), true);
            while (it.hasNext()) {
                Instruction ins = it.next();
                for (Reference r : ins.getReferencesFrom()) {
                    Address to = r.getToAddress();
                    if (to != null && mem.contains(to)) {
                        MemoryBlock blk = mem.getBlock(to);
                        if (blk != null && blk.isWrite() && !blk.isExecute())
                            println("  " + ins.getAddress() + "  " + ins.toString() + "  -> " + to + " [" + blk.getName() + "]");
                    }
                }
            }
        }
        println("\nDONE");
    }
}
