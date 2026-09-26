---
name: local-llm
description: Offload bulk text work to this machine's local Ollama model -- summarising or extracting from large on-disk logs/dumps/docs, zh-TW translation drafts, first-pass triage of many similar items -- and keep that model's VRAM away from commercial games. Use when a task would otherwise pull a large file into context, or before launching, injecting or running a rig against a commercial game (not a test fixture the machine exempts). Does nothing on a machine that has not installed it.
---

# Local LLM (Ollama): when to use it, and when the GPU belongs to the game

The helper is installed ONCE per machine and shared by every repo that joined it. Every command
below runs that machine copy; in PowerShell write `$env:LOCALAPPDATA` for `$LOCALAPPDATA`.
The mechanism and every measurement behind these rules live in the helper's own header; how a repo
joins or leaves is its `tools/llm/README.md` in the source repo (UE5CEDumper). This file says **when**.

## 0. Is it available here? Ask once per session, before anything else

```bash
py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" status
```

`state=ready` → usable. Anything else (`disabled`, `absent`, `no-model`), or a "can't open file"
error (this machine never installed it) → do the work yourself and do not mention the LLM again.
Never install anything, pull a model or start a server to make it ready.

## 1. ⛔ A commercial game owns the GPU

- While a commercial game runs, or is about to, **do not use the LLM**. `ask` / `warm` refuse
  (exit 3) and unload on their own.
- The hook runs in **every session on this machine, in every repo**. It unloads the model before a
  `steam -applaunch` / `*-Win64-Shipping.exe` launch, and whenever it finds a commercial game
  running beside a loaded model. Both also **reserve the GPU machine-wide**, so no other session
  can load the model back while the game starts.
- ⚠ **The gap the hook cannot close:** a script or rig that *launches* a commercial game inside one
  command, or a launch you are about to click in a launcher UI (Steam's Play button). Run this
  **before** it; it reserves the GPU and waits for any request in flight:

  ```bash
  py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" reserve --wait 120 --reason "<rig name>"
  ```

  The reservation lapses on its own (30 s after the game exits, or 180 s after a launch that never
  started a game). `release` clears it at once if you abandon the launch.
- ✅ **The machine's exempt test fixtures** may run beside the LLM (by default the DumperTest
  projects: DumperTest, DumperTest51, DumperTest58, ...; a machine adds its own at install time).
- A game whose exe does not look like a UE shipping build is invisible to the process check. If
  `ask` refuses for low VRAM, that is probably why: unload and tell the user. `status` shows the
  arithmetic (`vram=`): free VRAM on the NVIDIA GPU against what a cold load of this model needs.

### Other sessions share the same model

- Requests from every session **queue**: Ollama serves one at a time. A second session's request
  waits for the first, and nothing fails.
- `status` lists other sessions with a request in flight (`in_use_by`: pid, action, directory,
  age). It cannot see `ollama run`, other apps or LAN clients.
- An `unload` while another session's request runs is **deferred by Ollama until that request
  ends**. `unload` reports this as PENDING (exit 5) and names the holder. `--wait S` blocks until
  the model is free.

## 2. Is it worth it? The cost model

- The **cold load is the slow part** (~15-20 s); warm answers are fast. An idle model unloads itself
  after 10 minutes.
- Before a batch: start `warm` with `run_in_background`, keep working, then send the items.
- **One small question → do it yourself.** The load alone costs more than the answer saves.
- **The real win is context.** `--file` is read by the helper, so a 200 KB log never enters your
  context; you read back a short answer instead.

## 3. What to delegate, and what never to

**Good fits**
- Summarise / extract the gist of large on-disk text: logs, dumps, long docs. Anything over the
  window: add `--chunked`.
- zh-TW translation **drafts**, which you then review.
- First-pass triage of many similar items; spot-check a sample yourself.

**Never**
- Anything that becomes **evidence**: verification records, offsets or addresses, API facts (never
  invent an API), audit findings, derived counts.
- Code edits.
- Anything grep or Python answers deterministically: exact values, counts, max/min, "does X
  appear". Measured: asked for the maximum of ~800-2000 numbers per slice, it was **wrong on 1 of 7
  slices**, stated just as confidently as the six right ones.

## 4. Its output is a lead, not a finding

- Verify before acting on it or quoting it. When a reply to the user rests on LLM output, say so.
- It is told to answer only from the input it was given; "not in the input" is a valid answer.

## 5. Commands

```bash
py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" warm
```
```bash
py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" ask --prompt "What failed, and at which step?" --file path/to/big.log --chunked
```
```bash
py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" unload
```

- Also `--prompt-file F` for long instructions and `--think` for reasoning-heavy asks (slower).
- Exit codes: `0` ok · `2` unavailable here · `3` refused (a game holds the GPU, a reservation, or
  too little VRAM) · `4` input too large (the message carries the **server's** token count) ·
  `5` unload still pending behind another request.
- `--chunked` prints `### slice N -- <file> lines a-b` and one answer per slice. A slice that
  overflows is re-split automatically at the measured rate. **Numbers tokenize digit by digit**, so
  numeric logs fit far fewer lines per slice than prose.
- ⚠ The hook reads your **command text**. A literal `steam ... -applaunch <digits>` or
  `<Name>-Win64-Shipping.exe` in any Bash command, even inside an `echo`, counts as a launch: it
  unloads the model and **reserves the GPU for 180 s**. `taskkill /IM <exe>` is not counted.
  If this happens by accident, run `release`.

## 6. Joining, leaving, installing

- **This repo leaves:** `py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" leave` (run in the repo)
  removes this file and nothing else. **Another repo joins:** the same with `join`. `repos` lists
  every joined repo and whether its copy of this file is current.
- **Installing on a machine, or changing the model**, is the user's call, never yours. When the user
  says "I have Ollama, use model `<tag>`", run from a checkout of the source repo (UE5CEDumper):
  `py tools/llm/ollama_local.py install --model <tag>`. It writes the machine copy, the machine
  config and ONE PreToolUse hook in the user-level `~/.claude/settings.json` (merged, never
  replaced, backed up once). `uninstall` removes them. `CLAUDE_LOCAL_LLM=off` stops one shell from
  USING the model; the machine-wide game guard keeps running regardless.
- **Changing a setting** (a VRAM floor, the window, an exempt fixture) touches no repo: the user runs
  the machine copy, e.g. `py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" install --min-free-vram-mb 16384`.
- ⚠ If the user also runs that model outside the helper (`ollama run`, another app), its default
  window must EQUAL the helper's `num_ctx` (32768 unless `install --num-ctx N` said otherwise). A
  different `num_ctx` reloads the model, so otherwise every switch between the two costs a cold load.
  Set it in the model's Modelfile (`PARAMETER num_ctx 32768`) and re-run `ollama create`, then check
  with `ollama show <tag> --parameters`.

## ⛔ This file is public, in every repo that joined

Never write this machine's facts into a repo: model tags, IPs/URLs, account or computer names, tokens,
local paths, VRAM floors. They belong in the machine config under `%LOCALAPPDATA%\claude-local-llm\`,
which no repo contains, and no command writes them anywhere else. (The helper's header keeps dated
measurements of one public model as worked examples; that is documentation, not configuration.)
