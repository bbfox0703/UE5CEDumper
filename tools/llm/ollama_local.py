#!/usr/bin/env python3
r"""Local Ollama helper for Claude Code sessions: opt-in per machine, a no-op everywhere else.

    py tools/llm/ollama_local.py status [--json]      # disabled | absent | no-model | ready
    py tools/llm/ollama_local.py warm                 # load the model now (the cold load is the slow part)
    py tools/llm/ollama_local.py ask --prompt TEXT [--file PATH ...] [--chunked] [--think]
    py tools/llm/ollama_local.py unload [--wait S]    # free the VRAM now (PENDING if a request is in flight)
    py tools/llm/ollama_local.py reserve [--wait S]   # reserve the GPU for a game, machine-wide, then unload
    py tools/llm/ollama_local.py release              # clear a reservation (an abandoned launch)
    py tools/llm/ollama_local.py install --model TAG  # install / update THE machine copy (from a source checkout)
        [--url U] [--num-ctx N] [--exempt PREFIX] [--min-free-vram-mb N] [--force]
                                                      # from the machine copy: change settings only
    py tools/llm/ollama_local.py uninstall [--leave-all]
    py tools/llm/ollama_local.py join [--repo PATH] [--force]   # a repo joins: it receives the skill, nothing else
    py tools/llm/ollama_local.py leave [--repo PATH]  # ...and leaves by losing it
    py tools/llm/ollama_local.py repos                # joined repos, and whether their skill is current
    py tools/llm/ollama_local.py hook                 # the PreToolUse hook body (hook JSON on stdin)
    py tools/llm/ollama_local.py --selftest           # pure-logic controls: no network, no processes

After `install`, every command above also runs as the MACHINE copy, from any repo:
    py "$LOCALAPPDATA/claude-local-llm/ollama_local.py" status        (PowerShell: $env:LOCALAPPDATA)
`setup --model TAG` / `setup --remove` are the version-1 names of install / uninstall.

WHEN to use it is the skill's business: .claude/skills/local-llm/SKILL.md. How ANOTHER repo adopts it
is tools/llm/README.md. This file is the mechanism and the guard.

ONE INSTALL PER MACHINE, ANY NUMBER OF REPOS (version 2). `install` copies this file and the skill to
%LOCALAPPDATA%\claude-local-llm\, writes the machine config there, and points ONE user-level hook at
that copy. A repo JOINS by receiving the skill file only (`join`), which calls the machine copy through
$LOCALAPPDATA and so carries no machine fact; it LEAVES by losing it (`leave`). So every repo, every
session and the hook run the same code against the same config, leases and reservation -- there is no
per-repo copy to drift, and no repo can remove another's guard. The source checkout (this repo) is where
the helper is developed; re-running `install` there updates the machine copy for every joined repo. The
config records the installed revision (commit time), so an OLDER checkout -- a stale worktree, a second
clone -- is refused rather than silently downgrading the machine (--force to do it on purpose), and the
skill always comes from the same checkout as the helper. `join` refuses the source repo, a directory that
does not exist, the home directory, and a skill file at that path that is not this helper's.

Exit codes: 0 done / ready, 2 not available here (disabled, absent, no model), 3 refused (a
commercial game holds the GPU, a reservation, or too little VRAM free), 4 input too large,
5 unload still pending (a request in flight), 1 anything else.

⭐ OPT-IN, BECAUSE THE REPO IS PUBLIC. Nothing machine-specific is committed -- not by this repo and not
by a joined one: the model tag, URL, local paths, the joined-repo list and the leases live only in the
machine dir, and the hook only in the user-level ~/.claude/settings.json. Without the install every
subcommand answers `disabled` WITHOUT touching the network, so a clone on another PC, or a stranger's,
is unaffected even if it runs Ollama with the same model -- and the hook itself returns at once.

⛔ GAMES AND VRAM. A 12B Q8 model holds ~14 GB of VRAM; a commercial game under test must not share
the card with it. Layered, because a rule the model has to remember is not a rule:
  1. `ask` / `warm` REFUSE while a commercial game process is running -- and unload on the way out.
     The check runs before EVERY request (each --chunked slice, and before a retry after an HTTP
     500), and it re-reads /api/ps each time rather than trusting what this process last saw.
  2. `hook` runs before every Bash / PowerShell call and every computer-use action that can start a
     game (a click, a key, opening an app), in every session on the machine. It unloads before an
     explicit launch (steam -applaunch, a *-Win64-Shipping.exe path in the command), whenever the
     model is loaded while a commercial game is running, and whenever it is loaded under an active
     reservation -- which also catches rigs and manual launches, one tool call late. The per-shell
     CLAUDE_LOCAL_LLM=off never turns it off: that switch stops a shell USING the model, not the guard.
  3. A machine-wide GPU RESERVATION (%LOCALAPPDATA%\claude-local-llm\gpu-reserved.json), written by
     the hook on a launch, by (1) when it sees a game, and by `reserve`. Every session refuses while
     it holds -- which is what stops ANOTHER session loading the model back in the gap before the
     game's process appears. Once no game runs, it lapses RESERVE_GRACE_S after the LAST time one
     was seen (every check that sees one stamps `last_game`); `release` clears it.
  4. Free VRAM: a cold load is refused when the NVIDIA GPU CUDA would use (nvidia-smi: an integrated
     AMD / Intel GPU is never counted) has less free than the model needs -- weights + the KV cache
     computed from the model's own GGUF metadata + compute overhead + a buffer, and at least the
     machine's floor (`install --min-free-vram-mb N`). `status` prints the arithmetic.
  5. KEEP_ALIVE is short, so an idle model leaves on its own.
  The DumperTest fixtures (DumperTest, DumperTest51, DumperTest58, ...) are exempt by default: the
  maintainer's rule; a machine adds its own (`install --exempt PREFIX`). ⚠ THE REMAINING GAPS: a rig
  that LAUNCHES a commercial game inside one tool call starts it after the hook has run -- the skill
  says `reserve` before such a rig; and a request already in flight is never cut short, so the VRAM
  frees when it ends (Ollama defers the unload; measured).

CROSS-SESSION, MEASURED 2026-09-26 with OLLAMA_NUM_PARALLEL=1 (two real processes): a second
session's request QUEUES behind the first (3.8 s alone -> 7.6 s); an unload sent mid-request is
DEFERRED until it ends. So ask/warm hold a LEASE (leases\<pid>.json beside the reservation) while a
request is in flight: `status` shows other sessions' as in_use_by, and a PENDING unload names the
holder. Nothing here sees `ollama run`, another app or a LAN client -- Ollama exposes no in-flight
requests.

LIVE-CHECKED 2026-09-26, in a fresh session after the version-2 install (hook -> the machine copy):
  * a Bash command whose TEXT held `steam.exe -applaunch <id>` (an echo) wrote the launch reservation
    before the command ran;
  * with the model resident (/api/ps listed it) and a reservation written by a tool the matcher does NOT
    cover (so no hook ran), a computer-use `open_application` alone unloaded it: /api/ps read empty
    right after, with 10 minutes of keep_alive left. That is the computer-use matcher and the
    evict-under-reservation path, both on the real hook.

MEASURED 2026-09-25 on the first machine that opted in (the numbers are hardware-bound; the
behaviours are Ollama's own):
  * cold load ~20 s; unload (keep_alive 0) ~0.35 s, VRAM back within 1 s.
  * ⚠ a DIFFERENT num_ctx RELOADS the model (another cold load), so num_ctx is fixed per machine
    (config key `num_ctx`, default NUM_CTX) and oversized input is refused or chunked, never "fixed"
    with a bigger window.
  * THE WINDOW IS CHEAP FOR THIS MODEL, and the GGUF header says why. Of its 48 layers, 40 are
    sliding-window (8 KV heads x 256, capped at 1024 tokens whatever num_ctx is) and only 8 are
    global -- with ONE KV head of 512. So each extra token of window costs 8 layers x 1 head x
    (512 K + 512 V) x 2 bytes (f16) = 16 KiB of KV cache. Measured (VRAM over idle, 2026-09-26):
        num_ctx    8192   16384   32768   65536   131072
        MiB       14287   14447   14767   15407    16081
    i.e. ~20 KiB/token (KV + compute buffer) up to 64k. From 64k to 128k it grew by half that,
    so Ollama's memory fitting changes beyond 64k: measure, never extrapolate. To re-derive for
    another model, read `<arch>.attention.*` from the GGUF header (head_count_kv, key_length,
    value_length, sliding_window, sliding_window_pattern).
  * ⚠ /api/ps `size_vram` reported 1.09 GB for a model nvidia-smi measured at 13.9 GB. Never judge
    VRAM from it; "unloaded" means "absent from /api/ps".
  * ⚠ on Windows a connect to a CLOSED loopback port waits out the whole timeout (a 1.5 s timeout
    took 1.51 s) instead of failing fast, so PROBE_TIMEOUT is short.
  * ⛔ BY DEFAULT OLLAMA TRUNCATES AN OVER-WINDOW PROMPT SILENTLY. A ~134k-token request to a 32k
    window came back HTTP 200 with prompt_eval_count 16387: the question at the top was cut away and
    the model confidently answered a DIFFERENT question. Every request therefore sends
    `truncate: false`, which makes the same request an HTTP 400 carrying the server's exact count
    ("request (134020 tokens) exceeds the available context size (32768 tokens)") in 0.8 s. An Ollama
    too old to know the field would ignore it and truncate again -- measured on 0.34.4.
  * ⚠ THIS MODEL'S TOKENIZER SPLITS NUMBERS DIGIT BY DIGIT: numeric log text measured 1.5-1.7 chars
    per token (the estimate had assumed 2.8 and was 1.8x short). So the estimate only PLANS slices;
    the server's count decides, and --chunked re-splits an overflowing slice at the MEASURED rate.

⚠ NOT OLLAMA_HOST. That variable is the SERVER's bind address; on a machine that exposes Ollama to
the network it is `0.0.0.0`, which a client cannot connect to on Windows. The URL comes from the
config file or DEFAULT_URL.
"""
from __future__ import annotations

import argparse
import csv
import io
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

# ── Magic values, all in one place ───────────────────────────────────────────────────────────────
SCRIPT = pathlib.Path(__file__).resolve()
ROOT = SCRIPT.parents[2]               # a SOURCE checkout: <repo>/tools/llm/ollama_local.py
# Bump when behaviour changes: the machine config records the version installed, and `status` run from
# a newer source checkout says so -- every repo on the machine runs the ONE installed copy.
HELPER_VERSION = 2
CONFIG_REL = pathlib.Path(".claude") / "local-llm.json"          # legacy per-repo opt-in (version 1)
SETTINGS_REL = pathlib.Path(".claude") / "settings.local.json"
SKILL_REL = pathlib.Path(".claude") / "skills" / "local-llm" / "SKILL.md"
# =off stops THIS shell from USING the model (ask / warm / status answer `disabled`). It never disables the
# machine-wide GPU guard: the hook ignores it, or a session that opted out could launch a game beside a
# model another session loaded.
DISABLE_ENV = "UE5CE_LLM"              # legacy name
DISABLE_ENVS = ("CLAUDE_LOCAL_LLM", DISABLE_ENV)
HOME_ENV = "CLAUDE_LOCAL_LLM_HOME"     # overrides the machine state dir -- the selftest and rigs only
DEFAULT_URL = "http://127.0.0.1:11434"
PROBE_TIMEOUT = 0.4                    # s -- a closed loopback port costs the whole timeout
UNLOAD_TIMEOUT = 10
SETUP_TIMEOUT = 3
GENERATE_TIMEOUT = 600                 # a cold load plus a long answer
LOAD_RETRY_DELAY_S = 2                 # before retrying a load that died in CUDA init (HTTP 500)
NUM_CTX = 32768                        # fixed: a different value reloads the model
OUTPUT_RESERVE = 4096                  # tokens of the window kept for the answer
KEEP_ALIVE = "10m"
TEMPERATURE = 0.2
# VRAM a cold load needs = weights + KV cache (computed from the MODEL'S OWN GGUF metadata, so it holds for any
# model) + compute/runtime overhead + a safety buffer, and at least the machine's floor (config
# `min_free_vram_mb`, set with `install --min-free-vram-mb N`; a machine fact, so never in a repo).
KV_BYTES_PER_ELEM = 2                  # f16 KV cache (Ollama's default); a quantized cache only needs less
COMPUTE_BASE_MIB = 1152                # compute buffer + CUDA runtime: measured on the 12B below, 8k..64k,
COMPUTE_PER_TOKEN_KIB = 4              # ...together within ~40 MiB of every measured point
VRAM_BUFFER_MIB = 512                  # on top of the estimate
FALLBACK_KV_KIB_PER_TOKEN = 160        # no usable metadata: assume a dense model this heavy per token
HOOK_TIMEOUT_S = 15
# The tools that can START a game: shells, and the computer-use actions that click / type / open an app (the
# handover launches Steam titles through the library UI). Screenshots and reads are left out: they cannot
# launch anything, and each hook costs a Python spawn.
HOOK_MATCHER = ("Bash|PowerShell|mcp__computer-use__(?:left_click|double_click|triple_click|left_mouse_up|key|"
                "type|open_application|computer_batch)")
HOOK_BUDGET_S = HOOK_TIMEOUT_S - 2     # the hook's own work must end inside Claude Code's timeout
# The hook runs `python -c HOOK_BOOTSTRAP <script> hook`, never `python <script> hook`: for a missing
# script python exits 2, and a PreToolUse hook exiting 2 BLOCKS the tool call -- every Bash call, in
# every repo, once the hook lives in the user-level settings. The bootstrap makes that a silent no-op.
HOOK_BOOTSTRAP = ("import os,sys,runpy;p=sys.argv[1];sys.argv=[p]+sys.argv[2:];"
                  "os.path.isfile(p) and runpy.run_path(p,run_name='__main__')")
USER_SETTINGS = pathlib.Path.home() / ".claude" / "settings.json"
USER_SETTINGS_ENV = "CLAUDE_LOCAL_LLM_USER_SETTINGS"   # the selftest and rigs only
USER_SETTINGS_BACKUP_SUFFIX = ".before-local-llm"
# Everything machine-specific lives HERE and nowhere else: the installed copy, its config (model tag,
# URL), the joined-repo list, leases, the GPU reservation. It is MACHINE-wide (every session, every repo),
# outside every checkout -- so nothing machine-specific can reach a public repo -- and outside
# %LOCALAPPDATA%\UE5CEDumper, which is the UI app's own data (CLAUDE.md "App-data layout").
STATE_DIRNAME = "claude-local-llm"
MACHINE_SCRIPT_FILE = "ollama_local.py"
MACHINE_SKILL_FILE = "SKILL.md"
MACHINE_CONFIG_FILE = "config.json"
JOINED_FILE = "repos.json"
LEASE_DIRNAME = "leases"
RESERVATION_FILE = "gpu-reserved.json"
LEASE_MAX_AGE_S = GENERATE_TIMEOUT * 3  # older than any request can run: stale even if its pid was reused
# How long a GPU reservation outlives its evidence: a launch has until the game's process appears; a
# game seen running keeps it active, and it lapses shortly after the game exits.
RESERVE_GRACE_S = {"launch": 180, "running": 30}
UNLOAD_WAIT_S = 5                      # Ollama DEFERS an unload until any in-flight request ends
HOOK_UNLOAD_WAIT_S = 5                 # the hook must stay well inside HOOK_TIMEOUT_S
EXIT_UNLOAD_PENDING = 5
ASCII_CHARS_PER_TOKEN = 2.0            # planning only; numeric logs measured 1.5-1.7, prose ~4
HOPELESS_CHARS_PER_TOKEN = 8           # no real text averages more, so len/8 > window can't fit
CHUNK_SLACK_TOKENS = 256
RESPLIT_SAFETY = 0.85                  # re-split an overflowing slice at 85% of its measured rate
MAX_RESPLIT_DEPTH = 6
OVERFLOW_RE = re.compile(r"request \((\d+) tokens\) exceeds", re.I)

# A UE packaged-game process: <Stem>-<Platform>-<Config>.exe. A launcher shim (Elliot.exe) is not
# matched; the shipping exe beside it always runs while the game does.
EXE_TAIL = r"-(?:Win64|WinGDK|WinGRDK)-(?:Shipping|Test|DebugGame)\.exe"
PROCESS_EXE_RE = re.compile(r"(.+)" + EXE_TAIL, re.I)
COMMAND_EXE_RE = re.compile(r"([^\\/\"'\s=(),;:|<>`]+)" + EXE_TAIL + r"\b", re.I)  # `--exe=X`, `(X)` too
# Test fixtures that may share the GPU with the model: this repo's DumperTest projects (DumperTest,
# DumperTest51, ...). A machine adds its own with `install --exempt PREFIX` (config `exempt_prefixes`).
DEFAULT_EXEMPT_PREFIXES = ("dumpertest",)
# UE-shaped helper processes that are never the game. EOSOverlayRenderer is the Epic Online Services
# overlay: measured 2026-09-26 running under the Epic Games Launcher with NO game open, which made the
# guard refuse the LLM for as long as the launcher was up. An EOS game still has its own shipping exe
# alive beside it, so excluding the helper loses nothing.
NON_GAME_STEMS = frozenset({"eosoverlayrenderer"})
# DumperTest is never a Steam app, so any Steam launch is a commercial one. `[\\"']*` and not `["']?`:
# in raw (unparsed) hook JSON the closing quote after steam.exe arrives escaped, as `\"`.
STEAM_LAUNCH_RE = re.compile(r"steam(?:\.exe)?[\\\"']*\s+(?:\S+\s+)*?-applaunch\s+\d+"
                             r"|steam://(?:rungameid|run)/\d+", re.I)

# Neutral, because every joined repo uses the one installed copy; a machine can override it with the
# config key `system_prompt`.
SYSTEM_PROMPT = (
    "You are a careful assistant helping with a software project. "
    "Answer only from the provided input. If the input does not contain the answer, say so plainly. "
    "Never invent function names, offsets, addresses, APIs or file paths. Be concise."
)


# ── Pure logic (every function here is covered by --selftest) ────────────────────────────────────
def normalize_model(name: str) -> str:
    """Ollama lists `foo` as `foo:latest`; compare case-insensitively on the full name:tag."""
    n = (name or "").strip().lower()
    return n if ":" in n.rsplit("/", 1)[-1] else n + ":latest"


def find_model(tags: dict, wanted: str):
    w = normalize_model(wanted)
    for m in (tags or {}).get("models") or []:
        if normalize_model(m.get("name") or m.get("model") or "") == w:
            return m
    return None


def exempt_of(cfg) -> tuple:
    """The machine's exempt fixture prefixes (lower-case); the defaults when the config names none."""
    got = [str(x).strip().lower() for x in ((cfg or {}).get("exempt_prefixes") or []) if str(x).strip()]
    return tuple(got) or DEFAULT_EXEMPT_PREFIXES


def system_prompt_of(cfg) -> str:
    return str((cfg or {}).get("system_prompt") or SYSTEM_PROMPT)


def _is_game_stem(stem: str, exempt=DEFAULT_EXEMPT_PREFIXES) -> bool:
    s = stem.lower()
    return not s.startswith(tuple(exempt)) and s not in NON_GAME_STEMS


def is_commercial_process(image: str, exempt=DEFAULT_EXEMPT_PREFIXES) -> bool:
    m = PROCESS_EXE_RE.fullmatch((image or "").strip())
    return bool(m) and _is_game_stem(m.group(1), exempt)


def command_launches_commercial(command: str, exempt=DEFAULT_EXEMPT_PREFIXES) -> str | None:
    """What in this shell command looks like a commercial game launch, or None.

    Errs toward yes: `taskkill /IM Elliot-Win64-Shipping.exe` also matches. That costs nothing
    unless the model is loaded, and then only a re-warm."""
    m = STEAM_LAUNCH_RE.search(command or "")
    if m:
        return m.group(0)
    for m in COMMAND_EXE_RE.finditer(command or ""):
        if re.search(r"/im\s+[\"']?$", command[max(0, m.start() - 8):m.start()], re.I):
            continue                                 # `taskkill /IM <exe>` names it to KILL it
        if _is_game_stem(m.group(1), exempt):
            return m.group(0)
    return None


def hook_command(raw: str) -> str:
    """The shell command from the hook's stdin JSON; the RAW text if that is not parseable JSON.

    A guard that goes blind on malformed input is the wrong way to fail: found 2026-09-25 when a
    hand-built test payload (a `\\` collapsed by the shell) was silently a no-op."""
    try:
        data = json.loads(raw) if (raw or "").strip() else {}
        return str(((data.get("tool_input") or {}).get("command")) or "")
    except (ValueError, AttributeError):
        return raw or ""


def parse_tasklist_procs(text: str) -> list[tuple[int, str]]:
    """(pid, image) from `tasklist /FO CSV /NH`; the "INFO: No tasks" line is dropped."""
    out = []
    for row in csv.reader(io.StringIO(text or "")):
        if len(row) >= 2 and row[0].lower().endswith(".exe") and row[1].strip().isdigit():
            out.append((int(row[1]), row[0]))
    return out


def parse_tasklist(text: str) -> list[str]:
    return [name for _, name in parse_tasklist_procs(text)]


def live_leases(records, now: float, alive_pids, own_pid: int) -> list[dict]:
    """Leases of OTHER processes with a request in flight: pid alive, and younger than any request
    can run (so a reused pid cannot keep a dead lease alive)."""
    return [r for r in records if isinstance(r, dict) and r.get("pid") != own_pid
            and r.get("pid") in alive_pids and 0 <= now - float(r.get("started") or 0) < LEASE_MAX_AGE_S]


def reservation_state(marker, now: float, game_running: bool) -> str:
    """none | active | expired. A game seen running keeps it active however old the marker is; after that
    the grace runs from the LAST time a game was seen (`last_game`, refreshed by every check that sees one),
    not from when the marker was set -- a game that ran for an hour still leaves its 30 s."""
    if not isinstance(marker, dict):
        return "none"
    if game_running:
        return "active"
    grace = RESERVE_GRACE_S.get(marker.get("kind"), max(RESERVE_GRACE_S.values()))
    since = max(float(marker.get("set_at") or 0), float(marker.get("last_game") or 0))
    return "active" if 0 <= now - since < grace else "expired"


def commercial_games(images, exempt=DEFAULT_EXEMPT_PREFIXES) -> list[str]:
    return sorted({i for i in images if is_commercial_process(i, exempt)}, key=str.lower)


def ollama_running(images) -> bool:
    """Is a local Ollama server process up? (`ollama.exe` / `ollama app.exe`)"""
    return any((i or "").lower() in ("ollama.exe", "ollama app.exe", "ollama") for i in images)


def _per_layer(value, n):
    """A GGUF per-layer field: a list (one per layer) or a scalar for all; None when absent."""
    if isinstance(value, list):
        return [value[i] if i < len(value) else value[-1] for i in range(n)] if value else None
    return [value] * n if value is not None else None


def kv_cache_mib(model_info: dict, num_ctx: int):
    """KV cache in MiB at `num_ctx`, from Ollama /api/show `model_info`; None when the metadata cannot say.

    Per layer: tokens x head_count_kv x (key_length + value_length) x KV_BYTES_PER_ELEM, where a
    sliding-window layer holds min(num_ctx, sliding_window) tokens with its own *_swa lengths. Works for any
    architecture that publishes these standard GGUF keys; a model without a sliding pattern is counted at
    the full window on every layer (an over-, never an under-estimate)."""
    mi = model_info or {}
    arch = mi.get("general.architecture")
    if not arch:
        return None

    def g(k):
        return mi.get(f"{arch}.{k}")
    n = g("block_count")
    if not isinstance(n, int) or n <= 0:
        return None
    heads = g("attention.head_count")
    kv_heads = _per_layer(g("attention.head_count_kv"), n) or _per_layer(heads, n)
    if not kv_heads:
        return None
    emb = g("embedding_length")
    head_dim = emb // heads if isinstance(emb, int) and isinstance(heads, int) and heads else None
    k_len = g("attention.key_length") or head_dim
    v_len = g("attention.value_length") or k_len
    if not k_len or not v_len:
        return None
    k_swa, v_swa = g("attention.key_length_swa") or k_len, g("attention.value_length_swa") or v_len
    window, pattern = g("attention.sliding_window"), g("attention.sliding_window_pattern")
    if isinstance(pattern, list):
        sliding = [bool(pattern[i]) if i < len(pattern) else False for i in range(n)]
    elif isinstance(pattern, int) and not isinstance(pattern, bool) and pattern > 1:
        sliding = [(i + 1) % pattern != 0 for i in range(n)]          # every Nth layer global
    else:
        sliding = [False] * n
    total = 0
    for i in range(n):
        if sliding[i] and isinstance(window, int) and window > 0:
            total += min(num_ctx, window) * int(kv_heads[i] or 0) * (k_swa + v_swa) * KV_BYTES_PER_ELEM
        else:
            total += num_ctx * int(kv_heads[i] or 0) * (k_len + v_len) * KV_BYTES_PER_ELEM
    return total / (1024 * 1024)


def vram_need_mib(weights_bytes: int, model_info, num_ctx: int, floor_mib: int = 0) -> tuple[int, str]:
    """(MiB a cold load needs free, how it was derived). Checked against the measured table in the header:
    for the 12B there the estimate sits VRAM_BUFFER_MIB (+~40) above the measurement at every window."""
    weights = (weights_bytes or 0) / (1024 * 1024)
    kv = kv_cache_mib(model_info, num_ctx)
    how = "metadata"
    if kv is None:
        kv, how = num_ctx * FALLBACK_KV_KIB_PER_TOKEN / 1024, "fallback: no usable metadata"
    overhead = COMPUTE_BASE_MIB + num_ctx * COMPUTE_PER_TOKEN_KIB / 1024
    est = int(weights + kv + overhead + VRAM_BUFFER_MIB + 0.5)
    need = max(est, int(floor_mib or 0))
    detail = (f"weights {weights:.0f} + KV {kv:.0f} ({how}) + compute {overhead:.0f} + buffer "
              f"{VRAM_BUFFER_MIB} = {est} MiB" + (f", floor {int(floor_mib)} MiB" if floor_mib else ""))
    return need, detail


def pick_gpu(csv_text: str, visible=None):
    """(free MiB, name) of the NVIDIA GPU with the most free memory among those CUDA may use; None if none.

    `csv_text` is `nvidia-smi --query-gpu=index,uuid,name,memory.total,memory.used --format=csv,noheader,nounits`,
    which lists NVIDIA GPUs only -- an integrated AMD / Intel GPU can never be the one judged. `visible` is
    CUDA_VISIBLE_DEVICES (indices or UUIDs); unset = all."""
    rows = []
    for line in (csv_text or "").splitlines():
        parts = [x.strip() for x in line.split(",")]
        if len(parts) < 5:
            continue
        try:
            rows.append((parts[0], parts[1], ",".join(parts[2:-2]), int(parts[-2]), int(parts[-1])))
        except ValueError:
            continue
    if visible is not None and visible.strip():
        want = [v.strip() for v in visible.split(",") if v.strip()]
        rows = [r for r in rows if r[0] in want or any(r[1] == w or (w.startswith("GPU-") and r[1].startswith(w))
                                                       for w in want)]
    if not rows:
        return None
    best = max(rows, key=lambda r: r[3] - r[4])
    return best[3] - best[4], best[2]


def compare_revisions(installed, mine) -> str:
    """newer | older | same | unknown: this checkout's helper revision against the installed one (commit
    times). It is what stops an old checkout from silently downgrading the machine."""
    if installed is None or mine is None:
        return "unknown"
    return "newer" if mine > installed else "older" if mine < installed else "same"


def estimate_tokens(text: str, cpt: float = ASCII_CHARS_PER_TOKEN) -> int:
    """A PLANNING estimate (cpt = ASCII chars per token); the server's count is the truth."""
    ascii_n = len(text.encode("ascii", "ignore"))
    return int(ascii_n / cpt + (len(text) - ascii_n)) + 1


def parse_overflow(status: int, body: str):
    """The token count the server gave when refusing an over-window request (truncate:false);
    -1 if it refused without a count; None if this is not an overflow at all."""
    if status != 400 or "exceed" not in (body or "").lower():
        return None
    m = OVERFLOW_RE.search(body or "")
    return int(m.group(1)) if m else -1


def build_request(prompt: str, sections) -> str:
    """Content FIRST, the task LAST: models attend best to the end of a long context, and it is the
    end that survives if anything is ever cut. `sections` = [(header, text), ...]."""
    body = "".join(f"=== {header} ===\n{text}\n\n" for header, text in sections)
    return f"{body}=== TASK ===\n{prompt}"


def split_lines(text: str, budget_tokens: int, cpt: float = ASCII_CHARS_PER_TOKEN) -> list[tuple[int, int, str]]:
    """Consecutive line slices (first, last line number, text), each under the token budget.

    A single line over budget is cut by characters, so nothing is dropped and nothing overlaps."""
    max_chars = max(1, int(budget_tokens * min(cpt, 1.0)))   # 1 char >= 1 token: safe for any mix
    pieces = []
    for n, line in enumerate(text.splitlines(keepends=True), 1):
        if estimate_tokens(line, cpt) <= budget_tokens:
            pieces.append((n, line))
        else:
            pieces.extend((n, line[i:i + max_chars]) for i in range(0, len(line), max_chars))
    chunks, cur, first, last, used = [], [], None, None, 0
    for n, piece in pieces:
        cost = estimate_tokens(piece, cpt)
        if cur and used + cost > budget_tokens:
            chunks.append((first, last, "".join(cur)))
            cur, first, used = [], None, 0
        cur.append(piece)
        first = n if first is None else first
        last, used = n, used + cost
    if cur:
        chunks.append((first, last, "".join(cur)))
    return chunks


def config_candidates(root: pathlib.Path) -> list[pathlib.Path]:
    """The opt-in file; a session in .claude/worktrees/<name>/ also reads the MAIN checkout's."""
    out = [root / CONFIG_REL]
    parts = root.parts
    for i in range(len(parts) - 1):
        if parts[i].lower() == ".claude" and parts[i + 1].lower() == "worktrees":
            out.append(pathlib.Path(*parts[:i]) / CONFIG_REL)
            break
    return out


def main_checkout(root: pathlib.Path) -> pathlib.Path:
    return config_candidates(root)[-1].parent.parent


_OUR_SCRIPT_SUFFIXES = ("tools/llm/ollama_local.py",                 # a source checkout (version 1)
                        f"{STATE_DIRNAME}/{MACHINE_SCRIPT_FILE}")       # the machine copy (version 2+)


def _is_our_hook(h: dict) -> bool:
    """Ours = runs this helper's `hook`, from ANY checkout or the machine copy. Matching them all is what
    keeps the machine at exactly ONE entry however many repos ever ran setup."""
    args = [str(a) for a in (h.get("args") or [])]
    if "hook" not in args:
        return False
    mine = machine_script().as_posix().lower()
    return any(a.replace("\\", "/").lower().endswith(_OUR_SCRIPT_SUFFIXES) or a.replace("\\", "/").lower() == mine
               for a in args)


def hook_target(settings) -> str | None:
    """The script our hook entry runs, or None when the settings hold no entry of ours."""
    for g in ((settings or {}).get("hooks") or {}).get("PreToolUse") or []:
        for h in g.get("hooks") or []:
            if _is_our_hook(h):
                args = [str(a) for a in h.get("args") or []]
                return args[args.index("hook") - 1] if args.index("hook") > 0 else None
    return None


def remove_hook(settings: dict) -> dict:
    """Drop only OUR PreToolUse entry; every other hook and key is left exactly as found."""
    out = dict(settings or {})
    hooks = dict(out.get("hooks") or {})
    groups = []
    for g in hooks.get("PreToolUse") or []:
        kept = [h for h in (g.get("hooks") or []) if not _is_our_hook(h)]
        if kept:
            groups.append({**g, "hooks": kept})
    if groups:
        hooks["PreToolUse"] = groups
    else:
        hooks.pop("PreToolUse", None)
    if hooks:
        out["hooks"] = hooks
    else:
        out.pop("hooks", None)
    return out


def merge_hook(settings: dict, python: str, script: str) -> dict:
    """Idempotent: re-running setup replaces our entry (a moved checkout gets the new path)."""
    out = remove_hook(settings)
    hooks = dict(out.get("hooks") or {})
    hooks["PreToolUse"] = list(hooks.get("PreToolUse") or []) + [{
        "matcher": HOOK_MATCHER,
        "hooks": [{"type": "command", "command": python, "args": ["-c", HOOK_BOOTSTRAP, script, "hook"],
                   "timeout": HOOK_TIMEOUT_S}],
    }]
    out["hooks"] = hooks
    order = list(settings or {})                     # keep the user's key order: remove_hook may have
    return dict(sorted(out.items(),                  # popped "hooks" and re-adding would append it
                       key=lambda kv: order.index(kv[0]) if kv[0] in order else len(order)))


def settings_text(data: dict) -> str:
    """A Claude Code settings file as written back: non-ASCII stays literal (the user-level file holds
    e.g. a "language" in CJK), never \\u-escaped."""
    return json.dumps(data, indent=2, ensure_ascii=False) + "\n"


def newline_of(original) -> str:
    """The line ending to write a file back with: the one it already has (LF for a new file).

    Path.write_text translates "\\n" to os.linesep on Windows, which rewrote an LF settings.json
    as CRLF -- a one-hook edit that diffed as a whole-file rewrite (measured 2026-09-26)."""
    return "\r\n" if original and b"\r\n" in original else "\n"


def disabled_by_env(env) -> bool:
    return any((env.get(k) or "").strip().lower() in ("off", "0", "false", "no") for k in DISABLE_ENVS)


# ── I/O ──────────────────────────────────────────────────────────────────────────────────────────
_OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))   # never via a proxy


def http_json(url: str, payload=None, timeout: float = PROBE_TIMEOUT):
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(url, data=data,
                                 headers={"Content-Type": "application/json"} if data else {})
    with _OPENER.open(req, timeout=timeout) as r:
        return json.loads(r.read().decode("utf-8"))


def _toolhelp_processes() -> list[tuple[int, str]]:
    """(pid, image) from a Toolhelp32 snapshot -- milliseconds, where `tasklist` took ~1.6 s.

    The hook runs before EVERY Bash call while the model is loaded, so the spawn cost was paid on each
    one (measured 2026-09-25). ctypes, not PowerShell: see the AMSI note in handover section 10."""
    import ctypes
    from ctypes import wintypes

    class PROCESSENTRY32W(ctypes.Structure):
        _fields_ = [("dwSize", wintypes.DWORD), ("cntUsage", wintypes.DWORD),
                    ("th32ProcessID", wintypes.DWORD), ("th32DefaultHeapID", ctypes.c_size_t),
                    ("th32ModuleID", wintypes.DWORD), ("cntThreads", wintypes.DWORD),
                    ("th32ParentProcessID", wintypes.DWORD), ("pcPriClassBase", ctypes.c_long),
                    ("dwFlags", wintypes.DWORD), ("szExeFile", ctypes.c_wchar * 260)]

    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    k32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
    for fn in (k32.Process32FirstW, k32.Process32NextW):
        fn.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W)]
        fn.restype = wintypes.BOOL
    k32.CloseHandle.argtypes = [wintypes.HANDLE]
    snap = k32.CreateToolhelp32Snapshot(0x2, 0)                  # TH32CS_SNAPPROCESS
    if not snap or snap == ctypes.c_void_p(-1).value:            # INVALID_HANDLE_VALUE
        raise OSError(ctypes.get_last_error(), "CreateToolhelp32Snapshot")
    try:
        entry = PROCESSENTRY32W()
        entry.dwSize = ctypes.sizeof(entry)
        procs, more = [], k32.Process32FirstW(snap, ctypes.byref(entry))
        while more:
            procs.append((int(entry.th32ProcessID), entry.szExeFile))
            more = k32.Process32NextW(snap, ctypes.byref(entry))
        return procs
    finally:
        k32.CloseHandle(snap)


def _post_with_retry(url: str, payload, timeout: float, post=None, sleep=time.sleep, precheck=None):
    """POST once more after an HTTP 500, never after anything else -- and never when `precheck` (the guard)
    now refuses: a game may have started during the delay, and the retry would load the model beside it.

    Measured 2026-09-26: a model load returned 500 because llama-server died on "CUDA error: shared
    object initialization failed" (0xc0000409) -- a laptop dGPU waking from idle -- and the next
    request loaded normally. A 400 is the request's own fault (e.g. an over-window prompt) and a
    second 500 is a real failure; both propagate."""
    post = post or http_json
    try:
        return post(url, payload, timeout=timeout)
    except urllib.error.HTTPError as e:
        if e.code != 500:
            raise
    sleep(LOAD_RETRY_DELAY_S)
    refusal = precheck() if precheck else None
    if refusal:
        raise Refused(refusal)
    return post(url, payload, timeout=timeout)


def running_processes() -> list[tuple[int, str]]:
    if os.name == "nt":
        try:
            return _toolhelp_processes()
        except (OSError, AttributeError, ValueError):
            pass                                                  # fall back to tasklist
    try:
        out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, text=True,
                             errors="replace", timeout=15).stdout
    except (OSError, subprocess.SubprocessError):
        return []
    return parse_tasklist_procs(out)


def running_images() -> list[str]:
    return [name for _, name in running_processes()]


# ── Cross-session state (machine-wide; every write is best-effort and never fails the caller) ───────
def state_dir() -> pathlib.Path:
    if os.environ.get(HOME_ENV):
        return pathlib.Path(os.environ[HOME_ENV])
    base = os.environ.get("LOCALAPPDATA") or str(pathlib.Path.home() / ".cache")
    return pathlib.Path(base) / STATE_DIRNAME


def user_settings_path() -> pathlib.Path:
    return pathlib.Path(os.environ[USER_SETTINGS_ENV]) if os.environ.get(USER_SETTINGS_ENV) else USER_SETTINGS


def machine_script() -> pathlib.Path:
    return state_dir() / MACHINE_SCRIPT_FILE


def machine_skill() -> pathlib.Path:
    return state_dir() / MACHINE_SKILL_FILE


def machine_config_path() -> pathlib.Path:
    return state_dir() / MACHINE_CONFIG_FILE


def registry_path() -> pathlib.Path:
    return state_dir() / JOINED_FILE


def _same_path(a: pathlib.Path, b: pathlib.Path) -> bool:
    """realpath, not abspath: %LOCALAPPDATA% behind a junction or symlink is still the same file."""
    return os.path.normcase(os.path.realpath(a)) == os.path.normcase(os.path.realpath(b))


def is_machine_copy() -> bool:
    """True when THIS file is the installed machine copy rather than a source checkout's."""
    return _same_path(SCRIPT, machine_script())


# ── Machine install, and repos joining / leaving it ──────────────────────────────────────────────────
# A repo JOINS by receiving the skill file -- nothing else: the skill calls the machine copy through
# $LOCALAPPDATA, so the file carries no machine fact and can be committed to a public repo. It LEAVES by
# losing that file. The helper, its config and the hook are installed ONCE per machine; every joined
# repo, and every session anywhere (the hook), shares them.
def repo_skill_path(repo: pathlib.Path) -> pathlib.Path:
    return pathlib.Path(repo) / SKILL_REL


def _is_source_repo(repo: pathlib.Path) -> bool:
    """A checkout that ships the helper itself: its skill is part of its source, never `leave`d."""
    return (pathlib.Path(repo) / "tools" / "llm" / MACHINE_SCRIPT_FILE).is_file()


def _registry() -> dict:
    data = _read_json(registry_path())
    repos = (data or {}).get("repos") if isinstance(data, dict) else None
    return {"schema": 1, "repos": repos if isinstance(repos, dict) else {}}


def _registry_key(repo: pathlib.Path) -> str:
    return os.path.normcase(os.path.abspath(repo))


def register_repo(repo: pathlib.Path) -> None:
    reg = _registry()
    reg["repos"][_registry_key(repo)] = {"path": str(pathlib.Path(repo)), "joined_at": time.time()}
    _write_json(registry_path(), reg)


def unregister_repo(repo: pathlib.Path) -> bool:
    reg = _registry()
    had = reg["repos"].pop(_registry_key(repo), None) is not None
    if had:
        _write_json(registry_path(), reg)
    return had


OUR_SKILL_MARK = f"{STATE_DIRNAME}/{MACHINE_SCRIPT_FILE}"     # every version of our skill calls the machine copy


def _skill_is_ours(repo: pathlib.Path) -> bool:
    """The repo's skill file is this helper's: it calls the machine copy, or the repo is on the joined list."""
    p = repo_skill_path(repo)
    try:
        if p.is_file() and OUR_SKILL_MARK in p.read_text(encoding="utf-8", errors="replace"):
            return True
    except OSError:
        pass
    return _registry_key(repo) in _registry()["repos"]


def join_refusal(repo: pathlib.Path, force: bool) -> str | None:
    """Why `join` must not touch this directory, or None."""
    repo = pathlib.Path(repo)
    if not repo.is_dir():
        return f"{repo} is not an existing directory"
    if _same_path(repo, pathlib.Path.home()):
        return "the home directory is not a repo (a skill there would load in EVERY session)"
    if _is_source_repo(repo):
        return "this repo ships the helper itself: its skill is source, published by `install`, never joined"
    if repo_skill_path(repo).is_file() and not _skill_is_ours(repo) and not force:
        return f"{SKILL_REL.as_posix()} there is not this helper's (it never calls the machine copy); --force replaces it"
    return None


def join_repo(repo: pathlib.Path, skill_text: str) -> str:
    """joined | updated | current. Writes the skill (LF, as the installed copy has it) and registers."""
    path = repo_skill_path(repo)
    try:
        old = path.read_text(encoding="utf-8") if path.is_file() else None
    except (OSError, UnicodeDecodeError):
        old = ""                                     # unreadable: rewrite it
    if old != skill_text:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(skill_text, encoding="utf-8", newline="\n")
    register_repo(repo)
    return "joined" if old is None else ("current" if old == skill_text else "updated")


def leave_repo(repo: pathlib.Path) -> str:
    """left | not-joined | refused-source | refused-foreign. Removes OUR skill and the dirs it emptied;
    nothing else -- a skill file at that path that is not ours is left alone."""
    repo = pathlib.Path(repo)
    if _is_source_repo(repo):
        return "refused-source"
    path = repo_skill_path(repo)
    if path.is_file() and not _skill_is_ours(repo):
        return "refused-foreign"
    had = path.is_file()
    if had:
        path.unlink()
        for d in (path.parent, path.parent.parent, repo / ".claude"):      # local-llm/, skills/, .claude/
            try:
                d.rmdir()                                                    # only if empty
            except OSError:
                break
    return "left" if (unregister_repo(repo) or had) else "not-joined"


def joined_repos() -> list[dict]:
    """[{path, status}]: current | outdated | missing | unreadable against the installed skill, and
    source | source-differs for a checkout that ships the helper (it publishes via `install`, never `join`)."""
    try:
        want = machine_skill().read_text(encoding="utf-8") if machine_skill().is_file() else None
    except (OSError, UnicodeDecodeError):
        want = None
    out = []
    for rec in _registry()["repos"].values():
        repo = pathlib.Path(rec.get("path") or "")
        p = repo_skill_path(repo)
        try:
            have = p.read_text(encoding="utf-8").replace("\r\n", "\n") if p.is_file() else None
        except (OSError, UnicodeDecodeError):
            out.append({"path": rec.get("path"), "status": "unreadable"})
            continue
        same = want is None or have == want
        if _is_source_repo(repo):
            status = "source" if same else "source-differs"
        else:
            status = "missing" if have is None else ("current" if same else "outdated")
        out.append({"path": rec.get("path"), "status": status})
    return sorted(out, key=lambda r: str(r["path"]).lower())


def install_machine(src_script: pathlib.Path, skill_text: str | None, cfg: dict) -> None:
    """Copy the helper (and its skill) to the machine dir and write the machine config."""
    d = state_dir()
    d.mkdir(parents=True, exist_ok=True)
    if not _same_path(src_script, machine_script()):
        try:
            shutil.copyfile(src_script, machine_script())
        except shutil.SameFileError:
            pass
    if skill_text is not None:
        machine_skill().write_text(skill_text, encoding="utf-8", newline="\n")
    _write_json(machine_config_path(), {**cfg, "version": HELPER_VERSION}, indent=2)


def uninstall_machine() -> None:
    """Remove the machine copy, skill and config. Leases / reservation / the repo list stay: the list is
    what a later `install` or `repos` reports as joined."""
    for p in (machine_script(), machine_skill(), machine_config_path()):
        try:
            p.unlink(missing_ok=True)
        except OSError:
            pass


def resolve_repo(path):
    """--repo, else the git top level of the working directory; None when neither names a repo. git prints
    the path in UTF-8 whatever the console code page -- decoded as cp950 a CJK path became a DIFFERENT path."""
    if path:
        return pathlib.Path(path).resolve()
    try:
        top = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, encoding="utf-8",
                             errors="strict", timeout=10).stdout
        top = (top or "").strip()
        if top:
            return pathlib.Path(top).resolve()
    except (OSError, subprocess.SubprocessError, ValueError, AttributeError):
        pass
    return None


def install_skill_source(root: pathlib.Path) -> pathlib.Path:
    """The skill to install goes with the helper being installed: the SAME checkout's -- a worktree's own,
    never its main checkout's, or a helper and a skill of different revisions ship together."""
    return pathlib.Path(root) / SKILL_REL


def existing_machine_config() -> dict | None:
    """The machine config as installed, whatever the shell's CLAUDE_LOCAL_LLM switch says."""
    cfg = _read_json(machine_config_path())
    return cfg if isinstance(cfg, dict) else None


def helper_revision(root: pathlib.Path):
    """Commit time of the last change to the helper in `root` (None outside git). Compared at install so an
    OLDER checkout cannot silently replace a newer installed copy."""
    try:
        out = subprocess.run(["git", "-C", str(root), "log", "-1", "--format=%ct", "--",
                              "tools/llm/ollama_local.py", str(SKILL_REL).replace("\\", "/")],
                             capture_output=True, encoding="utf-8", errors="replace", timeout=10).stdout
        return int((out or "").strip()) if (out or "").strip().isdigit() else None
    except (OSError, subprocess.SubprocessError, ValueError):
        return None


def _read_json(path: pathlib.Path):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def _write_json(path: pathlib.Path, data, indent=None) -> bool:
    """Write-then-rename, so a reader in another session never sees a half-written file (it read as junk,
    and a junk lease or marker is pruned as dead)."""
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        tmp = path.with_name(f"{path.name}.{os.getpid()}.tmp")
        tmp.write_text(json.dumps(data, indent=indent) + ("\n" if indent else ""), encoding="utf-8", newline="\n")
        os.replace(tmp, path)
        return True
    except OSError:
        return False


class Lease:
    """Announces, machine-wide, that THIS process has a request in flight -- so `status` / `unload`
    in any other session can say who the model is busy for. Removed on exit; a crashed holder's
    lease is dropped once its pid is gone or it outlives LEASE_MAX_AGE_S."""

    def __init__(self, action: str):
        self.action = action
        self.path = state_dir() / LEASE_DIRNAME / f"{os.getpid()}.json"

    def __enter__(self):
        _write_json(self.path, {"pid": os.getpid(), "action": self.action, "cwd": os.getcwd(),
                                "started": time.time()})
        return self

    def __exit__(self, *exc):
        try:
            self.path.unlink(missing_ok=True)
        except OSError:
            pass
        return False


def other_leases() -> list[dict]:
    """Other processes' live leases; dead or stale lease files are pruned on the way."""
    d = state_dir() / LEASE_DIRNAME
    found = [(f, _read_json(f)) for f in (d.glob("*.json") if d.is_dir() else [])]
    alive = {pid for pid, _ in running_processes()}
    live = live_leases([r for _, r in found], time.time(), alive, os.getpid())
    if not alive:                                    # no process list = no evidence: never prune on it
        return live
    for f, r in found:
        if r not in live and not (isinstance(r, dict) and r.get("pid") == os.getpid()):
            try:
                f.unlink(missing_ok=True)
            except OSError:
                pass
    return live


def describe_leases(leases) -> str:
    now = time.time()
    return "; ".join(f"pid {r.get('pid')} {r.get('action')} from {r.get('cwd')} "
                     f"({now - float(r.get('started') or now):.0f}s)" for r in leases)


def read_reservation():
    return _read_json(state_dir() / RESERVATION_FILE)


def reserve_gpu(kind: str, reason: str) -> None:
    _write_json(state_dir() / RESERVATION_FILE, {"kind": kind, "reason": reason, "set_at": time.time(),
                                                 "pid": os.getpid(), "cwd": os.getcwd()})


def release_gpu(expected_set_at=None) -> bool:
    """Delete the marker -- only the one read (`expected_set_at`) when given, so an expiry never deletes a
    FRESH reservation another session wrote in between."""
    try:
        path = state_dir() / RESERVATION_FILE
        if expected_set_at is not None and (read_reservation() or {}).get("set_at") != expected_set_at:
            return False
        existed = path.is_file()
        path.unlink(missing_ok=True)
        return existed
    except OSError:
        return False


def active_reservation(games) -> dict | None:
    """The reservation if it still holds (an expired one is deleted); `games` = running commercial games.
    Seeing a game refreshes `last_game`, which the grace after its exit is measured from."""
    marker = read_reservation()
    now = time.time()
    state = reservation_state(marker, now, bool(games))
    if state == "expired":
        release_gpu(expected_set_at=marker.get("set_at"))
    elif state == "active" and games:
        # The game it waited for is here: from now on it is a RUNNING reservation, so it lapses
        # RESERVE_GRACE_S["running"] after the game is last seen -- the launch grace is only for
        # a game that has not appeared yet.
        marker["last_game"], marker["kind"] = now, "running"
        _write_json(state_dir() / RESERVATION_FILE, marker)
    return marker if state == "active" else None


def gpu_free():
    """(free MiB, GPU name) of the NVIDIA GPU CUDA would use with the most free memory; None without
    nvidia-smi (then the free-VRAM check is skipped, and `status` says so)."""
    exe = shutil.which("nvidia-smi")
    if not exe:
        return None
    try:
        out = subprocess.run([exe, "--query-gpu=index,uuid,name,memory.total,memory.used",
                              "--format=csv,noheader,nounits"],
                             capture_output=True, encoding="utf-8", errors="replace", timeout=15).stdout
    except (OSError, subprocess.SubprocessError):
        return None
    return pick_gpu(out or "", os.environ.get("CUDA_VISIBLE_DEVICES"))


def load_config(root=None, ignore_env=False):
    """(config, where-or-why). Reads no network: a machine without the install stays untouched.

    The MACHINE config (written by `install`) is the one every joined repo and the hook share. A
    source checkout's legacy `.claude/local-llm.json` (version 1) is read only when there is none.
    `ignore_env`: the hook's view -- the per-shell switch never turns the machine guard off."""
    if not ignore_env and disabled_by_env(os.environ):
        return None, "disabled for this shell (CLAUDE_LOCAL_LLM=off)"
    mc = machine_config_path()
    if mc.is_file():
        cfg = _read_json(mc)
        if not isinstance(cfg, dict) or not str(cfg.get("model") or "").strip():
            return None, f'{mc.name} (the machine install) has no "model"; re-run install'
        return cfg, str(mc)
    if is_machine_copy():
        return None, "this machine has not installed it (install --model TAG)"
    for p in config_candidates(pathlib.Path(root) if root else ROOT):
        if p.is_file():
            try:
                cfg = json.loads(p.read_text(encoding="utf-8"))
            except (OSError, ValueError) as e:
                return None, f"unreadable {p.name}: {e}"
            if not isinstance(cfg, dict) or not str(cfg.get("model") or "").strip():
                return None, f'{p.name} has no "model"'
            return cfg, str(p)
    return None, "this machine has not installed it (install --model TAG)"


class Probe:
    def __init__(self, cfg):
        self.cfg = cfg or {}
        self.url = str(self.cfg.get("url") or DEFAULT_URL).rstrip("/")
        self.num_ctx = int(self.cfg.get("num_ctx") or NUM_CTX)
        self.keep_alive = str(self.cfg.get("keep_alive") or KEEP_ALIVE)
        self.system_prompt, self.exempt = system_prompt_of(self.cfg), exempt_of(self.cfg)
        self.state, self.reason, self.entry, self.loaded = "disabled", "", None, False

    @property
    def name(self) -> str:
        return (self.entry or {}).get("name") or str(self.cfg.get("model") or "")

    def run(self):
        try:
            tags = http_json(self.url + "/api/tags")
        except Exception as e:                      # noqa: BLE001 -- any failure means "absent"
            self.state, self.reason = "absent", f"no Ollama at {self.url} ({type(e).__name__})"
            return self
        self.entry = find_model(tags, self.cfg["model"])
        if not self.entry:
            have = ", ".join(m.get("name", "?") for m in tags.get("models") or []) or "none"
            self.state, self.reason = "no-model", f"{self.cfg['model']} not pulled (have: {have})"
            return self
        self.state, self.loaded = "ready", self.is_loaded()
        return self

    def is_loaded(self) -> bool:
        try:
            ps = http_json(self.url + "/api/ps")
        except Exception:                           # noqa: BLE001
            return False
        return find_model(ps, self.name) is not None

    def model_info(self) -> dict:
        """GGUF metadata (/api/show `model_info`) -- what the VRAM need is computed from; {} if unavailable."""
        if getattr(self, "_info", None) is None:
            try:
                self._info = http_json(self.url + "/api/show", {"model": self.name}, timeout=SETUP_TIMEOUT).get(
                    "model_info") or {}
            except Exception:                        # noqa: BLE001
                self._info = {}
        return self._info

    def vram_need(self) -> tuple[int, str]:
        return vram_need_mib(int((self.entry or {}).get("size") or 0), self.model_info(), self.num_ctx,
                             int(self.cfg.get("min_free_vram_mb") or 0))

    def unload(self, wait_s: float = UNLOAD_WAIT_S, deadline=None) -> str:
        """unloaded | pending | failed. keep_alive 0, then watch /api/ps (never trust size_vram).

        ⚠ `pending` is NOT a failure. Measured 2026-09-26: Ollama DEFERS the unload until any request
        in flight -- another session's -- has finished, then frees the model within ~0.5 s. The old
        code reported that as "STILL LOADED"."""
        post_timeout = UNLOAD_TIMEOUT if deadline is None else max(0.5, min(UNLOAD_TIMEOUT, deadline - time.monotonic()))
        try:
            http_json(self.url + "/api/generate", {"model": self.name, "keep_alive": 0}, timeout=post_timeout)
        except Exception:                           # noqa: BLE001
            return "failed"
        stop = time.monotonic() + wait_s
        if deadline is not None:
            stop = min(stop, deadline)
        while self.is_loaded():
            if time.monotonic() >= stop:
                return "pending"
            time.sleep(0.25)
        self.loaded = False
        return "unloaded"


def pending_note() -> str:
    """Who the deferred unload is waiting for."""
    leases = other_leases()
    if leases:
        return f"Ollama frees it when the request in flight ends ({describe_leases(leases)})"
    return ("Ollama frees it when the request in flight ends (not this helper's: `ollama run`, another "
            "app, or a LAN client)")


def available_or_exit(quiet=False) -> Probe:
    cfg, why = load_config()
    if cfg is None:
        if not quiet:
            print(f"local-llm: disabled -- {why}", file=sys.stderr)
        sys.exit(2)
    p = Probe(cfg).run()
    if p.state != "ready":
        if not quiet:
            print(f"local-llm: {p.state} -- {p.reason}", file=sys.stderr)
        sys.exit(2)
    return p


def guard(p: Probe):
    """None when the GPU may be used, else the refusal. Unloads if it finds a game.

    Checked before EVERY request (each --chunked slice too), which is what closes the cross-session
    gap: another session reserves the GPU before its game's process exists, and this session's next
    request sees the reservation instead of loading the model back."""
    p.loaded = p.is_loaded()                         # never a cached flag: a request of OURS may have loaded it
    games = commercial_games(running_images(), p.exempt)   # (an overflowing slice does), another session may
    if games:                                        # have unloaded it
        reserve_gpu("running", f"running: {', '.join(games)}")
        active_reservation(games)                    # stamps last_game
        if p.loaded:
            p.unload()
        return f"a commercial game is running ({', '.join(games)}); its VRAM is not ours"
    marker = active_reservation(games)
    if marker:
        if p.loaded:
            p.unload()
        return (f"the GPU is reserved for a game ({marker.get('reason')}); `release` clears it if that "
                f"launch was abandoned")
    if not p.loaded:
        gpu = gpu_free()
        need, detail = p.vram_need()
        if gpu is not None and gpu[0] < need:
            return (f"{gpu[0]} MiB VRAM free on {gpu[1]}, a cold load needs {need} MiB ({detail}) -- something "
                    f"else holds the GPU (a game whose exe does not look like a UE shipping build?)")
    return None


# ── Subcommands ──────────────────────────────────────────────────────────────────────────────────
def cmd_status(args) -> int:
    cfg, why = load_config()
    if cfg is None:
        info = {"state": "disabled", "reason": why}
    else:
        p = Probe(cfg).run()
        info = {"state": p.state, "reason": p.reason, "model": p.name, "url": p.url,
                "num_ctx": p.num_ctx, "loaded": p.loaded}
        if p.state == "ready":
            games = commercial_games(running_images(), p.exempt)
            marker = active_reservation(games)
            info["commercial_games"] = games
            info["gpu_reserved"] = marker.get("reason") if marker else None
            leases = other_leases()
            info["in_use_by"] = ([{k: r.get(k) for k in ("pid", "action", "cwd", "started")} for r in leases]
                                 if args.json else (describe_leases(leases) or None))
            gpu = gpu_free()
            need, detail = p.vram_need()
            info["vram"] = (f"{gpu[1]}: {gpu[0]} MiB free, a cold load needs {need} MiB ({detail})" if gpu
                            else f"no nvidia-smi: the free-VRAM check is skipped (a cold load needs ~{need} MiB)")
    info.update(install_health())
    if args.json:
        print(json.dumps(info))
    else:
        print("  ".join(f"{k}={v}" for k, v in info.items() if v not in ("", None)))
    return 0 if info["state"] == "ready" else 2


def install_health() -> dict:
    """What `status` adds about the machine install: where the config came from, the installed version,
    whether the ONE hook points at the machine copy, and -- run from a source checkout -- whether the
    installed copy is behind this one."""
    out = {"install": None, "version": None, "hook": None, "update": None}
    if machine_config_path().is_file():
        out["install"] = "machine"
        out["version"] = (_read_json(machine_config_path()) or {}).get("version")
    elif not is_machine_copy() and any(p.is_file() for p in config_candidates(ROOT)):
        out["install"] = "repo (version 1: run install)"
    settings, _ = _load_settings(user_settings_path())
    target = hook_target(settings or {})
    if out["install"] == "machine":
        interp = hook_interpreter(settings or {})
        out["hook"] = ("MISSING -- run install" if not target
                       else f"points at {target} -- run install" if not _same_path(pathlib.Path(target), machine_script())
                       else f"interpreter missing ({interp}) -- run install" if not _interpreter_ok(interp)
                       else "ok")
    if (out["install"] == "machine" and not is_machine_copy() and machine_script().is_file()
            and machine_script().read_bytes() != SCRIPT.read_bytes()):
        rel = compare_revisions((existing_machine_config() or {}).get("source_time"), helper_revision(ROOT))
        out["update"] = {"newer": "this checkout's helper is NEWER than the installed copy -- run install",
                         "older": "this checkout is OLDER than the installed copy -- pull; do not install from here",
                         "same": "differs from the installed copy at the same revision (local edits?)",
                         }.get(rel, "differs from the installed copy -- run install if this checkout is the newer")
    return out


def hook_interpreter(settings) -> str | None:
    for g in ((settings or {}).get("hooks") or {}).get("PreToolUse") or []:
        for h in g.get("hooks") or []:
            if _is_our_hook(h):
                return str(h.get("command") or "")
    return None


def _interpreter_ok(cmd) -> bool:
    return bool(cmd) and (os.path.isfile(cmd) or shutil.which(cmd) is not None)


def cmd_warm(args) -> int:
    p = available_or_exit()
    refusal = guard(p)
    if refusal:
        print(f"local-llm: refused -- {refusal}", file=sys.stderr)
        return 3
    t0 = time.perf_counter()
    with Lease("warm"):
        try:
            _post_with_retry(p.url + "/api/generate", {"model": p.name, "prompt": "", "keep_alive": p.keep_alive,
                                                       "options": {"num_ctx": p.num_ctx}}, GENERATE_TIMEOUT,
                             precheck=lambda: guard(p))
        except Refused as e:
            print(f"local-llm: refused -- {e}", file=sys.stderr)
            return 3
    print(f"local-llm: {p.name} loaded (num_ctx={p.num_ctx}, keep_alive={p.keep_alive}) "
          f"in {time.perf_counter() - t0:.1f}s")
    return 0


def cmd_unload(args) -> int:
    cfg, why = load_config()
    if cfg is None:
        print(f"local-llm: disabled -- {why}")
        return 0
    return _unload_and_report(Probe(cfg).run(), args.wait)


def _unload_and_report(p: Probe, wait_s: float) -> int:
    if p.state != "ready" or not p.loaded:
        print(f"local-llm: nothing to unload ({p.state}{', not loaded' if p.state == 'ready' else ''})")
        return 0
    result = p.unload(wait_s)
    if result == "unloaded":
        print(f"local-llm: {p.name} unloaded")
        return 0
    if result == "pending":
        print(f"local-llm: {p.name} unload PENDING after {wait_s:g}s -- {pending_note()}. "
              f"Re-run with a longer --wait to block until it is free.")
        return EXIT_UNLOAD_PENDING
    print(f"local-llm: could not ask Ollama to unload {p.name} -- check `ollama ps`")
    return 1


def cmd_reserve(args) -> int:
    """Before something that will start a commercial game the hook cannot see coming (a rig): reserve
    the GPU machine-wide, then unload. Every session's next request is refused until the reservation
    lapses (RESERVE_GRACE_S after the game's exit) or `release` clears it."""
    cfg, why = load_config()
    if cfg is None:
        print(f"local-llm: disabled -- {why}")
        return 0
    reserve_gpu("launch", args.reason or "reserved by `reserve`")
    print(f"local-llm: GPU reserved for a game ({args.reason or 'reserved by `reserve`'}) -- every "
          f"session's LLM requests are refused until it lapses or `release`")
    return _unload_and_report(Probe(cfg).run(), args.wait)


def cmd_release(args) -> int:
    had = release_gpu()
    print("local-llm: GPU reservation released" if had else "local-llm: no GPU reservation to release")
    return 0


class ContextOverflow(Exception):
    def __init__(self, tokens: int):
        super().__init__(f"request is {tokens} tokens")
        self.tokens = tokens


class Refused(Exception):
    pass


def _chat(p: Probe, user: str, think: bool) -> dict:
    """One request. `truncate: false` turns Ollama's silent over-window cut into ContextOverflow."""
    try:
        return _post_with_retry(p.url + "/api/chat", {
            "model": p.name, "stream": False, "think": bool(think), "keep_alive": p.keep_alive,
            "truncate": False,
            "messages": [{"role": "system", "content": p.system_prompt},
                         {"role": "user", "content": user}],
            "options": {"num_ctx": p.num_ctx, "temperature": TEMPERATURE, "num_predict": OUTPUT_RESERVE},
        }, GENERATE_TIMEOUT, precheck=lambda: guard(p))
    except urllib.error.HTTPError as e:
        tokens = parse_overflow(e.code, e.read().decode("utf-8", errors="replace"))
        if tokens is None:
            raise
        raise ContextOverflow(tokens) from None


def _stats(r: dict) -> str:
    gen_s = (r.get("eval_duration") or 0) / 1e9
    rate = f"{(r.get('eval_count') or 0) / gen_s:.0f} tok/s" if gen_s else "n/a"
    return (f"[local-llm] load {(r.get('load_duration') or 0) / 1e9:.1f}s, prompt "
            f"{r.get('prompt_eval_count')} tok (untruncated), answer {r.get('eval_count')} tok @ {rate}")


def _answer_slices(p: Probe, prompt: str, name: str, first: int, last: int, text: str, think: bool,
                   cpt: float, depth: int = 0):
    """Yield (first, last, response) for one slice; on overflow re-split it at the MEASURED rate."""
    refusal = guard(p)                               # a game may start mid-batch
    if refusal:
        raise Refused(refusal)
    request = build_request(prompt, [(f"FILE: {name} (lines {first}-{last})", text)])
    try:
        r = _chat(p, request, think)
    except ContextOverflow as e:
        if depth >= MAX_RESPLIT_DEPTH or not text:
            raise
        measured = len(request) / e.tokens if e.tokens > 0 else cpt / 2
        cpt = min(cpt, measured) * RESPLIT_SAFETY
        budget = p.num_ctx - OUTPUT_RESERVE - estimate_tokens(p.system_prompt + prompt, cpt) - CHUNK_SLACK_TOKENS
        subs = split_lines(text, max(budget, 256), cpt)
        if len(subs) < 2:                            # never retry the same slice unchanged
            half = max(1, len(text) // 2)
            subs = [(1, 1, text[:half]), (1, 1, text[half:])]
        print(f"[local-llm] {name} lines {first}-{last} was {e.tokens} tokens; re-split into "
              f"{len(subs)} at {cpt:.2f} chars/token", file=sys.stderr)
        for a, b, t in subs:
            yield from _answer_slices(p, prompt, name, first + a - 1, first + b - 1, t, think, cpt, depth + 1)
        return
    p.loaded = True
    yield first, last, r


def cmd_ask(args) -> int:
    prompt = args.prompt or ""
    if args.prompt_file:
        prompt = (prompt + "\n\n" if prompt else "") + pathlib.Path(args.prompt_file).read_text(
            encoding="utf-8", errors="replace")
    if not prompt.strip():
        print("local-llm: --prompt or --prompt-file is required", file=sys.stderr)
        return 1
    files = [(f, pathlib.Path(f).read_text(encoding="utf-8", errors="replace")) for f in args.file or []]
    p = available_or_exit()
    refusal = guard(p)
    if refusal:
        print(f"local-llm: refused -- {refusal}", file=sys.stderr)
        return 3
    with Lease("ask --chunked" if args.chunked else "ask"):
        return _ask(p, args, prompt, files)


def _ask(p: Probe, args, prompt: str, files) -> int:
    budget = p.num_ctx - OUTPUT_RESERVE - estimate_tokens(p.system_prompt + prompt) - CHUNK_SLACK_TOKENS

    if not args.chunked:
        whole = build_request(prompt, [(f"FILE: {name}", text) for name, text in files])
        if len(whole) > p.num_ctx * HOPELESS_CHARS_PER_TOKEN:
            print(f"local-llm: {len(whole)} chars cannot fit a {p.num_ctx}-token window. Pre-slice it "
                  f"(grep/sed) or pass --chunked.", file=sys.stderr)
            return 4
        try:
            r = _chat(p, whole, args.think)
        except ContextOverflow as e:
            print(f"local-llm: the server counted {e.tokens} tokens; the window is {p.num_ctx}. Pre-slice "
                  f"it (grep/sed) or pass --chunked.", file=sys.stderr)
            return 4
        print(r["message"]["content"])
        print(_stats(r), file=sys.stderr)
        return 0

    if budget < 1024:
        print("local-llm: the prompt alone nearly fills the window", file=sys.stderr)
        return 4
    n = 0
    try:
        for name, body in files or [("(prompt only)", "")]:
            for first, last, text in split_lines(body, budget) or [(0, 0, "")]:
                for a, b, r in _answer_slices(p, prompt, name, first, last, text, args.think,
                                              ASCII_CHARS_PER_TOKEN):
                    n += 1
                    print(f"### slice {n} -- {name} lines {a}-{b}\n{r['message']['content']}\n", flush=True)
                    print(_stats(r), file=sys.stderr)
    except Refused as e:
        print(f"local-llm: stopped after {n} slice(s) -- {e}", file=sys.stderr)
        return 3
    except ContextOverflow as e:
        print(f"local-llm: a slice stayed over the window after {MAX_RESPLIT_DEPTH} re-splits "
              f"({e.tokens} tokens) -- pre-slice the input", file=sys.stderr)
        return 4
    return 0


def cmd_hook(args) -> int:
    """Never blocks and never fails the tool call: every path exits 0, inside HOOK_BUDGET_S. It ignores the
    per-shell CLAUDE_LOCAL_LLM switch: that stops a shell USING the model, never the machine's guard."""
    deadline = time.monotonic() + HOOK_BUDGET_S
    try:
        raw = sys.stdin.buffer.read().decode("utf-8", errors="replace")
        command = hook_command(raw)
        cfg, _ = load_config(ignore_env=True)
        if cfg is None:
            return 0
        exempt = exempt_of(cfg)
        launch = command_launches_commercial(command, exempt)
        if launch:                                   # reserve FIRST: other sessions must not reload it
            reserve_gpu("launch", f"launch in a command: {launch}")
        procs = None
        if (state_dir() / RESERVATION_FILE).is_file():   # keep the grace measured from the last game seen
            procs = [n for _, n in running_processes()]
            active_reservation(commercial_games(procs, exempt))
        p = Probe(cfg)
        if not launch and p.url.startswith(("http://127.0.0.1", "http://localhost")):
            procs = procs if procs is not None else [n for _, n in running_processes()]
            if procs and not ollama_running(procs):  # no local server: nothing can be loaded, skip the probe
                return 0
        p.entry = {"name": str(cfg["model"])}
        if not p.is_loaded():                        # the common case: one /api/ps round trip
            return 0
        try:
            p.entry = find_model(http_json(p.url + "/api/ps"), p.name) or p.entry
        except Exception:                            # noqa: BLE001
            pass
        if launch:
            why = f"launch in this command: {launch}"
        else:
            games = commercial_games(procs if procs is not None else running_images(), exempt)
            marker = None if games else active_reservation([])
            if games:
                reserve_gpu("running", f"running: {', '.join(games)}")
                why = f"running: {', '.join(games)}"
            elif marker:                             # resident under a reservation: evict it before the game boots
                why = f"GPU reserved: {marker.get('reason')}"
            else:
                return 0
        result = p.unload(HOOK_UNLOAD_WAIT_S, deadline=deadline)
        if result == "unloaded":
            msg = f"local-llm: unloaded {p.name} to free VRAM ({why})"
        elif result == "pending":
            msg = f"local-llm: unload of {p.name} PENDING ({why}) -- {pending_note()}; GPU reserved"
        else:
            msg = f"local-llm: could not unload {p.name} ({why}) -- check `ollama ps`"
        print(json.dumps({"systemMessage": msg}))
    except Exception:                                # noqa: BLE001
        pass
    return 0


def _load_settings(path: pathlib.Path):
    """(settings, error). A missing file is {}; an unparseable one is an error -- never overwritten."""
    if not path.is_file():
        return {}, None
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as e:
        return None, f"{path} is not readable JSON ({e}); fix it first"
    return (data, None) if isinstance(data, dict) else (None, f"{path} is not a JSON object")


def _save_settings(path: pathlib.Path, before: dict, after: dict, backup: bool) -> None:
    """Write only on a real change; the user-level file is backed up once, before our first edit."""
    if after == before:
        return
    if not after and path.name == SETTINGS_REL.name:
        path.unlink(missing_ok=True)                 # a settings.local.json that only ever held ours
        return
    original = path.read_bytes() if path.is_file() else None
    if backup and original is not None:
        bak = path.with_name(path.name + USER_SETTINGS_BACKUP_SUFFIX)
        if not bak.exists():
            shutil.copy2(path, bak)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(settings_text(after), encoding="utf-8", newline=newline_of(original))


def _cleanup_legacy_repo_optin(main: pathlib.Path) -> None:
    """Version 1 kept the opt-in in the checkout (.claude/local-llm.json) and, before that, the hook in
    its settings.local.json. The machine install replaces both."""
    (main / CONFIG_REL).unlink(missing_ok=True)
    local_path = main / SETTINGS_REL
    local_before, err = _load_settings(local_path)
    if not err:
        _save_settings(local_path, local_before, remove_hook(local_before), backup=False)


def _print_joined(prefix: str = "") -> None:
    repos = joined_repos()
    if not repos:
        print(f"{prefix}joined repos: none")
        return
    print(f"{prefix}joined repos ({len(repos)}):")
    for r in repos:
        hint = {"outdated": "  <- run `join` there to take the installed skill",
                "missing": "  <- its skill file is gone: `join` there, or `leave` it",
                "unreadable": "  <- its skill file is not UTF-8: `join --force` there rewrites it",
                "source-differs": "  <- the source's skill differs from the installed one: `install` from it if newer",
                }.get(r["status"], "")
        print(f"  {r['status']:14} {r['path']}{hint}")


def cmd_install(args) -> int:
    """Install (or update) THE machine copy: helper + skill + config in the machine dir, one hook in the
    user-level settings pointing at it. Run from a source checkout to install or update the helper (its
    OWN skill goes with it -- a worktree's, not its main checkout's); run from the machine copy to change
    settings only (model / URL / window / exempt fixtures / VRAM floor). An OLDER source checkout than the
    installed copy is refused unless --force: it would silently downgrade every joined repo."""
    source = None if is_machine_copy() else ROOT
    old = existing_machine_config() or {}
    if not old and source:
        legacy, _ = load_config(root=main_checkout(source), ignore_env=True)
        old = legacy or {}
    mine = helper_revision(source) if source else None
    if source and compare_revisions(old.get("source_time"), mine) == "older" and not args.force:
        print(f"local-llm: refused -- this checkout's helper (commit time {mine}) is OLDER than the installed "
              f"copy ({old.get('source_time')}); pull first, or --force to downgrade on purpose", file=sys.stderr)
        return 3
    model = args.model or old.get("model")
    if not model:
        print("local-llm: install needs --model TAG", file=sys.stderr)
        return 1
    url = (args.url or old.get("url") or DEFAULT_URL).rstrip("/")
    try:
        tags = http_json(url + "/api/tags", timeout=SETUP_TIMEOUT)
    except Exception as e:                           # noqa: BLE001
        print(f"local-llm: no Ollama answers at {url} ({type(e).__name__})", file=sys.stderr)
        return 2
    entry = find_model(tags, model)
    if not entry:
        have = ", ".join(m.get("name", "?") for m in tags.get("models") or []) or "none"
        print(f"local-llm: {model} is not pulled here (have: {have})", file=sys.stderr)
        return 2
    user_path = user_settings_path()
    user_before, err = _load_settings(user_path)
    if err:
        print(f"local-llm: {err}", file=sys.stderr)
        return 1

    cfg = {k: v for k, v in old.items() if k != "version"}
    cfg["model"] = entry["name"]
    if url != DEFAULT_URL:
        cfg["url"] = url
    else:
        cfg.pop("url", None)
    if args.num_ctx:
        cfg["num_ctx"] = args.num_ctx
    if args.exempt:
        cfg["exempt_prefixes"] = sorted({*exempt_of(cfg), *(x.strip().lower() for x in args.exempt if x.strip())})
    if args.min_free_vram_mb is not None:
        if args.min_free_vram_mb > 0:
            cfg["min_free_vram_mb"] = args.min_free_vram_mb
        else:
            cfg.pop("min_free_vram_mb", None)
    if source:
        cfg["source_time"] = mine
    skill_src = install_skill_source(source) if source else machine_skill()
    skill_text = skill_src.read_text(encoding="utf-8").replace("\r\n", "\n") if skill_src.is_file() else None
    install_machine(SCRIPT, skill_text, cfg)
    python = shutil.which("py") or sys.executable
    _save_settings(user_path, user_before, merge_hook(user_before, python, machine_script().as_posix()),
                   backup=True)
    if source:
        _cleanup_legacy_repo_optin(main_checkout(source))
        register_repo(main_checkout(source))
    shown = {k: v for k, v in cfg.items() if k not in ("system_prompt", "source_time")}
    print(f"local-llm: installed version {HELPER_VERSION} for this machine -- {json.dumps(shown)}\n"
          f"  helper, skill and config: {state_dir()}\n"
          f"  PreToolUse hook ({HOOK_MATCHER}) in the USER-level Claude Code settings -> the machine copy, "
          f"so it guards every session here, in every repo (backed up once as *{USER_SETTINGS_BACKUP_SUFFIX}).\n"
          f"  The hook takes effect in a NEW session (or after opening /hooks once).\n"
          f"  A repo joins with `join` (run in it), leaves with `leave`. Settings change the same way:\n"
          f"  `install --min-free-vram-mb N` etc., run from the machine copy -- no repo file changes.")
    _print_joined("  ")
    return 0


def cmd_uninstall(args) -> int:
    """Remove the machine install and its hook. Joined repos keep their skill, which is inert without the
    install (it answers "not installed"); `--leave-all` removes those too."""
    user_path = user_settings_path()
    user_before, err = _load_settings(user_path)
    if err:
        print(f"local-llm: {err}", file=sys.stderr)
        return 1
    if args.leave_all:
        for r in joined_repos():
            print(f"  leave {r['path']}: {leave_repo(pathlib.Path(r['path']))}")
    _save_settings(user_path, user_before, remove_hook(user_before), backup=True)
    uninstall_machine()
    if not is_machine_copy():
        _cleanup_legacy_repo_optin(main_checkout(ROOT))
    print("local-llm: uninstalled -- the machine copy, its config and the user-level hook are gone.")
    _print_joined("  ")
    return 0


def cmd_setup(args) -> int:
    """Version-1 name, kept: `setup --model TAG` installs, `setup --remove` uninstalls."""
    if args.remove:
        args.leave_all = False
        return cmd_uninstall(args)
    return cmd_install(args)


def _skill_to_hand_out():
    """The skill a repo joins with: the installed copy's."""
    try:
        return machine_skill().read_text(encoding="utf-8") if machine_skill().is_file() else None
    except (OSError, UnicodeDecodeError):
        return None


def _repo_or_complain(arg):
    repo = resolve_repo(arg)
    if repo is None:
        print("local-llm: not inside a git repo -- run it in the repo, or name it with --repo PATH", file=sys.stderr)
    return repo


def cmd_join(args) -> int:
    repo = _repo_or_complain(args.repo)
    if repo is None:
        return 1
    if not machine_script().is_file():
        print("local-llm: this machine has no install yet -- run `install --model TAG` from a source "
              "checkout first", file=sys.stderr)
        return 2
    skill = _skill_to_hand_out()
    if skill is None:
        print("local-llm: the machine install carries no skill; re-run `install` from a source checkout",
              file=sys.stderr)
        return 1
    refusal = join_refusal(repo, args.force)
    if refusal:
        print(f"local-llm: not joined -- {refusal}", file=sys.stderr)
        return 1
    result = join_repo(repo, skill)
    print(f"local-llm: {result} -- {repo}\n"
          f"  wrote {SKILL_REL.as_posix()} (no machine facts: it calls the machine copy through "
          f"$LOCALAPPDATA). Commit it to share it -- it is inert on a machine without the install.\n"
          f"  To leave: `leave` in that repo.")
    return 0


def cmd_leave(args) -> int:
    repo = _repo_or_complain(args.repo)
    if repo is None:
        return 1
    result = leave_repo(repo)
    if result == "refused-source":
        print(f"local-llm: {repo} ships the helper itself; its skill is source, not a join. To stop using "
              f"the helper on this machine: `uninstall`.", file=sys.stderr)
        return 1
    if result == "refused-foreign":
        print(f"local-llm: {SKILL_REL.as_posix()} in {repo} is not this helper's skill -- left untouched.",
              file=sys.stderr)
        return 1
    print(f"local-llm: {result} -- {repo}" + (f" (removed {SKILL_REL.as_posix()})" if result == "left" else ""))
    return 0


def cmd_repos(args) -> int:
    _print_joined()
    return 0


def selftest() -> int:
    checks = []

    def ok(name, cond):
        checks.append((name, bool(cond)))

    tags = {"models": [{"name": "example4:latest"}, {"name": "example-12b-it-Q8_0:latest"},
                       {"name": "hf.co/org/repo:Q4_K_M"}]}
    ok("normalize adds :latest", normalize_model("example-12b-it-Q8_0") == "example-12b-it-q8_0:latest")
    ok("normalize keeps a tag", normalize_model("qwen:7b") == "qwen:7b")
    ok("normalize: a registry path's colon is the tag", normalize_model("hf.co/org/repo") == "hf.co/org/repo:latest")
    ok("find: bare tag matches :latest", (find_model(tags, "example-12b-it-Q8_0") or {}).get("name") == "example-12b-it-Q8_0:latest")
    ok("find: case-insensitive", find_model(tags, "EXAMPLE-12B-IT-q8_0:LATEST") is not None)
    ok("find: example4 is not the 12B", (find_model(tags, "example4") or {}).get("name") == "example4:latest")
    ok("find: absent -> None", find_model(tags, "example-12b-it-Q4_0") is None)
    ok("find: no models key", find_model({}, "x") is None)

    for image, want in [("Elliot-Win64-Shipping.exe", True), ("FactoryGameSteam-Win64-Shipping.exe", True),
                        ("Game-WinGDK-Shipping.exe", True), ("Some Game-Win64-Shipping.exe", True),
                        ("DumperTest-Win64-Shipping.exe", False), ("DumperTest51-Win64-Shipping.exe", False),
                        ("DumperTest58-Win64-DebugGame.exe", False), ("dumpertest-win64-shipping.exe", False),
                        ("DumperTest.exe", False), ("Elliot.exe", False), ("ollama.exe", False),
                        ("Code.exe", False), ("Elliot-Win64-Shipping.exe.bak", False),
                        ("EOSOverlayRenderer-Win64-Shipping.exe", False)]:
        ok(f"process {image} -> {want}", is_commercial_process(image) is want)

    for cmd, want in [
        ('"C:\\Program Files (x86)\\Steam\\steam.exe" -applaunch 526870', True),
        ("steam -silent -applaunch 3483510", True),
        ("start steam://rungameid/1363080", True),
        ('start "" "C:\\Games\\Elliot\\Binaries\\Win64\\Elliot-Win64-Shipping.exe"', True),
        ("taskkill /IM Elliot-Win64-Shipping.exe", False),  # a kill is not a launch (and now reserves the GPU)
        ("taskkill /f /im Elliot-Win64-Shipping.exe && echo done", False),
        ("py tools/verify/launch_dumpertest.py shipping", False),
        ("py tools/verify/inject.py --name DumperTest", False),
        ('"D:\\Out\\DumperTest51\\Binaries\\Win64\\DumperTest51-Win64-Shipping.exe" -windowed', False),
        ("git log --oneline -5", False),
        ("grep -n applaunch docs/handover-2026-08-22.md", False),
        ("", False),
    ]:
        ok(f"command {cmd[:48]!r} -> {want}", (command_launches_commercial(cmd) is not None) is want)

    good = json.dumps({"tool_name": "Bash", "tool_input": {"command": "steam.exe -applaunch 1"}})
    ok("hook input: the command field", hook_command(good) == "steam.exe -applaunch 1")
    ok("hook input: malformed JSON is scanned raw, not ignored",
       command_launches_commercial(hook_command('{"command":"\\"C:\\Steam\\steam.exe\\" -applaunch 3"')) is not None)
    ok("hook input: empty and non-object inputs", hook_command("") == "" and hook_command("[1]") == "[1]")
    ok("command: the EOS overlay helper is not a game launch",
       command_launches_commercial("taskkill /IM EOSOverlayRenderer-Win64-Shipping.exe") is None)

    def fake_post(results):
        calls = []

        def post(url, payload, timeout):
            calls.append(url)
            r = results[len(calls) - 1]
            if isinstance(r, int):
                raise urllib.error.HTTPError(url, r, "fake", None, io.BytesIO(b"{}"))
            return r
        return post, calls

    post, calls = fake_post([500, {"done": True}])
    ok("retry: one transient 500 (CUDA init) is retried once",
       _post_with_retry("u", {}, 1, post=post, sleep=lambda s: None) == {"done": True} and len(calls) == 2)
    for seq, label in (([400], "a 400 is not retried"), ([500, 500], "a second 500 propagates")):
        post, calls = fake_post(seq)
        try:
            _post_with_retry("u", {}, 1, post=post, sleep=lambda s: None)
            raised = None
        except urllib.error.HTTPError as e:
            raised = e.code
        ok(f"retry: {label}", raised == seq[-1] and len(calls) == len(seq))

    listing = ('"System Idle Process","0","Services","0","8 K"\r\n'
               '"Elliot-Win64-Shipping.exe","4242","Console","1","3,210,000 K"\r\n'
               '"DumperTest51-Win64-Shipping.exe","62872","Console","1","900,000 K"\r\n'
               '"py.exe","1","Console","1","9 K"\r\n')
    images = parse_tasklist(listing)
    ok("tasklist: exe rows only", images == ["Elliot-Win64-Shipping.exe", "DumperTest51-Win64-Shipping.exe", "py.exe"])
    ok("tasklist: the no-match INFO line is dropped",
       parse_tasklist("INFO: No tasks are running which match the specified criteria.\r\n") == [])
    ok("games: DumperTest exempt, commercial kept", commercial_games(images) == ["Elliot-Win64-Shipping.exe"])

    ok("estimate: ascii at the planning rate", 1700 <= estimate_tokens("a" * 3500) <= 1800)
    ok("estimate: a measured rate is honoured", 1000 <= estimate_tokens("a" * 1500, 1.5) <= 1001)
    ok("estimate: CJK is one per char", 1000 <= estimate_tokens("中" * 1000) <= 1001)
    text = "".join(f"line {i:05d} 0x7FF6{i:08X}\n" for i in range(1, 5001))
    chunks = split_lines(text, 2000)
    ok("split: more than one chunk", len(chunks) > 1)
    ok("split: every chunk under budget", all(estimate_tokens(c) <= 2000 for _, _, c in chunks))
    ok("split: lossless and in order", "".join(c for _, _, c in chunks) == text)
    ok("split: line numbers contiguous", chunks[0][0] == 1 and chunks[-1][1] == 5000
       and all(a[1] + 1 == b[0] for a, b in zip(chunks, chunks[1:])))
    giant = "x" * 50000
    parts = split_lines(giant, 1000)
    ok("split: an over-budget line is cut, not dropped", "".join(c for _, _, c in parts) == giant
       and all(estimate_tokens(c) <= 1000 for _, _, c in parts))
    ok("split: empty input -> no chunks", split_lines("", 1000) == [])
    tighter = split_lines(text, 2000, 1.5)
    ok("split: a lower measured rate makes more, smaller slices", len(tighter) > len(chunks)
       and "".join(c for _, _, c in tighter) == text)

    measured = ('{"error":"{\\"error\\":{\\"code\\":400,\\"message\\":\\"request (134020 tokens) exceeds the '
                'available context size (32768 tokens), try increasing it\\",\\"type\\":\\"exceed_context_size_error\\"}}"}')
    ok("overflow: the measured 0.34.4 body parses to its count", parse_overflow(400, measured) == 134020)
    ok("overflow: a 400 that says exceed but gives no count -> -1", parse_overflow(400, "context exceeded") == -1)
    ok("overflow: another 400 is not an overflow", parse_overflow(400, '{"error":"model not found"}') is None)
    ok("overflow: a 500 is not an overflow", parse_overflow(500, measured) is None)
    nl = chr(10)
    req = build_request("What is on the last line?", [("FILE: a.log", "x" + nl), ("FILE: b.log", "y" + nl)])
    ok("request: content first, the task LAST", req.index("FILE: a.log") < req.index("FILE: b.log") < req.index("=== TASK ===")
       and req.rstrip().endswith("What is on the last line?"))
    ok("request: no files -> the task alone", build_request("Q", []) == "=== TASK ===" + nl + "Q")

    main = pathlib.Path("D:/Repo")
    wt = main / ".claude" / "worktrees" / "blissful-x"
    ok("config: main checkout reads its own", config_candidates(main) == [main / CONFIG_REL])
    ok("config: a worktree also reads the main checkout's", config_candidates(wt)[-1] == main / CONFIG_REL)
    ok("config: main_checkout of a worktree", main_checkout(wt) == main)

    foreign = {"permissions": {"allow": ["Bash(git *)"]},
               "hooks": {"PreToolUse": [{"matcher": "Bash", "hooks": [{"type": "command", "command": "echo hi"}]}],
                         "Stop": [{"hooks": [{"type": "command", "command": "echo bye"}]}]}}
    merged = merge_hook(foreign, "py", "D:/Repo/tools/llm/ollama_local.py")
    ok("merge: foreign keys kept", merged["permissions"] == foreign["permissions"] and merged["hooks"]["Stop"] == foreign["hooks"]["Stop"])
    ok("merge: foreign PreToolUse kept", merged["hooks"]["PreToolUse"][0] == foreign["hooks"]["PreToolUse"][0])
    ok("merge: ours appended once", sum(_is_our_hook(h) for g in merged["hooks"]["PreToolUse"] for h in g["hooks"]) == 1)
    again = merge_hook(merged, "py", "E:/Moved/tools/llm/ollama_local.py")
    ours = [h for g in again["hooks"]["PreToolUse"] for h in g["hooks"] if _is_our_hook(h)]
    ok("merge: idempotent, and a moved checkout replaces the path", len(ours) == 1 and ours[0]["args"][-2].startswith("E:/Moved"))
    ok("merge: exec form, no shell", ours[0]["command"] == "py" and ours[0]["args"][-1] == "hook")
    user = {"hooks": {"PreToolUse": []}, "enabledPlugins": {}, "language": "繁體中文, English"}
    merged_user = merge_hook(user, "py", "D:/R/tools/llm/ollama_local.py")
    ok("merge: the user's key ORDER survives (hooks stays first)", list(merged_user) == list(user))
    ok("settings text keeps non-ASCII literal, not \\u-escaped", "繁體中文" in settings_text(merged_user)
       and json.loads(settings_text(merged_user)) == merged_user)
    # guard() with the world faked in-process: no game, no reservation, 9.5 GB free, and a Probe whose
    # `loaded` flag is stale -- the model it is about to judge was loaded by this same process.
    saved = (globals()["running_images"], globals()["active_reservation"], globals()["gpu_free"])
    try:
        globals().update(running_images=lambda: [], active_reservation=lambda games: None,
                         gpu_free=lambda: (9579, "GPU"))
        stale = Probe({"model": "m"})
        stale.entry, stale.loaded, stale._info = {"name": "m", "size": 13_309_873_056}, False, {}
        stale.is_loaded = lambda: True
        ok("guard: its OWN loaded model is not 'something else holding the GPU'", guard(stale) is None)
        cold = Probe({"model": "m"})
        cold.entry, cold.loaded, cold._info = {"name": "m", "size": 13_309_873_056}, False, {}
        cold.is_loaded = lambda: False
        ok("guard: a cold model with too little VRAM free is still refused", guard(cold) is not None)
    finally:
        globals().update(running_images=saved[0], active_reservation=saved[1], gpu_free=saved[2])

    ok("newline: an LF file stays LF, a CRLF one CRLF, a new one LF",
       newline_of(b'{\n  "a": 1\n}') == "\n" and newline_of(b'{\r\n  "a": 1\r\n}') == "\r\n" and newline_of(None) == "\n")
    ok("merge: runs through the missing-script bootstrap", ours[0]["args"][:2] == ["-c", HOOK_BOOTSTRAP]
       and ours[0]["args"][-2] == "E:/Moved/tools/llm/ollama_local.py")
    legacy = {"hooks": {"PreToolUse": [{"matcher": HOOK_MATCHER, "hooks": [{"type": "command", "command": "py",
              "args": ["D:/R/tools/llm/ollama_local.py", "hook"]}]}]}}
    ok("remove: the pre-bootstrap exec form is still recognised as ours", remove_hook(legacy) == {})
    saved_argv = sys.argv
    try:
        sys.argv = ["-c", "Z:/no/such/dir/tools/llm/ollama_local.py", "hook"]
        exec(compile(HOOK_BOOTSTRAP, "<bootstrap>", "exec"), {"__name__": "bootstrap"})
        ran_clean = True
    except BaseException:                            # noqa: BLE001 -- SystemExit(2) is the failure
        ran_clean = False
    finally:
        sys.argv = saved_argv
    ok("bootstrap: a missing script is a silent no-op, never exit 2 (which BLOCKS the tool call)", ran_clean)

    procs = parse_tasklist_procs('"Elliot-Win64-Shipping.exe","4242","Console","1","3 K"\r\n"py.exe","77","C","1","9 K"\r\n')
    ok("tasklist: pid and image", procs == [(4242, "Elliot-Win64-Shipping.exe"), (77, "py.exe")])

    now = 10_000.0
    leases = [{"pid": 1, "started": now - 5}, {"pid": 2, "started": now - 5},
              {"pid": 3, "started": now - LEASE_MAX_AGE_S - 1}, {"pid": 99, "started": now - 1}, "junk"]
    ok("leases: only other LIVE, FRESH processes count",
       [r["pid"] for r in live_leases(leases, now, alive_pids={1, 3, 99}, own_pid=99)] == [1])

    for marker, running, want in [
        (None, False, "none"), (None, True, "none"),
        ({"kind": "launch", "set_at": now - 60}, False, "active"),
        ({"kind": "launch", "set_at": now - RESERVE_GRACE_S["launch"] - 1}, False, "expired"),
        ({"kind": "running", "set_at": now - RESERVE_GRACE_S["running"] - 1}, False, "expired"),
        ({"kind": "running", "set_at": now - 999_999}, True, "active"),
        ({"kind": "???", "set_at": now - 60}, False, "active"),
    ]:
        ok(f"reservation {marker and marker['kind']} game={running} -> {want}",
           reservation_state(marker, now, running) == want)
    ok("remove: restores the foreign settings exactly", remove_hook(again) == foreign)
    ok("remove: an only-ours file loses the hooks key", remove_hook(merge_hook({}, "py", "D:/R/tools/llm/ollama_local.py")) == {})
    ok("remove: a foreign hook that merely mentions the script is kept",
       remove_hook({"hooks": {"PreToolUse": [{"hooks": [{"type": "command", "command": "py",
                    "args": ["D:/R/tools/llm/ollama_local.py", "status"]}]}]}})["hooks"]["PreToolUse"] != [])

    ok("env: UE5CE_LLM=off disables", disabled_by_env({DISABLE_ENV: "off"}) and disabled_by_env({DISABLE_ENV: " OFF "}))
    ok("env: unset or on does not", not disabled_by_env({}) and not disabled_by_env({DISABLE_ENV: "on"}))

    # ── Machine install + join / leave: several repos, ONE helper, ONE config, ONE hook ───────────────
    try:
        _selftest_cooperative(ok)
    except Exception as e:                           # noqa: BLE001 -- a missing API is a failed control
        ok(f"cooperative: the install / join / leave API runs ({type(e).__name__}: {e})", False)
    try:
        _selftest_review(ok)
    except Exception as e:                           # noqa: BLE001
        ok(f"review: the controls run ({type(e).__name__}: {e})", False)

    failed = [n for n, good in checks if not good]
    for n in failed:
        print(f"FAIL  {n}")
    print(f"ollama_local selftest: {len(checks) - len(failed)}/{len(checks)} controls passed")
    return 1 if failed else 0


def _selftest_cooperative(ok) -> None:
    """Every repo on a machine shares one installed helper, one config and one hook; a repo joins by
    receiving the skill and leaves by losing it. Run against a temp state dir -- never the real one."""
    import tempfile

    ok("env: the generic CLAUDE_LOCAL_LLM=off disables too", disabled_by_env({"CLAUDE_LOCAL_LLM": "off"}))
    ok("exempt: defaults keep DumperTest exempt", exempt_of({}) == DEFAULT_EXEMPT_PREFIXES
       and not is_commercial_process("DumperTest58-Win64-Shipping.exe"))
    ok("exempt: a machine config can add its own fixtures",
       exempt_of({"exempt_prefixes": ["MyFixture"]}) == ("myfixture",)
       and not is_commercial_process("MyFixture-Win64-Shipping.exe", exempt=("myfixture",))
       and is_commercial_process("MyFixture-Win64-Shipping.exe"))
    ok("exempt: reaches the launch and game checks",
       command_launches_commercial('"X\\MyFixture-Win64-Shipping.exe"', exempt=("myfixture",)) is None
       and commercial_games(["MyFixture-Win64-Shipping.exe"], exempt=("myfixture",)) == [])
    ok("prompt: neutral by default, overridable per machine",
       "Unreal" not in system_prompt_of({}) and system_prompt_of({"system_prompt": "S"}) == "S")

    saved = os.environ.get(HOME_ENV)
    with tempfile.TemporaryDirectory() as td:
        os.environ[HOME_ENV] = str(pathlib.Path(td) / "state")         # NOT named claude-local-llm, on purpose
        try:
            src = pathlib.Path(td) / "Src"
            (src / "tools" / "llm").mkdir(parents=True)
            src_script = src / "tools" / "llm" / "ollama_local.py"
            src_script.write_text("# helper\n", encoding="utf-8")
            skill = "---\nname: local-llm\n---\nuse $LOCALAPPDATA/claude-local-llm/ollama_local.py\n"
            (src / ".claude").mkdir()
            (src / CONFIG_REL).write_text('{"model": "legacy"}', encoding="utf-8")

            install_machine(src_script, skill, {"model": "m"})
            cfg_disk = _read_json(machine_config_path()) or {}
            ok("install: one machine copy, its skill, and the config",
               machine_script().read_text(encoding="utf-8") == "# helper\n"
               and machine_skill().read_text(encoding="utf-8") == skill and cfg_disk.get("model") == "m")
            ok("install: the config records the helper version", cfg_disk.get("version") == HELPER_VERSION)
            cfg, _ = load_config(root=src)
            ok("config: the machine install wins over a repo's legacy opt-in", (cfg or {}).get("model") == "m")

            b = pathlib.Path(td) / "RepoB"
            (b / ".claude").mkdir(parents=True)
            (b / ".claude" / "settings.json").write_text("{}", encoding="utf-8")
            ok("join: writes the skill", join_repo(b, skill) == "joined"
               and repo_skill_path(b).read_text(encoding="utf-8") == skill)
            ok("join: again is a no-op", join_repo(b, skill) == "current")
            ok("join: a changed skill is an update", join_repo(b, skill + "v2\n") == "updated")
            listed = {r["path"]: r["status"] for r in joined_repos()}
            ok("repos: a joined repo whose skill differs from the installed one is outdated",
               listed.get(str(b)) == "outdated")
            ok("leave: removes the skill", leave_repo(b) == "left" and not repo_skill_path(b).exists())
            ok("leave: keeps the repo's other .claude content",
               (b / ".claude" / "settings.json").is_file() and not (b / ".claude" / "skills").exists())
            ok("leave: again is not-joined", leave_repo(b) == "not-joined")
            ok("leave: unregistered", str(b) not in {r["path"] for r in joined_repos()})
            ok("leave: the helper's own source repo is refused", leave_repo(src) == "refused-source")

            legacy = merge_hook({}, "py", "D:/R/tools/llm/ollama_local.py")
            moved = merge_hook(legacy, "py", machine_script().as_posix())
            ours = [h for g in moved["hooks"]["PreToolUse"] for h in g["hooks"] if _is_our_hook(h)]
            ok("hook: the machine copy replaces a repo-pointing entry (still exactly one)",
               len(ours) == 1 and hook_target(moved) == machine_script().as_posix())
            ok("hook: the machine entry is recognised as ours", remove_hook(moved) == {})
            ok("hook: none -> no target", hook_target({}) is None)

            uninstall_machine()
            ok("uninstall: the machine copy, skill and config are gone",
               not machine_script().exists() and not machine_skill().exists()
               and not machine_config_path().exists())
            cfg, _ = load_config(root=src)
            ok("config: without the install a legacy repo opt-in still reads", (cfg or {}).get("model") == "legacy")
        finally:
            if saved is None:
                os.environ.pop(HOME_ENV, None)
            else:
                os.environ[HOME_ENV] = saved


def _selftest_review(ok) -> None:
    """wf_b18865b3-513's findings (runtime + cooperative) and the VRAM estimate, each as a control. The world is
    faked in-process: a temp state dir and settings file, a fake Ollama, a fake process list."""
    import contextlib
    import tempfile

    ok("matcher: launch-capable computer-use tools run the hook too",
       re.fullmatch(HOOK_MATCHER, "mcp__computer-use__left_click") is not None
       and re.fullmatch(HOOK_MATCHER, "Bash") is not None
       and re.fullmatch(HOOK_MATCHER, "mcp__computer-use__screenshot") is None)
    for cmd in ("py tools/verify/inject.py --exe=DumperTest51-Win64-Shipping.exe",
                "run(DumperTest51-Win64-Shipping.exe)", "x --target=DumperTest-Win64-Shipping.exe"):
        ok(f"exempt: a fixture after punctuation is not a launch: {cmd[:40]}", command_launches_commercial(cmd) is None)
    ok("exempt: a real game after '=' still is", command_launches_commercial("x --exe=Elliot-Win64-Shipping.exe") is not None)
    ok("ollama process: seen / not seen",
       ollama_running(["ollama.exe", "x.exe"]) and ollama_running(["ollama app.exe"]) and not ollama_running(["x.exe"]))

    # VRAM need from the model's own metadata (a public 12B GGUF: 48 layers, 40 sliding 8 KV heads x 256,
    # 8 global 1 KV head x 512, window 1024). Measured VRAM over idle: 8k 14287, 32k 14767, 64k 15407 MiB.
    info = {"general.architecture": "g4", "g4.block_count": 48, "g4.attention.head_count": 16,
            "g4.attention.head_count_kv": [8, 8, 8, 8, 8, 1] * 8, "g4.attention.key_length": 512,
            "g4.attention.value_length": 512, "g4.attention.key_length_swa": 256,
            "g4.attention.value_length_swa": 256, "g4.attention.sliding_window": 1024,
            "g4.attention.sliding_window_pattern": ([True] * 5 + [False]) * 8, "g4.embedding_length": 3840}
    ok("kv: sliding + global layers from metadata (320 + 512 MiB at 32k)", abs(kv_cache_mib(info, 32768) - 832) < 1)
    for ctx, measured in ((8192, 14287), (32768, 14767), (65536, 15407)):
        need, _ = vram_need_mib(13_309_873_056, info, ctx)
        ok(f"need at {ctx}: above the measured {measured} by the buffer, not more", 0 < need - measured <= VRAM_BUFFER_MIB + 100)
    ok("kv: a dense model (32 layers x 8 KV heads x 128) = 4096 MiB at 32k",
       kv_cache_mib({"general.architecture": "l", "l.block_count": 32, "l.attention.head_count": 32,
                     "l.attention.head_count_kv": 8, "l.embedding_length": 4096}, 32768) == 4096)
    ok("kv: an integer sliding pattern (every Nth layer global)",
       kv_cache_mib({"general.architecture": "g", "g.block_count": 6, "g.attention.head_count_kv": 1,
                     "g.attention.key_length": 128, "g.attention.value_length": 128,
                     "g.attention.sliding_window": 1024, "g.attention.sliding_window_pattern": 6}, 32768)
       == (5 * 1024 + 1 * 32768) * 256 * 2 / 1048576)
    need, how = vram_need_mib(8_000_000_000, {}, 32768)
    ok("need: no metadata falls back to a conservative KV, and says so", "fallback" in how and need > 8_000_000_000 / 1048576 + 4000)
    ok("need: the machine floor wins when higher", vram_need_mib(13_309_873_056, info, 32768, 16384)[0] == 16384)
    csv_ = "0, GPU-aa, NVIDIA GeForce RTX X Laptop GPU, 24463, 117\n1, GPU-bb, NVIDIA RTX Y, 8192, 100\n"
    ok("gpu: the NVIDIA GPU with the most free memory", pick_gpu(csv_) == (24346, "NVIDIA GeForce RTX X Laptop GPU"))
    ok("gpu: CUDA_VISIBLE_DEVICES by index or UUID", pick_gpu(csv_, "1") == (8092, "NVIDIA RTX Y")
       and pick_gpu(csv_, "GPU-bb") == (8092, "NVIDIA RTX Y"))
    ok("gpu: none listed -> None", pick_gpu("", None) is None)

    ok("revisions: newer / older / same / unknown",
       compare_revisions(100, 200) == "newer" and compare_revisions(200, 100) == "older"
       and compare_revisions(100, 100) == "same" and compare_revisions(None, 100) == "unknown")

    saved_env = {k: os.environ.get(k) for k in (HOME_ENV, USER_SETTINGS_ENV, "CLAUDE_LOCAL_LLM", "UE5CE_LLM")}
    saved_g = {k: globals()[k] for k in ("http_json", "running_processes", "gpu_free", "_post_with_retry")}
    saved_stdin = sys.stdin
    with tempfile.TemporaryDirectory() as td:
        tdp = pathlib.Path(td)
        os.environ[HOME_ENV] = str(tdp / "state")
        os.environ[USER_SETTINGS_ENV] = str(tdp / "settings.json")
        OLL = (5, "ollama.exe")
        world = {"resident": True, "posts": [], "procs": [(4, "x.exe"), OLL], "free": 20000}

        def fake_http(url, payload=None, timeout=PROBE_TIMEOUT):
            if url.endswith("/api/ps"):
                return {"models": [{"name": "m:latest", "size": 13_309_873_056}] if world["resident"] else []}
            if url.endswith("/api/tags"):
                return {"models": [{"name": "m:latest", "size": 13_309_873_056}]}
            if url.endswith("/api/show"):
                return {"model_info": info}
            if url.endswith("/api/generate") and payload and payload.get("keep_alive") == 0:
                world["posts"].append(("unload", timeout))
                world["resident"] = False
                return {}
            world["posts"].append(("gen", timeout))
            world["resident"] = True
            return {"done": True}

        def run_hook(command):
            sys.stdin = type("S", (), {"buffer": io.BytesIO(json.dumps({"tool_input": {"command": command}}).encode())})()
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                rc = cmd_hook(None)
            return rc, out.getvalue()

        try:
            globals().update(http_json=fake_http, running_processes=lambda: list(world["procs"]),
                             gpu_free=lambda: (world["free"], "GPU"))
            install_machine(pathlib.Path(__file__), "skill claude-local-llm/ollama_local.py\n", {"model": "m"})

            os.environ["CLAUDE_LOCAL_LLM"] = "off"
            world.update(resident=True, posts=[])
            rc, _ = run_hook("steam.exe -applaunch 526870")
            ok("[ENV-OFF] the per-shell switch never disables the machine guard: launch still reserves + unloads",
               rc == 0 and read_reservation() is not None and any(k == "unload" for k, _ in world["posts"]))
            os.environ.pop("CLAUDE_LOCAL_LLM", None)
            release_gpu()

            world.update(resident=True, posts=[], procs=[(9, "Elliot-Win64-Shipping.exe"), OLL])
            stale = Probe({"model": "m"}).run()
            stale.loaded = False
            ok("[STALE-LOADED] guard re-reads /api/ps: a resident model beside a game is unloaded",
               guard(stale) is not None and any(k == "unload" for k, _ in world["posts"]))
            release_gpu()

            world.update(resident=True, posts=[], procs=[(4, "x.exe"), OLL])
            reserve_gpu("launch", "test")
            rc, msg = run_hook("git status")
            ok("[RES-UNLOAD] a model resident under an active reservation is unloaded by any hook",
               any(k == "unload" for k, _ in world["posts"]) and "reserv" in msg.lower())
            world.update(resident=True, posts=[])
            p = Probe({"model": "m"}).run()
            ok("[RES-UNLOAD] guard's reservation refusal unloads a resident model too",
               guard(p) is not None and any(k == "unload" for k, _ in world["posts"]))
            release_gpu()

            now = time.time()
            ok("[GRACE] the grace runs from the last time a game was SEEN, not from set_at",
               reservation_state({"kind": "running", "set_at": now - 600, "last_game": now - 5}, now, False) == "active"
               and reservation_state({"kind": "running", "set_at": now - 600, "last_game": now - 600}, now, False) == "expired")
            reserve_gpu("launch", "test")
            m0 = read_reservation()
            active_reservation(["Elliot-Win64-Shipping.exe"])
            ok("[GRACE] seeing a game refreshes last_game", (read_reservation() or {}).get("last_game", 0) >= m0["set_at"])
            ok("[GRACE] ...and turns a launch reservation into a running one (30 s after exit, not 180)",
               (read_reservation() or {}).get("kind") == "running")
            world.update(resident=False, posts=[], procs=[(9, "Elliot-Win64-Shipping.exe"), OLL])
            m1 = dict(read_reservation() or {}); m1["last_game"] = time.time() - 100
            _write_json(state_dir() / RESERVATION_FILE, m1)
            run_hook("git status")
            ok("[GRACE] a hook refreshes last_game while a marker exists, model loaded or not",
               (read_reservation() or {}).get("last_game", 0) > time.time() - 10)
            release_gpu()

            fresh = {"kind": "launch", "set_at": time.time() + 5}
            _write_json(state_dir() / RESERVATION_FILE, fresh)
            ok("[STATE-RACE] release with a stale expected set_at keeps a fresh marker",
               release_gpu(expected_set_at=1.0) is False and read_reservation() is not None)
            release_gpu()
            ld = state_dir() / LEASE_DIRNAME
            ld.mkdir(parents=True, exist_ok=True)
            (ld / "777.json").write_text(json.dumps({"pid": 777, "started": time.time()}), encoding="utf-8")
            globals()["running_processes"] = lambda: []
            other_leases()
            ok("[STATE-RACE] an empty process list never prunes leases", (ld / "777.json").is_file())
            globals()["running_processes"] = lambda: list(world["procs"])

            calls = []

            def post500(url, payload, timeout):
                calls.append(url)
                if len(calls) == 1:
                    raise urllib.error.HTTPError(url, 500, "x", None, io.BytesIO(b"{}"))
                return {"done": True}
            try:
                saved_g["_post_with_retry"](("u"), {}, 1, post=post500, sleep=lambda s: None,
                                            precheck=lambda: "reserved")
                refused = False
            except Refused:
                refused = True
            except TypeError:
                refused = False
            ok("[RETRY] a 500 is not retried when the guard now refuses", refused and len(calls) == 1)

            world.update(resident=True, posts=[])
            p = Probe({"model": "m"}).run()
            p.unload(5, deadline=time.monotonic() + 2)
            ok("[HOOK-BUDGET] an unload POST never outlives the caller's deadline",
               all(t <= 2.5 for k, t in world["posts"] if k == "unload"))

            world.update(resident=False, posts=[], free=15500, procs=[(4, "x.exe"), OLL])
            p = Probe({"model": "m", "min_free_vram_mb": 16384}).run()
            ok("[VRAM] the machine floor refuses a load with less free", guard(p) is not None)
            world.update(free=17000)
            p = Probe({"model": "m", "min_free_vram_mb": 16384}).run()
            ok("[VRAM] ...and allows one with more", guard(p) is None)

            os.environ["CLAUDE_LOCAL_LLM"] = "off"
            ok("[INSTALL-ENV] install reads the machine config whatever the shell switch",
               (existing_machine_config() or {}).get("model") == "m")
            os.environ.pop("CLAUDE_LOCAL_LLM", None)

            wt = tdp / "Main" / ".claude" / "worktrees" / "wt1"
            ok("[WORKTREE] install takes the skill from the checkout whose helper it installs",
               install_skill_source(wt) == wt / SKILL_REL)

            python_missing = merge_hook({}, str(tdp / "NoSuch" / "py.EXE"), machine_script().as_posix())
            pathlib.Path(os.environ[USER_SETTINGS_ENV]).write_text(json.dumps(python_missing), encoding="utf-8")
            ok("[INTERPRETER] status flags a hook whose interpreter is gone",
               "interpreter" in str(install_health().get("hook")))

            src = tdp / "Src"
            (src / "tools" / "llm").mkdir(parents=True)
            (src / "tools" / "llm" / "ollama_local.py").write_text("#", encoding="utf-8")
            ok("[JOIN-SOURCE] the source repo cannot join (its skill is source)", join_refusal(src, False) is not None)
            ok("[JOIN-GHOST] a directory that does not exist cannot join", join_refusal(tdp / "Nope", False) is not None)
            ok("[JOIN-HOME] the home directory cannot join", join_refusal(pathlib.Path.home(), False) is not None)
            foreign = tdp / "Foreign"
            repo_skill_path(foreign).parent.mkdir(parents=True)
            repo_skill_path(foreign).write_text("my own LM Studio notes\n", encoding="utf-8")
            ok("[FOREIGN] join refuses to overwrite a skill that is not ours", join_refusal(foreign, False) is not None
               and join_refusal(foreign, True) is None)
            ok("[FOREIGN] leave refuses to delete a skill that is not ours", leave_repo(foreign) == "refused-foreign"
               and repo_skill_path(foreign).is_file())
            u16 = tdp / "U16"
            join_repo(u16, "skill claude-local-llm/ollama_local.py\n")
            repo_skill_path(u16).write_bytes("skill".encode("utf-16"))
            try:
                st = {r["path"]: r["status"] for r in joined_repos()}.get(str(u16))
            except UnicodeDecodeError:
                st = "crash"
            ok("[UNREADABLE] a skill that is not UTF-8 is reported, not a crash", st == "unreadable")
        except Exception as e:                       # noqa: BLE001 -- a missing API is a failed control
            ok(f"review controls ran ({type(e).__name__}: {e})", False)
        finally:
            sys.stdin = saved_stdin
            globals().update(saved_g)
            for k, v in saved_env.items():
                if v is None:
                    os.environ.pop(k, None)
                else:
                    os.environ[k] = v


def main(argv) -> int:
    if "--selftest" in argv:
        return selftest()
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("status")
    s.add_argument("--json", action="store_true")
    sub.add_parser("warm")
    un = sub.add_parser("unload")
    un.add_argument("--wait", type=float, default=UNLOAD_WAIT_S,
                    help="seconds to wait for a request in flight (another session's) to finish")
    rs = sub.add_parser("reserve", help="reserve the GPU for a game launch, machine-wide, then unload")
    rs.add_argument("--wait", type=float, default=UNLOAD_WAIT_S)
    rs.add_argument("--reason")
    sub.add_parser("release", help="clear a GPU reservation (a launch that was abandoned)")
    sub.add_parser("hook")
    a = sub.add_parser("ask")
    a.add_argument("--prompt")
    a.add_argument("--prompt-file")
    a.add_argument("--file", action="append", help="read by this script, so the text never enters the caller's context")
    a.add_argument("--chunked", action="store_true", help="ask once per window-sized slice of the files")
    a.add_argument("--think", action="store_true")
    for name, hlp in (("install", "install / update THE machine copy + config + user-level hook"),
                      ("setup", "version-1 name of install (--remove = uninstall)")):
        u = sub.add_parser(name, help=hlp)
        u.add_argument("--model")
        u.add_argument("--url")
        u.add_argument("--num-ctx", type=int)
        u.add_argument("--exempt", action="append", help="a test-fixture exe prefix that may share the GPU")
        u.add_argument("--min-free-vram-mb", type=int,
                       help="refuse a cold load with less VRAM free than this (0 clears; a machine setting)")
        u.add_argument("--force", action="store_true", help="install even from a checkout OLDER than the installed copy")
        u.add_argument("--remove", action="store_true", help=argparse.SUPPRESS if name == "install" else None)
    un2 = sub.add_parser("uninstall", help="remove the machine install and its hook")
    un2.add_argument("--leave-all", action="store_true", help="also remove the skill from every joined repo")
    for name, hlp in (("join", "give a repo the skill (default: the repo you are in)"),
                      ("leave", "take the skill back out of a repo")):
        j = sub.add_parser(name, help=hlp)
        j.add_argument("--repo")
        if name == "join":
            j.add_argument("--force", action="store_true", help="replace a skill file at that path that is not ours")
    sub.add_parser("repos", help="list the joined repos and whether their skill is current")
    args = ap.parse_args(argv)
    if args.cmd == "install" and args.remove:
        args.cmd = "setup"
    return {"status": cmd_status, "warm": cmd_warm, "unload": cmd_unload, "reserve": cmd_reserve,
            "release": cmd_release, "hook": cmd_hook, "ask": cmd_ask, "setup": cmd_setup,
            "install": cmd_install, "uninstall": cmd_uninstall, "join": cmd_join, "leave": cmd_leave,
            "repos": cmd_repos}[args.cmd](args)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
