r"""Fail CI if a machine-local user path leaked into a tracked file.

    py tools/check_no_local_paths.py
    py tools/check_no_local_paths.py --list      # show every hit, do not exit 1

Why this exists: the repo is written on more than one PC and read by people who
are not the maintainer, so a literal home directory in a doc is both wrong for
every other reader and an unnecessary disclosure of the account name. It leaks
easily -- a path pasted out of a log, a scheduled-task location, a tool's usage
example -- and nothing else in CI notices.

Use the ENVIRONMENT-VARIABLE form instead, which is correct on every machine:

    C:\Users\<name>\AppData\Local\...   ->  %LOCALAPPDATA%\...
    C:\Users\<name>\AppData\Roaming\... ->  %APPDATA%\...
    C:\Users\<name>\...                 ->  %USERPROFILE%\...

Placeholders are allowed (`C:\Users\<you>`), because they cannot be mistaken for
a real account, as are the shared system accounts. Only concrete names fail.
"""
import re
import subprocess
import sys

# A Windows user directory followed by a CONCRETE name.
#
# The separator class is built from chr(92) rather than written as a literal
# backslash: this file is generated/edited through tooling that has been seen to
# collapse "\\" to "\", which silently turns [\\/] into "/ only" -- a regex that
# then matches nothing and reports a clean tree forever. Assembling it here means
# the class cannot be damaged in transit.
_SEP = "[" + chr(92) + chr(92) + "/]"
PATTERN = re.compile(r"[A-Za-z]:" + _SEP + "Users" + _SEP +
                     r"(?!<|\{|%)([A-Za-z0-9._-]+)", re.IGNORECASE)

# Generic stand-ins that carry no account name.
#
# "someone" is here for LogCompressionTests.Batch_SplitsByCommandLineLength_*,
# whose fixture path is a deliberate fiction AND whose assertion depends on that
# path's LENGTH ("a 500-char budget must split 40 x ~110-char paths"). Renaming it
# to a different-length placeholder would quietly change the arithmetic the test
# is measuring, so the name is load-bearing and is allowed rather than rewritten.
ALLOWED = {"public", "default", "all", "username", "user", "you", "youruser",
           "name", "yourname", "someone"}

# A second, NARROWER pattern that ALLOWED does not get to excuse.
#
# ⚠ Found 2026-09-06 in docs/archive/todo-completed-build-937.md, which had carried
# `…/C:/Users/user/.claude/projects/<project>/memory/<file>.md` since it was archived. PATTERN
# matched it and then waved it through, because `user` is a legitimate generic stand-in and the
# path genuinely reads like one.
#
# The account name is the wrong thing to key on. What makes that path certainly-real and certainly
# wrong is where it CONTINUES: `.claude/projects/` is a per-session Claude Code directory, and a
# drive-absolute path into one cannot be meaningful in a tracked file whatever the account is
# called. So this rule ignores ALLOWED entirely.
#
# ⚠ It must NOT fire on the correct form. docs/working-lessons.md references the same folder as
# `%USERPROFILE%\.claude\projects\…` — no drive letter, so this pattern does not match, which is
# exactly the distinction being drawn.
SESSION_DIR_PATTERN = re.compile(r"[A-Za-z]:" + _SEP + r"(?:[^\s:*?\"<>|]{0,120}" + _SEP +
                                 r")*?\.claude" + _SEP + "projects", re.IGNORECASE)

SKIP = ("tools/check_no_local_paths.py",)


def tracked_files():
    out = subprocess.run(["git", "ls-files"], capture_output=True, text=True,
                         errors="replace").stdout
    return [f for f in out.splitlines() if f and not f.startswith(SKIP)]


def main(argv):
    list_only = "--list" in argv
    files = tracked_files()
    hits = []
    for path in files:
        try:
            with open(path, "r", encoding="utf-8", errors="ignore") as fh:
                for n, line in enumerate(fh, 1):
                    for m in PATTERN.finditer(line):
                        if m.group(1).lower() in ALLOWED:
                            continue
                        hits.append((path, n, m.group(0), line.strip()[:110]))
                    # ALLOWED deliberately does NOT apply here -- see SESSION_DIR_PATTERN.
                    for m in SESSION_DIR_PATTERN.finditer(line):
                        hits.append((path, n, m.group(0), line.strip()[:110]))
        except (OSError, UnicodeError):
            continue

    if not hits:
        print(f"CHECK OK: no machine-local user path in {len(files)} tracked files.")
        return 0

    print(f"FOUND {len(hits)} machine-local path(s) in tracked files:")
    for path, n, frag, line in hits:
        print(f"  {path}:{n}: {frag}")
        print(f"      {line}")
    print()
    print("Replace with %LOCALAPPDATA% / %APPDATA% / %USERPROFILE%, or a <placeholder>.")
    return 0 if list_only else 1


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main(sys.argv[1:]))
