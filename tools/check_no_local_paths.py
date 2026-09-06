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
import os
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

# ─────────────────────────────────────────────────────────────────────────────
# THIS MACHINE's identifiers — derived at run time, stored nowhere.
#
# ⭐ THE PROBLEM THIS SOLVES, and it is a real chicken-and-egg: to flag the account name "Xyz" a
# gate must know "Xyz", and writing it into a tracked file publishes exactly what is being
# protected. The patterns above sidestep it by matching a SHAPE (`C:\Users\<something>` that is not
# a known placeholder) and never needing the value. But a bare hostname has no shape — it is just a
# word — so shape matching cannot see it, and on 2026-09-06 the machine name was found in 13
# tracked files, six of them in docs/archive/, with two rigs HARD-CODING it (which also broke them
# on the second PC).
#
# Resolution: read the identifiers from the ENVIRONMENT of whichever machine is running the gate —
# which is precisely the machine that could leak them. Nothing is committed and nothing is hashed.
#
# ⛔ HASHING WOULD BE SECURITY THEATRE, so it was rejected: usernames and hostnames come from a tiny
# search space, and a stored sha256 of one is brute-forced in seconds. A CI secret was rejected too
# — secrets are unavailable to pull requests from forks (this repo has 10), and the leak is authored
# LOCALLY, long before CI ever runs.
#
# ⚠ ITS LIMIT, STATED RATHER THAN HIDDEN: it only knows the CURRENT machine. If PC A commits PC B's
# name, A's gate cannot see it. This COMPLEMENTS the shape patterns; those remain the load-bearing
# half because they need no knowledge at all.
MIN_IDENTIFIER_LEN = 4


def runtime_identifiers():
    """{value: where it came from} for this machine. Nothing here is ever persisted."""
    found = {}
    for var in ("USERNAME", "COMPUTERNAME", "USERDOMAIN"):
        v = (os.environ.get(var) or "").strip()
        if len(v) >= MIN_IDENTIFIER_LEN and v.lower() not in ALLOWED:
            found.setdefault(v, var)
    home = (os.environ.get("USERPROFILE") or os.environ.get("HOME") or "").rstrip("\\/")
    leaf = os.path.basename(home)
    if len(leaf) >= MIN_IDENTIFIER_LEN and leaf.lower() not in ALLOWED:
        found.setdefault(leaf, "USERPROFILE")
    return found


def mask_account(m):
    """`C:\\Users\\Andyc` -> `C:\\Users\\A****`. The path stays readable; the identity does not."""
    acct = m.group(1)
    return m.group(0).replace(acct, redact(acct))


def mask_account_line(line):
    """Same masking applied to the quoted source line, so the fragment and the line agree."""
    out = line.strip()
    for m in PATTERN.finditer(out):
        if m.group(1).lower() in ALLOWED:
            continue
        out = out.replace(m.group(1), redact(m.group(1)))
    return out[:110]


def redact(value):
    """⚠ THE GATE'S OWN OUTPUT MUST NOT PRINT THE NAME. CI logs on a public repo are public, so a
    failure that echoed the identifier would publish the very string it exists to keep out."""
    return value[0] + "*" * (len(value) - 1)





def unpushed_commit_messages():
    """(sha, message) for every local commit not yet on any remote branch.

    ⭐ WHY MESSAGES, AND WHY ONLY THE UNPUSHED ONES. A file-level gate reads `git ls-files` and can
    never see `%B`. That gap is not theoretical: on 2026-09-06 the account name was found in the
    message of 4b4d6775 -- the commit that REMOVED the same string from a file restated it in its
    own message, so the file was cleaned and the leak stayed.

    Only unpushed commits are checked, because those are the only ones still fixable: an unpushed
    commit can be amended or rebased, a pushed one cannot be recalled. That is also exactly the
    maintainer's own framing -- a commit always precedes a push, so the build is the last cheap
    moment.
    """
    try:
        rng = subprocess.run(["git", "log", "--branches", "--not", "--remotes",
                              "--format=%H%x01%B%x02"],
                             capture_output=True, text=True, encoding="utf-8",
                             errors="replace")
    except OSError:
        return []
    out = []
    for chunk in (rng.stdout or "").split("\x02"):
        chunk = chunk.strip()
        if not chunk or "\x01" not in chunk:
            continue
        sha, msg = chunk.split("\x01", 1)
        out.append((sha.strip()[:8], msg))
    return out


def tracked_files():
    out = subprocess.run(["git", "ls-files"], capture_output=True, text=True,
                         errors="replace").stdout
    return [f for f in out.splitlines() if f and not f.startswith(SKIP)]


def main(argv):
    list_only = "--list" in argv
    files = tracked_files()
    # Built once. Word-boundary so a name that is a substring of an ordinary word does not fire.
    idents = runtime_identifiers()
    ident_patterns = {
        v: re.compile(r"(?<![A-Za-z0-9_])" + re.escape(v) + r"(?![A-Za-z0-9_])", re.IGNORECASE)
        for v in idents
    }

    hits = []
    for path in files:
        try:
            with open(path, "r", encoding="utf-8", errors="ignore") as fh:
                for n, line in enumerate(fh, 1):
                    for m in PATTERN.finditer(line):
                        if m.group(1).lower() in ALLOWED:
                            continue
                        # ⚠ REDACT THE ACCOUNT SEGMENT. This branch used to print m.group(0) and
                        # the raw line, i.e. it echoed the account name it had just caught -- which
                        # is how a caught leak gets laundered into a commit message or a CI log.
                        hits.append((path, n, mask_account(m), mask_account_line(line)))
                    # ALLOWED deliberately does NOT apply here -- see SESSION_DIR_PATTERN.
                    for m in SESSION_DIR_PATTERN.finditer(line):
                        hits.append((path, n, m.group(0), line.strip()[:110]))
                    for value, rx in ident_patterns.items():
                        if rx.search(line):
                            # Redact in BOTH the fragment and the quoted line.
                            hits.append((path, n, redact(value),
                                         rx.sub(redact(value), line.strip())[:110]))
        except (OSError, UnicodeError):
            continue

    # Commit MESSAGES of unpushed commits -- see unpushed_commit_messages().
    msg_hits = []
    for sha, msg in unpushed_commit_messages():
        for line in msg.splitlines():
            for m in PATTERN.finditer(line):
                if m.group(1).lower() in ALLOWED:
                    continue
                msg_hits.append((sha, mask_account(m), mask_account_line(line)))
            for m in SESSION_DIR_PATTERN.finditer(line):
                msg_hits.append((sha, m.group(0), line.strip()[:110]))
            for value, rx in ident_patterns.items():
                if rx.search(line):
                    msg_hits.append((sha, redact(value),
                                     rx.sub(redact(value), line.strip())[:110]))

    if msg_hits:
        print(f"FOUND {len(msg_hits)} leak(s) in UNPUSHED commit MESSAGE(S):")
        for sha, frag, line in msg_hits:
            print(f"  {sha}: {frag}")
            print(f"      {line}")
        print()
        print("These commits are NOT pushed, so they are still fixable:")
        print("  git commit --amend        (the most recent one)")
        print("  git rebase -i <base>      (an older one; reword it)")
        print("A pushed commit message cannot be recalled, which is why only unpushed ones fail here.")
        return 1

    if not hits:
        print(f"CHECK OK: no machine-local user path in {len(files)} tracked files, "
              f"nor in any unpushed commit message.")
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
