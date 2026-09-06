// dump_identity.java — everything that distinguishes one Ghidra program from another, in one
// small TSV: provenance metadata, analysis state, and a DIGEST over the symbol table.
//
// WHY. "Can a .rep be rebuilt from the archived binary?" cannot be answered by looking at a
// rebuilt project and deciding it seems fine. It needs a fingerprint of the original taken
// BEFORE the rebuild, over the things the corpus is actually used for:
//
//   * PROVENANCE — Ghidra records the executable's own MD5/SHA256 and path at import time, plus
//     the Ghidra version that created the program. That answers "was this built from the binary
//     I still have?" without trusting a filename.
//   * ANALYSIS STATE — function / instruction / defined-data / datatype counts. This corpus is a
//     MIX: `-noanalysis` raw imports (which the sweep needs) sit beside fully analysed projects
//     (which AOB authoring needs), and a size in GB does not tell them apart. A raw import has
//     essentially no functions; an analysed one has tens of thousands.
//   * SYMBOLS — count plus an order-independent digest of every (name @ address). `-noanalysis`
//     SKIPS PDB application, so whether a project carries PDB symbols is a per-project fact that
//     has to be measured, never assumed from `needs_pdb` in the manifest (that flag records what
//     the ROW needs, not what the PROJECT got).
//
// The digest is what makes a rebuild checkable: two symbol tables agree or they do not, and a
// count alone would pass a rebuild that had the right number of symbols at the wrong addresses.
//
// env GI_OUT = output dir (default ".")
// env GI_TAG = label for the project (defaults to the program name)
//
// OUTPUT: identity_<tag>__<prog>@<imagebase>.tsv  — key/value lines, plus a `symbols.sha256`.
// Optional: GI_SYMDUMP=1 also writes symbols_<stem>.tsv (every name+address), for diffing a
// mismatch down to the individual symbol.
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;
import java.io.*;
import java.security.MessageDigest;
import java.util.*;

public class dump_identity extends GhidraScript {

    static String sanitize(String n) { return n.replaceAll("[^A-Za-z0-9._-]", "_"); }

    static String hex(byte[] d) {
        StringBuilder sb = new StringBuilder(d.length * 2);
        for (byte b : d) sb.append(String.format("%02x", b & 0xFF));
        return sb.toString();
    }

    public void run() throws Exception {
        String outDir = System.getenv("GI_OUT");
        if (outDir == null || outDir.isEmpty()) outDir = ".";
        String tag = System.getenv("GI_TAG");
        String prog = currentProgram.getName();
        if (tag == null || tag.isEmpty()) tag = prog;
        boolean symDump = "1".equals(System.getenv("GI_SYMDUMP"));

        String stem = sanitize(tag) + "__" + sanitize(prog) + "@"
                    + sanitize(currentProgram.getImageBase().toString());
        PrintWriter w = new PrintWriter(new BufferedWriter(
                new FileWriter(outDir + "/identity_" + stem + ".tsv"), 1 << 16));

        w.println("program\t" + prog);
        w.println("tag\t" + tag);
        w.println("image_base\t" + currentProgram.getImageBase());
        w.println("language\t" + currentProgram.getLanguageID());
        w.println("language_version\t" + currentProgram.getLanguage().getVersion());
        w.println("compiler_spec\t" + currentProgram.getCompilerSpec().getCompilerSpecID());

        // Ghidra's own record of what was imported and by which version. Sorted so the file is
        // stable and diffable; the map's iteration order is not part of the identity.
        //
        // `SourceFileNNN` is EXCLUDED by default: a PDB-bearing program contributes hundreds of
        // build-tree paths, which is 85% of this file's bytes, is ignored by every comparison, and
        // is re-derivable from the archived PDB. Set GI_SOURCEFILES=1 to keep them. That default
        // matters because these files are committed — they are the fingerprint that outlives the
        // `.rep`, and without one there is nothing to verify a future rebuild against.
        boolean srcFiles = "1".equals(System.getenv("GI_SOURCEFILES"));
        Map<String, String> md = currentProgram.getMetadata();
        for (String k : new TreeSet<>(md.keySet())) {
            if (!srcFiles && k.startsWith("SourceFile")) continue;
            // Never emit the LOCAL PATH of the analysed binary. Same argument as SourceFile above,
            // plus one more: these files are committed to a PUBLIC repo, and "Executable Location"
            // / "FSRL" disclose the machine's drive layout and game library. They are also dead
            // weight -- reimport_verify.py lists both by name in its IGNORE set, with the comment
            // "carries the source path ... and the corpus's recorded paths are stale anyway".
            // The identity that matters (Executable MD5/SHA256 and the symbol digest) is untouched.
            // 148 such lines were stripped from the 74 committed exports on 2026-09-06.
            if (k.equals("Executable Location") || k.equals("FSRL")) continue;
            w.println("meta." + k.replace('\t', ' ') + "\t"
                      + String.valueOf(md.get(k)).replace('\t', ' ').replace('\n', ' '));
        }

        Listing lst = currentProgram.getListing();
        long instrs = lst.getNumInstructions();
        long defData = lst.getNumDefinedData();
        int funcs = currentProgram.getFunctionManager().getFunctionCount();
        w.println("functions\t" + funcs);
        w.println("instructions\t" + instrs);
        w.println("defined_data\t" + defData);
        w.println("datatypes\t" + currentProgram.getDataTypeManager().getDataTypeCount(true));
        // THE DISCRIMINATOR IS INSTRUCTIONS, NOT FUNCTIONS. Measured on UE4.10-GameDev: 195,451
        // functions, 840,853 defined data, 569,069 symbols — and ZERO instructions, with
        // `Analyzed=false`. Applying a PDB creates functions and data without disassembling
        // anything, so a function count says a PDB was applied, not that the program was
        // analysed. A .rep's SIZE says the same thing and is likewise not an analysis signal.
        w.println("has_pdb_applied\t" + (funcs > 1000 ? "yes" : "no"));
        w.println("analysis_state\t" + (instrs > 0 ? "DISASSEMBLED" : "NOT-DISASSEMBLED"));

        // Symbol digest. SORTED before hashing so it is independent of iteration order — a
        // rebuilt program can enumerate in a different order and still be the same table.
        SymbolTable st = currentProgram.getSymbolTable();
        List<String> lines = new ArrayList<>();
        SymbolIterator it = st.getAllSymbols(true);
        long n = 0, nGlobal = 0;
        while (it.hasNext()) {
            Symbol s = it.next();
            lines.add(s.getAddress() + "\t" + s.getName() + "\t" + s.getSymbolType()
                      + "\t" + (s.isGlobal() ? "G" : "L") + "\t" + s.getSource());
            n++;
            if (s.isGlobal()) nGlobal++;
        }
        Collections.sort(lines);
        MessageDigest sha = MessageDigest.getInstance("SHA-256");
        for (String l : lines) sha.update((l + "\n").getBytes("UTF-8"));
        w.println("symbols\t" + n);
        w.println("symbols_global\t" + nGlobal);
        w.println("symbols_sha256\t" + hex(sha.digest()));
        // Where symbols CAME from. A PDB-applied program is dominated by IMPORTED symbols; a raw
        // import has only what the PE export table and the loader produced (ANALYSIS/DEFAULT).
        Map<String, Integer> bySource = new TreeMap<>();
        for (String l : lines) {
            String[] f = l.split("\t");
            bySource.merge(f[4], 1, Integer::sum);
        }
        for (Map.Entry<String, Integer> e : bySource.entrySet())
            w.println("symbols_source." + e.getKey() + "\t" + e.getValue());
        w.close();

        if (symDump) {
            PrintWriter sw = new PrintWriter(new BufferedWriter(
                    new FileWriter(outDir + "/symbols_" + stem + ".tsv"), 1 << 20));
            sw.println("address\tname\ttype\tscope\tsource");
            for (String l : lines) sw.println(l);
            sw.close();
        }
        println("dump_identity DONE -> " + outDir + "/identity_" + stem + ".tsv  ("
                + funcs + " functions, " + n + " symbols)");
    }
}
