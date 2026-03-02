# run_all_aob_export.py
#
# Batch runner: iterates all Ghidra projects and runs ExtractAOBContext.ghidra.py
# on each one via pyghidra.
#
# Setup:
#   1. Activate Ghidra venv:
#      & "$env:USERPROFILE\AppData\Roaming\ghidra\ghidra_12.0.1_PUBLIC\venv\Scripts\Activate.ps1"
#   2. cd <this directory>
#   3. python run_all_aob_export.py
#
# Or run a single project:
#   python run_all_aob_export.py "ES2"

import os
import sys
import traceback

# ---------------------------------------------------------------------------
# Project configuration — edit these for your environment
# ---------------------------------------------------------------------------
SCRIPT_DIR  = os.path.dirname(os.path.abspath(__file__))
SCRIPT_PATH = os.path.join(SCRIPT_DIR, "ExtractAOBContext.ghidra.py")

# Root directory containing Ghidra .rep project folders
PROJECT_DIR = r"D:\Tools\GHIDRA_Projs"

# Map: project_name -> list of binary filenames to process.
#   None       = auto-detect ALL files in project (processes each one)
#   [name]     = process only that specific binary
#   [a, b, ..] = process these specific binaries
PROJECTS = {
    "ES":                     None,  # auto-detect all
    "ES2":                    None,
    "Everspace 2 UE 5.3":    None,
    "Everspace 2 UE4.27":    None,
    "FFRemake":               None,
    "Octopath Traveller":     None,
    "Satfifactory 4.22.3":   None,
    "Satfifactory 4.25.3":   None,
    "Satfifactory 4.26.2":   None,
    "Satfifactory 5.21.0":   None,
    "Satisfactory":           None,
}

# Output directory for JSON files
OUTPUT_DIR = os.path.join(PROJECT_DIR, "aob_export")


def run_one_binary(java_project, project_name, domain_file):
    """Run the extraction script on a single binary within a project."""
    from ghidra.app.script import GhidraState

    binary_name = domain_file.getName()
    display_name = binary_name.replace(".exe", "").replace(".dll", "")

    # For multi-binary projects, use "project_binary" as the output name
    # so each binary gets its own JSON file
    output_name = "{}_{}".format(project_name, display_name)

    print("\n  --- {} ---".format(binary_name))
    try:
        program = domain_file.getDomainObject(java_project, True, False, None)
    except Exception as e:
        print("  [!] Failed to open: {}".format(e))
        return False

    print("  [+] Loaded: {} ({} bytes)".format(
        program.getName(), program.getMemory().getSize()))

    try:
        # Build script environment
        script_globals = {
            'currentProgram': program,
            'PROJECT_NAME': output_name,
            '_SCRIPT_DIR': SCRIPT_DIR,
            '_OUTPUT_DIR': OUTPUT_DIR,
            'monitor': None,
            'state': GhidraState(None, java_project, program, None, None, None),
        }

        # Inject Ghidra API functions
        import pyghidra.api
        for attr in dir(pyghidra.api):
            if not attr.startswith("__"):
                script_globals[attr] = getattr(pyghidra.api, attr)

        # Read and execute the extraction script
        with open(SCRIPT_PATH, 'r', encoding='utf-8') as sf:
            script_content = sf.read()
            exec(script_content, script_globals)

        return True
    except Exception as e:
        print("  [!] Error: {}".format(e))
        traceback.print_exc()
        return False
    finally:
        program.release(java_project)


def _run_single_binary(java_project, project_name, domain_file):
    """Run the extraction script on a single binary, using project_name as output name."""
    from ghidra.app.script import GhidraState

    binary_name = domain_file.getName()
    print("\n  --- {} ---".format(binary_name))
    try:
        program = domain_file.getDomainObject(java_project, True, False, None)
    except Exception as e:
        print("  [!] Failed to open: {}".format(e))
        return False

    print("  [+] Loaded: {} ({} bytes)".format(
        program.getName(), program.getMemory().getSize()))

    try:
        script_globals = {
            'currentProgram': program,
            'PROJECT_NAME': project_name,
            '_SCRIPT_DIR': SCRIPT_DIR,
            '_OUTPUT_DIR': OUTPUT_DIR,
            'monitor': None,
            'state': GhidraState(None, java_project, program, None, None, None),
        }
        import pyghidra.api
        for attr in dir(pyghidra.api):
            if not attr.startswith("__"):
                script_globals[attr] = getattr(pyghidra.api, attr)
        with open(SCRIPT_PATH, 'r', encoding='utf-8') as sf:
            exec(sf.read(), script_globals)
        return True
    except Exception as e:
        print("  [!] Error: {}".format(e))
        traceback.print_exc()
        return False
    finally:
        program.release(java_project)


def run_project(project_name, binary_names=None):
    """Open a Ghidra project and run the extraction script on its binaries."""
    import pyghidra
    from pyghidra.api import open_project

    print("\n" + "=" * 70)
    print("[*] Opening project: {}".format(project_name))
    print("=" * 70)

    try:
        with open_project(PROJECT_DIR, project_name) as project:
            java_project = project._project if hasattr(project, '_project') else project
            project_data = java_project.getProjectData()
            root = project_data.getRootFolder()

            if binary_names:
                # Process specific binaries
                files_to_process = []
                for bname in binary_names:
                    f = project_data.getFile("/{}".format(bname))
                    if f:
                        files_to_process.append(f)
                    else:
                        print("[!] Binary not found: {}".format(bname))
            else:
                # Auto-detect: process ALL files in root folder
                files_to_process = list(root.getFiles())

            if not files_to_process:
                print("[!] No files in project: {}".format(project_name))
                return False

            print("[*] Found {} file(s) in project".format(len(files_to_process)))
            for f in files_to_process:
                print("    {}".format(f.getName()))

            # Single binary -> use project name directly (no suffix)
            # Multiple binaries -> each gets "project_binary" name
            all_ok = True
            for domain_file in files_to_process:
                if len(files_to_process) == 1:
                    ok = _run_single_binary(java_project, project_name, domain_file)
                else:
                    ok = run_one_binary(java_project, project_name, domain_file)
                if not ok:
                    all_ok = False

            return all_ok

    except Exception as e:
        print("[!] Error processing {}: {}".format(project_name, str(e)))
        traceback.print_exc()
        return False


def main():
    import pyghidra
    pyghidra.start()

    # Filter to specific project if given as argument
    if len(sys.argv) > 1:
        target = sys.argv[1]
        if target in PROJECTS:
            projects_to_run = {target: PROJECTS[target]}
        else:
            print("[!] Unknown project: {}".format(target))
            print("[*] Available: {}".format(", ".join(PROJECTS.keys())))
            sys.exit(1)
    else:
        projects_to_run = PROJECTS

    results = {}
    for name, binaries in projects_to_run.items():
        ok = run_project(name, binaries)
        results[name] = "OK" if ok else "FAILED"

    # Summary
    print("\n\n" + "=" * 70)
    print("BATCH SUMMARY")
    print("=" * 70)
    for name, status in results.items():
        icon = "[+]" if status == "OK" else "[!]"
        print("  {} {} - {}".format(icon, name, status))

    print("\nOutput directory: {}".format(OUTPUT_DIR))


if __name__ == "__main__":
    main()
