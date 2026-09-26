---
name: local-llm
description: Offload bulk text work to this machine's local Ollama model -- summarising or extracting from large on-disk logs/dumps/docs, zh-TW translation drafts, first-pass triage of many similar items -- and keep that model's VRAM away from commercial games. Use when a task would otherwise pull a large file into context, or before launching, injecting or running a rig against a commercial (non-DumperTest) game. Does nothing on a machine that has not opted in.
---

# Local LLM (Ollama): when to use it, and when the GPU belongs to the game

The mechanism and every measurement behind these rules live in the header of
[`tools/llm/ollama_local.py`](../../../tools/llm/ollama_local.py). This file says **when**.

## 0. Is it available here? Ask once per session, before anything else

```bash
py tools/llm/ollama_local.py status
```

`state=ready` → usable. Anything else (`disabled`, `absent`, `no-model`) → do the work yourself and
do not mention the LLM again. Never pull a model or start a server to make it ready.

## 1. ⛔ A commercial game owns the GPU

- While a commercial game runs, or is about to, **do not use the LLM**. `ask` / `warm` refuse
  (exit 3) and unload on their own.
- The hook unloads the model before a `steam -applaunch` / `*-Win64-Shipping.exe` launch, and
  whenever it finds a commercial game running beside a loaded model.
- ⚠ **The gap the hook cannot close:** a `tools/verify/` rig that *launches* a commercial game
  inside one command. Run `py tools/llm/ollama_local.py unload` **before** such a rig. When unsure,
  unload: it costs 0.35 s.
- ✅ **The DumperTest fixtures are exempt** (DumperTest, DumperTest51, DumperTest58, ...): the LLM may
  run beside them.
- A game whose exe does not look like a UE shipping build is invisible to the process check. If
  `ask` refuses for low VRAM, that is probably why: unload and tell the user.

## 2. Is it worth it? The cost model

- The **cold load is the slow part** (~15-20 s); warm answers are fast. An idle model unloads itself
  after 10 minutes.
- Before a batch: start `warm` with `run_in_background`, keep working, then send the items.
- **One small question → do it yourself.** The load alone costs more than the answer saves.
- **The real win is context.** `--file` is read by the script, so a 200 KB log never enters your
  context; you read back a short answer instead.

## 3. What to delegate, and what never to

**Good fits**
- Summarise / extract the gist of large on-disk text: DLL logs, dumps, long docs. Anything over
  the window: add `--chunked`.
- zh-TW translation **drafts**, which you then review.
- First-pass triage of many similar items; spot-check a sample yourself.

**Never**
- Anything that becomes **evidence**: verification-register rows, offsets or addresses, CE Lua API
  facts (CLAUDE.md: never invent an API), audit findings, derived counts.
- Code edits in this repo.
- Anything grep or Python answers deterministically: exact values, counts, max/min, "does X
  appear". Measured: asked for the maximum of ~800-2000 numbers per slice, it was **wrong on 1 of 7
  slices**, stated just as confidently as the six right ones.

## 4. Its output is a lead, not a finding

- Verify before acting on it or quoting it. When a reply to the user rests on LLM output, say so.
- It is told to answer only from the input it was given; "not in the input" is a valid answer.

## 5. Commands

```bash
py tools/llm/ollama_local.py warm
```
```bash
py tools/llm/ollama_local.py ask --prompt "What failed, and at which step?" --file "$LOCALAPPDATA/UE5CEDumper/Logs/<Process>/scan-0.log" --chunked
```
```bash
py tools/llm/ollama_local.py unload
```

- Also `--prompt-file F` for long instructions and `--think` for reasoning-heavy asks (slower).
- Exit codes: `0` ok · `2` unavailable here · `3` refused (a game holds the GPU, or too little VRAM) ·
  `4` input too large (the message carries the **server's** token count).
- `--chunked` prints `### slice N -- <file> lines a-b` and one answer per slice. A slice that
  overflows is re-split automatically at the measured rate. **Numbers tokenize digit by digit**, so
  numeric logs fit far fewer lines per slice than prose.
- ⚠ The hook reads your **command text**. A literal `steam ... -applaunch <digits>` or
  `<Name>-Win64-Shipping.exe` in any Bash command, even inside an `echo`, unloads the model. That is
  harmless, but it costs a re-warm.

## 6. Setting up another machine

When the user says "I have Ollama, use model `<tag>`":

```bash
py tools/llm/ollama_local.py setup --model <tag>
```

It checks the tag against the running server (`--url` if not the default local port) and writes
`.claude/local-llm.json` plus a PreToolUse hook into `.claude/settings.local.json`. **Both are
gitignored.** The hook applies from the next session. To opt out: `setup --remove`, or set
`UE5CE_LLM=off` for one shell.

## ⛔ This file is public

Never write machine facts here or in the helper: model tags, hardware, IPs/URLs, whether a server
is exposed to the network, local paths. They belong in the gitignored `.claude/local-llm.json`.
