---
name: ship
description: This repo's push, pull request and merge routine for the dev branch. Use when the user asks to push, to open a PR, to merge, or says "push and merge" / "ship it". Covers the gates, the AOT publish and build-number bump when the UI or DLL changed, the dev-to-main PR, and what "merged" leaves behind.
---

# Ship: push `dev`, open the PR, merge to `main`

Do only the part that was asked for. "Push" is steps 1 to 4. A PR and a merge need the user to ask
for them: opening a PR or merging on your own initiative is not part of this routine.

## 1. Know the state before touching it

```bash
git status --short
git fetch origin
git rev-list --left-right --count origin/dev...dev
git rev-list --count --no-merges dev..origin/main
```

- The second number of the third command is what is unpushed. Say what is pushed from this, never
  from memory: the other PC and peer sessions push to `dev` too.
- The last command must print `0`. `main` holds only PR merge commits beyond `dev`; a real commit
  there means someone committed to `main` directly, so stop and tell the user.
- A file in `git status` that this task did not touch belongs to someone else's work. Do not stage
  it; say it is there.

## 2. Gates

```bash
py tools/check_all.py
```

Every gate must pass; read the count from its own `N gate(s) run` line. When the commit adds or
removes a caller, a field, a pipe key or an enum value, also run
`py tools/verify/comment_impact.py --staged` and fix the starred comments.

## 3. If the UI or the DLL changed: publish, then bump

`CLAUDE.md` § Build & Deploy is the rule. In short: run `build.ps1 -Mode Publish`, confirm
`dist\UE5DumpUI.exe` is the AOT-trimmed size (compare with the last dev-log entry, not the ~107 MB
non-trimmed one), and let it bump `build_number.txt`. The bump is its own commit:

```
build <N>: bump the build number (<what this build carries>)
```

A released build gets a dev-log entry: user-facing, newest first, never editing an older entry.
Docs-only and tools-only changes skip this step and are logged as "(no build change)".

## 4. Commit and push

- One row or one item per commit; a fix that has a test goes red commit, then green commit.
- End the message with the `Co-Authored-By:` line this session was given. Never `--no-verify`.
- `git push origin dev`, then re-run the `rev-list` line from step 1 and report `0 0`.

## 5. Pull request (only when asked)

```bash
gh pr create --base main --head dev --title "<title>" --body-file <file>
```

- Write the body to a file with the file-writing tool; do not pass it through a heredoc.
- Body: one line saying what kind of change it is, `## What changes` for a user, `## Verification`
  with the numbers actually measured, and the Claude Code line last.
- Nothing machine-local in it: no home paths, no network addresses.

## 6. Merge (only when asked)

```bash
gh pr merge <N> --merge
```

- A merge commit, never squash or rebase, and **never `--admin`**.
- If the PR is blocked on a pending check, do not poll it. Turn on auto-merge with the merge method
  (the user asked for the merge) and say plainly that the merge has not happened yet. If the check
  fails the PR stays open: nothing wakes the session, so tell the user that too.

## 7. After the merge

```bash
git fetch origin main:main
```

`dev` is not fast-forwarded to `main`. After a merge the two branches have the same files and `main`
is one merge commit ahead; that is this repo's normal state, not a divergence to repair.

## A release is a separate request

Merging is not releasing. When the user asks for a release: tag `v<build>` on `main`'s merge commit,
write the notes, and **leave the release a draft** for the maintainer to publish. The steps and the
notes' format are `docs/working-lessons.md` §7.3.

## 8. Report

What was pushed, the PR link written as a full URL, whether it merged, and the commit ids read from
`git log`, not recalled.
