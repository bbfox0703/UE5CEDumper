# Local LLM helper — adopting it in another repo

[`ollama_local.py`](ollama_local.py) lets a Claude Code session hand bulk text work (large logs,
dumps, long docs, translation drafts) to a local Ollama model, and keeps that model's VRAM away from
commercial games. **When** a session should use it is the skill's job:
[`.claude/skills/local-llm/SKILL.md`](../../.claude/skills/local-llm/SKILL.md). This page is **how a
machine installs it and how any repo joins or leaves**.

This repo is the helper's **source**. Every other repo only *joins*: it never copies the helper.

## What lives where

| Where | What | In a repo? |
|---|---|---|
| a joined repo: `.claude/skills/local-llm/SKILL.md` | the skill; it calls the machine copy through `$LOCALAPPDATA` | yes: safe to commit, carries no machine fact |
| `%LOCALAPPDATA%\claude-local-llm\` | the installed helper, `SKILL.md`, `config.json` (model tag, URL, window, exempt fixtures, VRAM floor, installed revision), `repos.json` (joined repos), leases, the GPU reservation | **never** |
| `~/.claude/settings.json` (user level) | ONE PreToolUse hook, pointing at the installed helper | **never** |

⛔ **Nothing personal or machine-specific ever enters a repo.** Model tags, URLs, local paths,
account or computer names, VRAM floors and tokens live only in the machine dir and the user-level
settings. The skill a repo receives carries no machine fact; two machines on the same helper revision
hand out the same bytes (on different revisions, commit the skill from the machine with the newer
install, or `repos` on the other one will call it outdated).

## Install once per machine

Needs Windows, the `py` launcher, a running Ollama with the model already pulled, and Claude Code.
From a checkout of this repo:

```bash
py tools/llm/ollama_local.py install --model <tag>
```

Options: `--url http://host:port` if Ollama is not on the default local port · `--num-ctx N` (default
32768) · `--exempt PREFIX` for a test-fixture exe that may share the GPU with the model (repeatable;
the DumperTest projects are exempt by default) · `--min-free-vram-mb N`, a floor under the computed
need: a cold load is refused with less free VRAM than this on the NVIDIA GPU (0 clears it) ·
`--force` to install from a checkout OLDER than the installed copy (otherwise refused). The hook
applies from the next session.

**Changing a setting later touches no repo.** Run the machine copy with only what changes; it keeps the
rest of the machine config:

```bash
py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" install --min-free-vram-mb 16384
```

`status` shows what the free-VRAM check uses (`vram=`): the GPU, its free MiB, and the need as
weights + KV cache (computed from the model's own GGUF metadata, so it holds for any model) +
compute overhead + a 512 MiB buffer, floored at `--min-free-vram-mb`.

Re-running `install` here **updates** the machine copy for every joined repo. `uninstall` removes the
machine copy, its config and the hook; `uninstall --leave-all` also takes the skill back out of every
joined repo.

## Join and leave — any repo, one command each

Run in the repo, with the machine copy (so the repo needs nothing of its own):

```bash
py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" join
```
```bash
py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" leave
```
```bash
py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" repos
```

`join` writes `.claude/skills/local-llm/SKILL.md` and records the repo in the machine's list; `leave`
removes that file (and the directories it emptied) and nothing else; `repos` shows each joined repo
as `current`, `outdated` (the helper was updated since: `join` there again), `missing` or
`unreadable`, and the source repo as `source`. `--repo PATH` acts on another repo; outside a git repo
it is required. In PowerShell write `$env:LOCALAPPDATA`.

`join` refuses the source repo (its skill is source, published by `install`), a directory that does
not exist, the home directory, and a skill file at that path that is not this helper's (`--force`
replaces it). `leave` refuses the source repo and leaves a foreign skill file untouched.

## Why several repos cannot break each other

- **One helper, one config, one hook** for the whole machine. A joined repo holds no copy of the
  helper, so there is no version to drift and no second hook to fight over: `join` / `leave` touch the
  skill file only, and no repo can remove another's guard.
- **The guard is machine-wide.** The hook runs before every Bash / PowerShell call in every session,
  joined repo or not, and a GPU reservation or a request in flight is visible to all of them
  (`status`: `gpu_reserved`, `in_use_by`).
- **`status` reports the install's health:** `install=machine`, the installed `version`, `hook=ok`
  (or what to fix), and, run from this repo, `update=...` when the installed copy is behind.

## For a Claude session in another repo that has just read this page

1. `py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" status` in Git Bash, or
   `py "$env:LOCALAPPDATA/claude-local-llm/ollama_local.py" status` in PowerShell. A "can't open file"
   error naming `%LOCALAPPDATA%\claude-local-llm\...` means the machine has no install: installing is
   the **user's** call, from a checkout of this repo, and needs a model tag the user names. (If the path
   in the error starts at a drive root, `X:\claude-local-llm\...`, the variable did not expand: use the
   other shell's spelling.) Never pull a model or start a server yourself.
2. With `state=ready` (or any state: joining needs only the install), run `join` in your repo. The
   skill takes it from there; `leave` undoes it.
