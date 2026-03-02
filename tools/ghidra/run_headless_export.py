# run_headless_export.py
#
# Headless runner: opens a single Ghidra project and executes a script
# (ExportUESymbols.ghidra.py by default) on a specific binary via pyghidra.
#
# Setup:
#   1. Activate Ghidra venv:
#      & "$env:USERPROFILE\AppData\Roaming\ghidra\ghidra_12.0.1_PUBLIC\venv\Scripts\Activate.ps1"
#   2. cd <this directory>
#   3. python run_headless_export.py
#
# Edit the configuration section below for your environment.

import os
import traceback

import pyghidra

pyghidra.start()

from pyghidra.api import open_project
from ghidra.app.script import GhidraState

# ---------------------------------------------------------------------------
# Configuration — edit these for your environment
# ---------------------------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

PROJECT_DIR = r"D:\Tools\GHIDRA_Projs"
PROJECT_NAME = "My Game"
BINARY_NAME = "My_Game-Win64-Shipping.exe"
SCRIPT_PATH = os.path.join(SCRIPT_DIR, "ExportUESymbols.ghidra.py")
OUTPUT_DIR = os.path.join(PROJECT_DIR, "aob_export")

# ---------------------------------------------------------------------------

print("[*] Opening project: {}...".format(PROJECT_NAME))

try:
    with open_project(PROJECT_DIR, PROJECT_NAME) as project:
        # Handle pyghidra wrapper
        java_project = project._project if hasattr(project, '_project') else project

        # Get program object
        project_data = java_project.getProjectData()
        domain_file = project_data.getFile("/{}".format(BINARY_NAME))

        if domain_file is None:
            print("[!] File not found: {}".format(BINARY_NAME))
        else:
            program = domain_file.getDomainObject(java_project, True, False, None)
            print("[+] Loaded: {}".format(BINARY_NAME))

            # Build a simulated Ghidra script environment
            script_globals = {
                'currentProgram': program,
                '_SCRIPT_DIR': SCRIPT_DIR,
                '_OUTPUT_DIR': OUTPUT_DIR,
                'monitor': None,
                'state': GhidraState(None, java_project, program, None, None, None),
                'askString': lambda title, message: "",  # no-op for headless
            }

            # Inject Ghidra API functions
            import pyghidra.api
            for attr in dir(pyghidra.api):
                if not attr.startswith("__"):
                    script_globals[attr] = getattr(pyghidra.api, attr)

            print("[*] Executing script: {}...".format(os.path.basename(SCRIPT_PATH)))

            # Read and execute the script
            with open(SCRIPT_PATH, 'r', encoding='utf-8') as f:
                script_content = f.read()
                exec(script_content, script_globals)

            program.release(java_project)
            print("[+] Script execution complete!")

except Exception as e:
    print("[!] Error: {}".format(e))
    traceback.print_exc()

print("[+] Done.")
