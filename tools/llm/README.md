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
| `%LOCALAPPDATA%\claude-local-llm\` | the installed helper, `SKILL.md`, `config.json` (model tag, URL, window, exempt fixtures), `repos.json` (joined repos), leases, the GPU reservation | **never** |
| `~/.claude/settings.json` (user level) | ONE PreToolUse hook, pointing at the installed helper | **never** |

⛔ **Nothing personal or machine-specific ever enters a repo.** Model tags, URLs, local paths,
account or computer names, hardware and tokens live only in the machine dir and the user-level
settings. The skill a repo receives is byte-identical on every machine.

## Install once per machine

Needs Windows, the `py` launcher, a running Ollama with the model already pulled, and Claude Code.
From a checkout of this repo:

```bash
py tools/llm/ollama_local.py install --model <tag>
```

Options: `--url http://host:port` if Ollama is not on the default local port · `--num-ctx N` (default
32768) · `--exempt PREFIX` for a test-fixture exe that may share the GPU with the model (repeatable;
the DumperTest projects are exempt by default). The hook applies from the next session.

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
as `current`, `outdated` (the helper was updated since: `join` there again) or `missing`. `--repo PATH`
acts on another repo. In PowerShell write `$env:LOCALAPPDATA`. The source repo cannot `leave`: its
skill is part of its source.

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

1. `py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" status`. A "can't open file" error means the
   machine has no install: installing is the **user's** call, from a checkout of this repo, and needs
   a model tag the user names. Never pull a model or start a server yourself.
2. With `state=ready` (or any state: joining needs only the install), run `join` in your repo. The
   skill takes it from there; `leave` undoes it.
