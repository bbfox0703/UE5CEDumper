# ExportUESymbols.ghidra.py
#
# Ghidra script: exports UE-related symbols (FName, UObject, GObjects, GNames,
# FNamePool) from the current program to a JSON file.
#
# Can be run standalone inside Ghidra or executed headlessly via
# run_headless_export.py / run_all_aob_export.py.

import json
import os
from pyghidra.api import *

# Get current program
if 'currentProgram' not in globals():
    currentProgram = getState().getCurrentProgram()

results = []
sm = currentProgram.getSymbolTable()

print("[*] Scanning symbols in {}...".format(currentProgram.getName()))

# Iterate all symbols and filter UE-related ones
count = 0
for sym in sm.getAllSymbols(True):
    name = sym.getName()
    if any(k in name for k in ["FName", "UObject", "GObjects", "GNames", "FNamePool"]):
        results.append({
            "name": name,
            "address": sym.getAddress().toString(),
            "type": sym.getSymbolType().toString()
        })
        count += 1

# Output path — configurable via _OUTPUT_DIR global (injected by batch runner)
if '_OUTPUT_DIR' in globals():
    output_dir = globals()['_OUTPUT_DIR']
else:
    output_dir = os.path.dirname(os.path.abspath(__file__))

output_path = os.path.join(output_dir, "ue_symbols.json")

print("[*] Scan complete, found {} matching symbol(s)".format(count))
print("[*] Writing to: {}".format(output_path))

try:
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(results, f, indent=2)

    if os.path.exists(output_path):
        print("[+] Success! File written.")
    else:
        print("[!] Error: file write failed.")
except Exception as e:
    print("[!] Write failed: {}".format(e))
