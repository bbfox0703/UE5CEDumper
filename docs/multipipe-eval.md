# Multi-pipe IPC Evaluation (UI / DLL / CE contention)

> **Status (2026-07-23, build 2324): the conclusion still holds, but §1–§6's REASON for it does
> not. Read §10 first.** This document's core claim — that DLL-side serial-dispatch head-of-line
> blocking is what makes the UI lag — was reasoned and never measured. §10 measured it on two games
> across both engine generations and **refuted it**: the dispatcher is idle ~70% of wall-clock, and
> the worst single dispatch out of 24,178 was **14.3 ms**. There is no head-of-line spike to remove,
> so **Phase 1 = WON'T DO** (§10.1). Treat §1–§6 as the historical argument, not as current fact.
>
> **Phase 0 + Phase 1 were SHIPPED then REVERTED** (build 1840, §8) after in-game testing regressed
> badly — Phase 0's scan thread-priority drop starved scans ~20× when the game was busy, and it is
> **not in the tree today** (`grep -rn SetThreadPriority dll/src/` = 0 hits; a stale index row said
> otherwise until 2026-08-05). What DID ship is the two-connection **lane split** (PR #396).
> **TL;DR:** Do **NOT** add more named pipes. The reported lag is **head-of-line blocking on
> the DLL's single serial command loop**. But both attempted fixes regressed: (a) the Phase 0
> scan thread-priority drop **starved scans ~20× when the game is busy**; (b) Phase 1's
> off-thread heavy worker **deadlocks on the synchronous pipe handle** — a `WriteFile` cannot
> proceed while the read thread is parked in `ReadFile` on the same non-overlapped handle. CE
> `.CT` mailbox is already off the pipe and isolated.

This document evaluates the user request: *"should the UI↔DLL and CE-Lua↔DLL IPC use
multiple pipes? classify UI functions; guarantee the CE `.CT` invoke mailbox stays
responsive while the UI does heavy work."*

-----

## 1. Current architecture — two independent channels

### Channel 1 — UI named pipe (`Fern`)

- **One** pipe instance: [`CreateNamedPipeW(..., /*maxInstances=*/1, ...)`](../dll/src/Fern.cpp:496),
  `PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT` (byte-mode, blocking), 64 KB buffers.
- [`HandleClient`](../dll/src/Fern.cpp:610) is a **strictly serial** loop:
  `ReadLine → Tot::ResetPerCommand → DispatchCommand (SYNCHRONOUS, blocks the thread
  for the whole command) → WriteLine`. While one command runs, the pipe thread cannot
  even **read** the next request line.
- A `MonitorLoop` thread peeks the in-flight handle every ~200 ms (only while
  `m_commandInFlight`) to trip `Tot` cooperative-cancel on a mid-command disconnect.
- Background threads (`RunScan`, `RunRescan`, per-address `watch`) run **outside** the
  dispatch loop and report via [`PushEvent`](../dll/src/Fern.cpp:655) — the only async
  write path, serialized with responses through `m_writeMutex`.

### UI client — already a request-id multiplexer (important)

- **One** [`PipeClient`](../ui/UE5DumpUI/App.axaml.cs:53) created once and shared by every
  panel/VM (manual composition root, no DI container — AOT).
- [`SendAsync`](../ui/UE5DumpUI/Services/PipeClient.cs:98) stamps a monotonic `id`, parks a
  `TaskCompletionSource` in a `ConcurrentDictionary` keyed by `id`, and holds the
  `SemaphoreSlim _writeLock` **only around the byte-write**
  ([acquire L127 → release L134](../ui/UE5DumpUI/Services/PipeClient.cs:127)); the response is
  awaited **after** the lock is released ([`return await tcs.Task`, L149](../ui/UE5DumpUI/Services/PipeClient.cs:149)).
  `ReadLoopAsync` demultiplexes responses back to the waiting TCS by `id`.
- **Consequence:** the client can have many requests outstanding at once. The
  `_writeLock` only prevents interleaved JSON bytes; it does **not** serialize the
  request/response round-trip. The client is **not** the bottleneck.

> ⚠️ A common mis-diagnosis (an automated reviewer made it too): "the UI `_writeLock`
> makes a heavy command block light commands." This is **false** — the lock is released
> before the response await. Verified against the code above.

### Channel 2 — CE Lua / `.CT` mailbox (`Mimic`)

- CE **does not touch the named pipe**. It RPM/WPMs the
  [`g_invokeMailbox`](../dll/src/Mimic.h:198) shared-memory struct; a dedicated DLL poll
  thread reads the `volatile cmd` field every ~1 ms (lock-free) and dispatches.
- Stateful ops (`CMD_INVOKE`, some `CMD_TELEPORT`) block **that poll thread** on
  [`Stark::EnqueueInvoke`](../dll/src/Stark.h:50) `future.wait_for` until the **game thread**
  drains the queue. Pure-memory ops (`CMD_PROTECT` GodMode, `CMD_MOVEMENT`, read-only
  teleport/POV) never touch the game thread.
- The mailbox path shares **no mutex** with the Fern pipe path
  (`Wirbel::s_opMutex` / `Solitar::s_mutex` / `Laufen::s_mutex` / `Stark::s_queueMutex`
  are all independent of `m_pipeMutex` / `m_writeMutex`).

-----

## 2. Root cause of "Live Walker is slow during Snapshot"

**DLL-side head-of-line blocking, full stop.**

Snapshot capture is a stream of `snapshot_chunk` commands (~0.5–2 s each). While one
chunk is inside `DispatchCommand`, the pipe thread will not read the next line — so a
`walk_instance` (a sub-30 ms command) the UI fired immediately just sits in the OS pipe
buffer until the chunk finishes. Because chunks stream continuously, Live Walker is
effectively starved for the whole capture.

It is **not** the UI `_writeLock` (released immediately), **not** GObjects drift, **not**
the CE channel.

-----

## 3. CE mailbox responsiveness — what actually threatens it

Multiple pipes do **nothing** for the CE concern, because CE is not on the pipe.

| CE operation class | Exposure to UI heavy work |
|---|---|
| Pure-memory (GodMode/Solitar, Movement/Laufen, read-only pose/POV/marker) | **Immune.** No pipe, no game thread, independent mutex. |
| Game-thread-routed (`CMD_INVOKE` ProcessEvent; cursor/recall teleports) | Shares the **single `Stark` game-thread queue** with the UI's own game-thread ops (`invoke_function`, game-thread teleports) **and** is sensitive to **CPU starvation**: a parallel full-pool scan pinning every core delays the game tick, so a queued invoke can hit its timeout. |

**The fix is to throttle scan CPU, not to add pipes** (Phase 0), plus the existing
`set_invoke_timeout` safety valve.

-----

## 4. Options considered

| # | Option | Needs concurrent `DispatchCommand`? | Shared-state safety | Effort / Risk | Verdict |
|---|---|---|---|---|---|
| 1 | **Single pipe + non-blocking dispatch** (worker pool, heavy concurrency = 1) | No (1 heavy worker + light inline) | ✅ Safe — light commands read only write-once globals/own session; do not build class caches | L / med | **Recommended** |
| 2 | **N pipe instances** (fast/bulk lanes) | **Yes** | ❌ Two `DispatchCommand` race on `s_classContainerCache`/`s_classRefCache` (double-checked-lock) + `Ubel::WalkClassEx` | L / high | Same benefit, more danger |
| 3 | Per-purpose dedicated pipes | Yes (most) | ❌ Least safe | XL / high | Rejected |
| 4 | Only loosen UI `_writeLock` | No | n/a | S / low | **Non-fix** (bottleneck isn't here) |
| 5 | Move CE further off / confirm isolation | No | n/a | S / low | Already isolated; needs CPU guard only |

**Why Option 1 beats Option 2:** identical concurrency-safety boundary (one heavy op at
a time; light reads run concurrently), but the worker-pool reuses the existing UI `id`
multiplexer and one connection — Option 2 adds a second connection lifecycle, doubles
disconnect/cleanup (`StopAllWatches`/`DropAll` assume a single client), and a single
global `Tot` flag would cross-trip between lanes.

### The concurrency-safety crux (gates Option 2 / Phase 2)

> **Superseded 2026-11-06** (`[PIPE-PER-PROCESS-2026-11-06]`, re-read against the code). The class-cache race below is
> gone: `s_classContainerCache` / `s_classRefCache` take their mutex on every access and are never erased, and
> `Ubel::WalkClassEx` publishes with `try_emplace`. There is still no GObjects epoch, but every walk already races the
> game's GC with one client. The live cost of concurrent heavy commands is scan threads (min(16, cores − 2) each),
> which that plan's Stage B budgets.

The DLL is **not** safe to run two scan-family commands concurrently today:

- [`s_classContainerCache`](../dll/src/Aura.cpp:1784) and
  [`s_classRefCache`](../dll/src/Aura.cpp:2644) use a check-without-lock → build-outside-lock
  (`Ubel::WalkClassEx`) → insert-under-lock pattern → a data race when two threads miss
  simultaneously.
- GObjects/GNames reads **are** reentrant (write-once globals set at `Init`), but there is
  **no epoch/version stamp**, so two concurrent walks can straddle a live GObjects drift
  inconsistently.
- `Radar` `SessionManager`/`GroupSessionManager` are mutex-protected **per session**, but
  two distinct commands don't share that lock — they still race the Aura class caches.

Therefore any true-parallel design (Option 2/3, or raising the heavy-worker cap above 1)
**must first** make those caches thread-safe + add a GObjects epoch. Defer.

-----

## 5. Recommendation — phased

- **Phase 0 (S, low risk) — protect CE during heavy UI work.** SHIPPED.
  Throttle scan CPU so the game thread always wins scheduling; document CE isolation.
- **Phase 1 (M–L, med risk) — fix the UI contention.** Split DLL dispatch: light/control
  commands run inline on the read thread; heavyweight commands go to **one** worker thread
  and return their `id`-tagged response via the existing write-mutex path, so the read loop
  is free immediately. Requires **per-request cancellation** (today
  [`Tot::ResetPerCommand`](../dll/src/Fern.cpp:616) is global — a light command would clear a
  running heavy scan's cancel state) and generalizing the monitor's in-flight detection to
  "any worker active". The UI needs ~no protocol change (already `id`-muxed).
- **Phase 2 (L, optional — do not pursue speculatively) — true parallel heavy ops.** Only
  if users want two heavy operations at once: first make `s_classContainerCache` /
  `s_classRefCache` / `Ubel` caches thread-safe and add a GObjects epoch, **then** raise the
  heavy-worker cap.

-----

## 6. UI function classification (for the Phase 1 dispatch split)

The dividing rule that keeps the light lane safe to run concurrently with a heavy worker:
a command is **light/control** only if it (a) reads write-once globals or its own session,
and (b) does **not** call `Ubel::WalkClassEx` / build the class-metadata caches.

| Lane | Representative commands | Handling |
|---|---|---|
| **CONTROL / LIGHT** (~47) | `get_object_list`, `get_object`, `walk_instance`, `walk_class`, `read_array_elements`, `query_candidates`, `query_group_candidates`, all `teleport_*`, `set/get_god_mode`, `set/reset_movement_multiplier`, `set/get_mouse_cursor`, `get_current_target`, `get_pointers`, `get_offsets` | run **inline** on the read thread |
| **HEAVY** (~15) | `begin_value_scan`/`refine_value_scan`, `begin_group_scan`/`refine_group_scan`, `snapshot_chunk`, `find_instances`, `find_refs_to_uobject`, `find_path_from_gworld`, `search_properties`/`search_properties_batch`, `list_classes`/`list_all_functions`, `walk_world` (GWorld-null fallback) | dispatch to **1 worker**; `Tot`-cancellable; concurrency = 1 |
| **ASYNC (already)** | `rescan`, `trigger_scan` | begin + `*_status` poll; unchanged |
| **Special** | `invoke_function` (heavy **and** game-thread-routed, up to 30 s) | worker lane; shares the `Stark` queue with CE invoke (second game-thread contention axis) |

-----

## 7. What was shipped (then reverted)

- **Phase 0** (`Aura.cpp`): scan worker threads + the chunk-0 calling thread were dropped to
  `THREAD_PRIORITY_BELOW_NORMAL` during a parallel scan (RAII `ScopedScanPriority`), on top of
  the pre-existing `hardware_concurrency() − 2` cap.
- **Phase 1** (`Fern.cpp`/`.h`): `HandleClient` routed light commands inline and heavy commands
  to a single FIFO worker thread (`HeavyWorkerLoop`), with per-client cancel + disconnect drain.

Both were reverted to the pre-change baseline (the Pipe Activity log, §8, was kept).

-----

## 8. Postmortem — why Phase 0 + Phase 1 were reverted (build 1840, Elliot)

In-game testing surfaced three regressions; root-caused from the DLL pipe log + screenshots.

### 8.1 Phase 1 deadlocks on the synchronous pipe handle (the connect hang)

The server pipe is created **without `FILE_FLAG_OVERLAPPED`** (synchronous handle,
[Fern.cpp:496](../dll/src/Fern.cpp)). On a synchronous handle the Windows I/O manager
**serializes operations**: a pending `ReadFile` holds the file-object lock, so a concurrent
`WriteFile` on the same handle **blocks until the read completes**. Phase 1's whole premise —
the heavy worker writing a response while the read thread is parked in `ReadFile` waiting for
the next command — is therefore impossible: the worker's `WriteFile` hangs until *some new
command* arrives to release the read.

Proof from the log: on the first connect, `trigger_scan #5` was received (11:52:29.515), its
handler ran (`RunScan finished` 30.036), but **the response never reached the UI** — nothing
else was sent, so the read thread stayed parked, the worker's write stayed blocked, and the UI
hung until a manual disconnect. The second connect "worked" only because continuous user
traffic (object-tree loads, a `walk_world` click) kept releasing the read and flushing the
previous write. Classic intermittently-masked deadlock.

(The old serial loop never hit this: the read thread did its own writing, so read and write
were never outstanding at the same time. Watch-thread `PushEvent` writes technically could, but
were rare enough to slip by.)

**A correct Phase 1 requires overlapped/async pipe I/O** (or separate read/write handles), so
the read can be pending while the worker writes independently. That is a real rework of
`ReadLine`/`WriteLine`/`AcceptLoop` and must be tested in-game before shipping.

### 8.2 Phase 0 priority drop starves scans ~20× (snapshot 1–2 min → ~1 hour)

With a game (Elliot) running and saturating cores, `THREAD_PRIORITY_BELOW_NORMAL` scan threads
are preempted by the game's normal-priority threads and barely run. The snapshot crawled to **2%
in 57 s (ETA ~40 min)**. The `cores − 2` *count* headroom was already the right (and sufficient)
throttle; lowering *priority* on top of it was the mistake — it converts "leave some CPU for the
game" into "run only when the game is fully idle", which a live game never is.

### 8.3 Net

Even with the deadlock worked around, Phase 1 wouldn't have helped the snapshot case much: the
shared `m_writeMutex` re-serializes a multi-MB chunk write, and the **UI-side `ReadLoopAsync`
parses each multi-MB chunk single-threaded (~2.4 s each)**, blocking delivery of any interleaved
light response. The dominant snapshot bottlenecks are (a) scan-thread starvation [fixed by the
revert] and (b) UI-side chunk parse — *not* DLL dispatch serialization. Any future work should
target those, and Phase 1 should only be revisited with overlapped I/O.

### 8.4 Kept: Pipe Activity log (System tab)

Unrelated to the deadlock; UI-only. `IPipeClient.Activity` event → `PointerPanelViewModel`
newest-first ring buffer (cap **200**, Pause/Clear), coalesced one `Dispatcher.UIThread.Post`
per burst. Ironically it was the tool that made the deadlock obvious (the missing `←` reply).

> Note: §8.3 originally said "Phase 1 should only be revisited with overlapped I/O." That is
> superseded by **§9** — the sister repo `discrete` shows a **two-connection** design that gets
> the lane benefit **without** overlapped I/O, by giving each connection its own handle.

-----

## 9. Phase 1 REDO — discrete-style two-connection lane split (Path A)

> **STATUS: SHIPPED — MERGED to `main` via PR #396 (build ~1845), in-game verified on Elliot
> (§9.6 items 1–5 pass: connect, interactive-responsive-during-scan, value-search-mid-snapshot,
> disconnect-mid-scan, CE invoke under load).** Remaining low-priority verification: watch-event
> delivery + the single-lane-independent-drop edge (§9.7). Decisions taken: `maxInstances=3`;
> value-scan sessions dropped only when the **last** connection disconnects; the monitor trips
> the **global** `Tot` cancel for **any** broken in-flight connection (the DLL is lane-agnostic
> — a fast light command finishes before the 200 ms peek catches it, so in practice only a bulk
> scan is ever caught; the UI router reconnects **both** lanes on either drop so the cancel flag
> is reset cleanly); Pipe Activity entries are **lane-tagged** (I/B). See §9.8 for what landed.

Modeled on the sister repo **`D:\Github\discrete`**, which runs this in production. It supersedes
the reverted single-handle worker-pool.

### 9.1 Core idea

The UI opens **TWO** named-pipe client connections to the same server name; the DLL accepts
multiple instances and serves **each connection on its own thread with its own handle**:

- **Interactive lane** — light, fast, read-only browse commands (Live Walker, properties,
  teleport, godmode/movement, pagination over cached sessions).
- **Bulk lane** — heavy, long-running commands (snapshot, value/group scan, find_*,
  search_*, list_*, invoke).

Each connection stays **serial** within itself (read → dispatch → write on one thread, one
handle). The two connections run **independently** on two handles/threads.

**Why this avoids the reverted deadlock (§8.1):** the deadlock was a worker thread doing
`WriteFile` while the read thread was parked in `ReadFile` on the **same** synchronous handle.
Here, each handle is touched by exactly **one** thread doing read→write sequentially — there is
never a second thread writing the same handle. So **synchronous I/O is fine; no overlapped/
`FILE_FLAG_OVERLAPPED` rewrite is needed.** (discrete also uses overlapped I/O, but only because
its CE-Lua bridge opens *additional* bursty pipe clients; our CE uses the Mimic mailbox, not the
pipe, so two UI connections is all we need.)

**Why it's concurrency-safe:** the interactive lane only reads write-once GObjects globals +
Ubel's mutex-guarded caches + global (mutex-guarded) value-scan sessions — it **never** builds
the Aura `s_classContainerCache` / `s_classRefCache` (those live only in scan engines, all on
the bulk lane). The bulk lane runs scans **one at a time** (serial within its connection). So
two cache-builders never run concurrently — the same safety boundary §4 established, now with
NO deadlock. (discrete confirms: handlers are "read-mostly; concurrent invocation worst-case is
a duplicate cache fill, not corruption.")

### 9.2 Reference (discrete — proven template)

- Server: `dll/src/shared/PipeServer.cpp` — `kMaxPipeInstances = 4`, accept-then-detach a
  thread per client, each with its own handle (`FILE_FLAG_OVERLAPPED` there; we keep sync).
- Client: `ui/UnityDumpUI.Core/Services/BackendAdapter.cs` — two `PipeClient`s (`_pipe`
  interactive + `_bulk`), `BulkCommands` set + `PipeFor(cmd)` routing, `ConnectAsync` opens both.
- Per-connection cache guards: `dll/src/shared/ExportAPI.cpp` (mutex-guarded `s_cache`).

### 9.3 DLL changes (`Fern`)

1. **maxInstances**: `CreateNamedPipeW(..., /*maxInstances=*/3, ...)` (2 UI lanes + 1 reconnect
   transient). Keep `PIPE_TYPE_BYTE | PIPE_WAIT`, **no** `FILE_FLAG_OVERLAPPED`.
   > *2026-11-06:* nothing else holds the value at 3; `[PIPE-PER-PROCESS-2026-11-06]` Stage B raises it to 6.
2. **Thread-per-connection accept loop**: `AcceptLoop` currently creates one instance, calls
   `HandleClient` **inline**, then loops. Change to: create an instance → `ConnectNamedPipe` →
   **spawn a thread** running `HandleClient(conn)` → loop immediately to create the next
   instance. Track the spawned threads for join on `Stop()`.
3. **Per-connection state** (replaces the single `m_pipe` / `m_writeMutex` /
   `m_commandInFlight` / `m_inflightPipe`): introduce a `Connection` struct holding `HANDLE
   pipe`, its **own** `std::mutex writeMutex`, `std::atomic<bool> inFlight`, and
   `std::atomic<bool> inFlightHeavy`. Keep a registry `std::vector<std::shared_ptr<Connection>>`
   under `m_connMutex` (for teardown + the monitor). `WriteLine` takes the connection's own
   `writeMutex` (writes to different handles need no shared lock).
4. **Watch events → owning connection** (the only `PushEvent` path, Fern.cpp:4515): tag each
   `WatchEntry` with its originating `Connection*`. The watch thread writes the event to **that**
   connection (via its `writeMutex`), not a global `m_pipe`. When a connection disconnects, stop
   **only its** watches (key `m_watches` per connection, or filter by owner).
5. **MonitorLoop → per-connection**: iterate the registry; for any connection whose `inFlight`
   handle is broken, trip cancellation. **Cancellation caveat:** `Tot` is global and only the
   bulk lane runs cancellable scans, so trip `Tot::RequestPerCommand()` **only when the broken
   connection had `inFlightHeavy`** — otherwise an interactive-lane disconnect would wrongly
   abort a running bulk scan. (Longer term: per-connection cancel tokens.)
6. **Session cleanup**: `Radar::SessionManager/GroupSessionManager::DropAll()` currently runs on
   the single client's disconnect. Change to drop **only when the last connection disconnects**
   (registry empty) — sessions are bulk-lane-only and the UI is single-instance, so both lanes
   drop together on close. (Per-owner drop is a future refinement if needed.)
7. **`Stop()`**: signal stop, close all registry handles, join all connection threads (plus the
   existing accept/monitor/scan threads), then `DropAll`.

### 9.4 UI changes (`PipeClient` / `DumpService`)

1. Keep `PipeClient` as-is (one connection, id-mux, `_writeLock` around the byte-write). It's
   already correct for a single serial lane.
2. Add a second `PipeClient` and a thin router (mirror discrete's `BackendAdapter`): a
   `BulkCommands` set + `SendAsync` picks the bulk client for those, the interactive client for
   the rest. `ConnectAsync`/`DisconnectAsync` manage both. The **command lane table is §6** (the
   light allowlist → interactive; everything else → bulk).
3. `Pipe Activity` log: tag each entry with its lane (interactive/bulk) so the System-tab tail
   shows the split visibly (one extra column). The `Activity` event already exists per client.
4. AOT: no reflection added — a second `PipeClient` instance + a `HashSet<string>` route are
   trim-safe.

### 9.5 NOT in scope (separate follow-ups)

The §8.3 snapshot bottlenecks are **orthogonal** to the lane split and remain after it:
(a) the bulk-lane multi-MB chunk write still ties up the bulk handle (fine — it's the bulk
lane's job), and (b) the **UI-side single-threaded multi-MB chunk parse (~2.4 s/chunk)**. The
lane split fixes "interactive stays responsive during a scan"; it does **not** speed up the
snapshot itself. If snapshot speed matters, separately switch `SnapshotChunkAsync` to a
streaming `Utf8JsonReader` and/or smaller chunks (already noted in todo).

### 9.6 In-game verification checklist (MANDATORY — Fern has no unit tests)

1. Connect handshake completes with no hang (the §8.1 failure), incl. a fresh `trigger_scan`.
2. Live Walker drill + teleport + godmode stay responsive **while a Snapshot / Value Search
   streams on the bulk lane**.
3. Start a Value Search **mid-Snapshot** — both complete; bulk lane serializes them, interactive
   stays live.
4. Close the UI mid-scan — both connections free cleanly, sessions/watches dropped, game exits.
5. Disconnect only one lane (edge) — the other keeps working; a bulk scan isn't wrongly cancelled
   by an interactive disconnect (the §9.3.5 caveat).
6. CE `.CT` mailbox invoke still works under heavy UI load (it's off-pipe; should be unaffected).
7. Watches (System-tab / address watch) still deliver events to the right (interactive) lane.

### 9.8 What landed on `feat/multipipe-lane-split` (build ~1845)

**DLL (`Fern.cpp`/`.h`):**
- `kMaxPipeInstances = 3`; `AcceptLoop` creates instances and **spawns a detached
  `HandleConnection` thread per accepted connection** (own handle), looping immediately for the
  next instance. A `std::vector<shared_ptr<Connection>>` registry (`m_connMutex` + `m_connCv`)
  tracks live connections; `m_listenPipe` is the parked accept instance.
- `Connection { HANDLE pipe; std::mutex writeMutex; atomic<bool> inFlight; atomic<bool> closed; }`.
  `WriteLine(Connection&, line)` takes the connection's **own** `writeMutex` and no-ops if closed;
  `CloseConnOnce` (owning thread only) closes once under that mutex. `inFlight` is set only around
  `DispatchCommand` so the monitor's peek never races the connection's I/O or close.
- `MonitorLoop` iterates the registry, peeks each in-flight connection, trips `Tot::RequestPerCommand`
  on a broken pipe. `AcceptLoop` resets `Tot::ResetPerCommand` only on the **first** connection of a
  session (registry empty→1) — never per-command — so a light command on one lane can't clear a
  running scan's cancel on the other.
- Watches are **per-connection**: `WatchEntry.owner` points at the registering `Connection`; the
  watch thread writes its event to that connection via `WriteLine` (old global `PushEvent` removed).
  `StopWatchesForConnection(owner)` stops+joins a connection's watches in its cleanup before the
  `Connection` is freed. Sessions (`Radar::*::DropAll`) dropped only on the last disconnect
  (until [PIPE-PER-PROCESS-2026-11-06] Stage C, 2026-11-10: a session is its connection's, and each
  connection's cleanup drops its own with `DropOwner`).
- `Stop()` closes `m_listenPipe` (unblock `ConnectNamedPipe`) and `CancelIoEx`'s each connection
  (unblock `ReadFile`/`WriteFile`; each thread then closes its **own** handle), stops watches, waits
  (bounded 5 s) for the registry to drain, then joins accept/monitor.
- `DispatchCommand` gained a `Connection` arg (used only by the `watch` handler).

**UI:**
- `LaneRoutingPipeClient : IPipeClient` wraps two `PipeClient`s (`"I"` interactive + `"B"` bulk),
  routes `SendAsync` by a `BulkCommands` set (mirrors discrete's `BackendAdapter`), forwards
  events + activity, and on either lane dropping unexpectedly tears down **both** for a clean
  reconnect. `App.axaml.cs` builds it as the single `IPipeClient`; everything downstream is
  unchanged. `PipeClient` gained a `laneTag` ctor arg; `PipeLogEntry` gained a `Lane` column.

### 9.7 Effort / risk / open decisions

- **Effort:** M–L (DLL per-connection refactor is the bulk; UI router is small). **Risk:** med
  — live threading, no Fern unit tests → §9.6 in-game pass is the gate.
- **Open decisions to confirm before building:**
  - maxInstances = 3 vs 2 vs 4 (reconnect transients / future bridge).
  - Session cleanup: "drop on last disconnect" (simple, proposed) vs per-owner tagging.
  - Cancellation: "trip global Tot only for heavy-in-flight connection" (proposed) vs full
    per-connection cancel tokens (cleaner, more work).
  - Whether to also tag Pipe Activity entries by lane now (small, recommended).

-----

## 10. MEASURED (2026-07-23, build 2324) — the dispatcher is NOT the bottleneck

This document's core claim — that DLL-side serial-dispatch head-of-line blocking is what makes the
UI lag — was reasoned, never measured. `Sense` (build 2308) plus the automatic PERF records
(build 2320) finally measured it, on two games spanning both engine generations: **The Adventures
of Elliot (UE 5.4)** and **SEED BATTLE DESTINY REMASTERED (UE 4.27)**.

| Operation | wall | dispatcher busy | busy % | dispatches | dominant command |
|---|---:|---:|---:|---:|---|
| Copy CE Field | 486.5 ms | 120.4 ms | 24.7% | 740 | `walk_instance` (100%) |
| Copy CE XML | 224.4 ms | 59.5 ms | 26.5% | 386 | `walk_instance` (100%) |
| Copy CE Field | 534.5 ms | 144.1 ms | 27.0% | 793 | `walk_instance` (100%) |
| **Copy CE XML** | **5,362.7 ms** | **1,651.3 ms** | **30.8%** | **20,357** | `walk_instance` (100%) |
| Copy CE Field | 570.1 ms | 163.6 ms | 28.7% | 1,902 | `walk_instance` (100%) |
| **aggregate** | **7,178 ms** | **2,139 ms** | **29.8%** | **24,178** | |

### 10.1 Verdict: do NOT build Phase 1

Three independent readings of the same data say the dispatch model is not the problem:

- **The dispatcher is idle ~70% of wall-clock**, and the ratio is strikingly stable (22–31%) across
  operations spanning 2.6 ms to 5.4 s. Making dispatch non-blocking can only ever recover a slice of
  the busy 30% — and only if something else were queued behind it, which in a single-user export
  there is not.
- **No head-of-line spike exists to remove.** The worst *single* dispatch across 24,178 of them was
  **14.3 ms**. Phase 1's entire premise is a long-blocking command holding the read loop; nothing
  here holds it for more than a frame.
- **Phase 1 is expensive.** It was shipped and reverted once already (build 1840, §8), and a correct
  version needs overlapped/async pipe I/O. Paying that to chase a 30% slice of a non-blocking
  workload is not a trade worth making.

### 10.2 What the data says the real lever is: CALL COUNT

`walk_instance` is **100% of the dispatcher cost in every row**, and one Copy CE XML issued
**20,357** of them. Per round-trip:

- **0.088 ms inside the DLL** (the actual work)
- **0.208 ms everywhere else** — pipe latency, JSON envelope, UI-side deserialise — i.e. **2.4× the
  work is overhead**

That is the *pipe round-trip amortisation* win this document already describes in §5's sibling
material, and the repo already has the pattern twice: `search_properties_batch` (build 685) and
`walk_class_batch` (build 693). Batching `walk_instance` at the established ~200/call chunk size
would collapse 24,178 round-trips to ~121.

**Honest limit on that estimate:** this data cannot decompose the 0.208 ms into pipe latency (which
batching removes) versus UI-side per-result work (which it does not). The trend across runs is
suggestive — per-call overhead falls from 0.427 ms at 386 calls to 0.182 ms at 20,357, consistent
with fixed costs amortising — but the split must be measured before promising a figure.
`walk_class_batch` is the precedent to compare against, since it too gets only the round-trip win.

### 10.3 Status change

- **Phase 1 — WON'T DO** on the evidence above. Revisit only if a workload appears whose *single*
  dispatches actually block for hundreds of ms (a full `Dump All`, or a `value_scan` on a huge pool,
  neither of which is in this sample).
- **Phase 2 — unchanged** (still speculative, still gated on the concurrency-safety prerequisites).
- **New candidate, better founded than Phase 1: `walk_instance` batching.** Measure the
  latency/UI-work split first.

### 10.4 MEASURED (build 2327) — the split, and what batching would actually recover

§10.2 identified `walk_instance`'s round-trip count as the lever but could not say how much of the
0.208 ms/call overhead batching would recover. `PipeTransportStats` now separates it. Three Copy CE
XML runs on **SEED BATTLE DESTINY REMASTERED (UE 4.27)**:

| run | wall | dll | ipc | ui | dll % | **ipc %** | ui % | calls |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| A | 5,548.3 ms | 1,689.5 | 3,290.0 | 568.8 | 30.5% | **59.3%** | 10.3% | 20,357 |
| B | 555.1 ms | 157.7 | 406.3 | 0.0 | 28.4% | **73.2%** | 0.0% | 1,901 |
| C | 614.6 ms | 165.9 | 411.8 | 36.9 | 27.0% | **67.0%** | 6.0% | 2,108 |

Per call, strikingly consistent across a 10× spread in operation size:

| | dll | ipc | ui |
|---|---:|---:|---:|
| A | 0.083 ms | 0.162 ms | 0.028 ms |
| B | 0.083 ms | 0.214 ms | 0.000 ms |
| C | 0.079 ms | 0.195 ms | 0.018 ms |

**IPC is the cost — 59–73% of wall-clock, roughly 2× the actual DLL work — and it is exactly the
part batching removes.** UI-side per-result work is negligible (0.000–0.028 ms/call), so the export
tree building is not where the time goes.

Projected at the established ~200/call chunk:

| run | now | batched | speed-up | round-trips |
|---|---:|---:|---:|---|
| A | 5,548 ms | ~2,275 ms | **2.4×** | 20,357 → 102 |
| B | 555 ms | ~160 ms | **3.5×** | 1,901 → 10 |
| C | 615 ms | ~205 ms | **3.0×** | 2,108 → 11 |

**Three caveats on that projection, all pushing the same way — treat it as an upper bound:**

- It assumes batching removes IPC proportionally and **adds nothing**. Real batching serialises a
  larger payload and parses a bigger JSON document; some of that reappears in `dll` and `ui`.
- `ui = wall − transport`, and run B hit the zero floor (transport ≥ wall). So `ui` is "negligible,
  at or below the measurement floor" rather than precisely quantified.
- `dll` is unaffected by batching and is a hard floor at ~0.08 ms/call — run A cannot go below its
  1,689 ms of actual walking without optimising the walk itself.

**This also settles Phase 1 more firmly than §10.1 did.** Phase 1 targets the `dll` share (27–30%);
the cost is `ipc` (59–73%). It would have been aimed at the smaller half of the wrong problem.

**Recommendation: batch `walk_instance`.** Effort **M**, risk **low-med**, following the
`walk_class_batch` / `search_properties_batch` precedent — including their safety net: a DLL-side
batch that is a trivial `for` loop over the single-call path, one shared serialiser between single
and batch dispatch, and an equivalence test proving byte-identical output.


### 10.5 RESULT (build 2335) - 1.71x measured, and the IPC model was wrong

Same Copy CE XML on SEED, before (build 2327) and after the struct-tree batching:

| | before | after | change |
|---|---:|---:|---|
| **wall** | 5,893.3 ms | **3,437.1 ms** | **1.71x faster** (-2,456 ms) |
| dispatches | 22,522 | **1,355** | 16.6x fewer |
| dll | 1,751.7 ms | 1,505.5 ms | 1.16x (the actual walking - expected to be flat) |
| **ipc** | 3,531.7 ms | **1,278.4 ms** | **2.76x** (-2,253 ms) |
| ui | 609.9 ms | 653.2 ms | 0.93x - **slightly worse**, as predicted |

`top:` now names `walk_instance_batch`, which was the acceptance criterion.

**The projection was 2.4-3.5x; reality is 1.71x.** Section 10.4 flagged it as an upper bound
because it assumed batching adds nothing. It adds, and the data says exactly where:

**IPC is NOT purely a per-round-trip cost.** If it were, 1,355 calls at the old 0.157 ms/call
would be ~212 ms. It is **1,278 ms**. So of the original 3,532 ms:

- **~2,253 ms was fixed per-round-trip overhead** - removed by batching.
- **~1,066 ms is payload-proportional** - the same bytes still cross the pipe regardless of how
  many messages carry them, and batching cannot touch it.

Per-call IPC rose 0.157 -> 0.945 ms for the same reason: each call now carries ~16x the payload.
`ui` rose too (610 -> 653 ms) - bigger JSON documents cost more to parse, which is the other half
of "batching adds something".

**Two secondary observations worth recording:**

- **Average batch size is only ~16.6 instances**, far below the 200 chunk cap. The limit is the
  *fan-out* - a struct has a handful of nested structs, not hundreds - so raising the chunk size
  would achieve nothing. Batching across roots rather than per `ResolveStructFieldsIntoAsync` call
  would grow the batches, but since the residual IPC is mostly payload-proportional, the return
  would be far smaller than the round-trip arithmetic suggests.
- **Worst single dispatch rose 14.5 ms -> 85.2 ms** (a batch does ~16 walks). Still nowhere near a
  problem, but it is the metric Phase 1 cared about, and batching moves it the *wrong* way. Another
  reason not to pair those two ideas.

**Where the remaining 3,437 ms sits:** dll 1,506 (real work) + ipc 1,278 (mostly payload) + ui 653
(parse). **The next lever is BYTES, not messages** - trimming fields the export never reads would
attack both the payload-proportional IPC and the UI parse cost at once. Unquantified: nobody has
measured what fraction of a `walk_instance` payload the CE export actually consumes.


### 10.6 MEASURED (build 2339) - what fraction of a `walk_instance` payload the export reads

§10.5 ended on "the next lever is BYTES" and named the gap: nobody had measured how much of a
`walk_instance` payload the CE export actually consumes. `scripts/analysis/walk_payload_audit.py`
measures it - byte-accounting every JSON key of every sampled response against a
key-by-key classification of what `CeXmlExportService` / `CsxExportService` really read
(the tags cite the consuming line, so the verdicts are re-derivable, not asserted).

Sample: the UI pipe logs of real Copy CE XML runs on **SEED BATTLE DESTINY REMASTERED (UE 4.27)** -
the same game as §10.4/§10.5, so the numbers compose. 6,778 `walk_instance`/`walk_instance_batch`
responses, 14,263 instances, 27,002 complete field objects, 6.8 MB accounted out of 113 MB that
crossed the pipe.

| scope | share of sample | used | csx-only | **unused** | structural |
|---|---:|---:|---:|---:|---:|
| `field` (per-field keys) | 52.7% | 60.9% | 18.6% | **16.7%** | 3.8% |
| `elem` (inline array elements) | 20.3% | 43.9% | - | **44.6%** | 11.5% |
| `instance` (per-instance header) | 20.4% | 0.0% | - | **99.0%** | 1.0% |
| `envelope` (per response) | 6.0% | 61.9% | - | 16.8% | 21.3% |

**The per-instance header is 100% dead weight for an export.** The exporter touches
`result.Fields` and nothing else, so `name` / `class` / `class_addr` / `outer` / `outer_name` /
`outer_class` / `props_size` / `stale` - and even `addr`, since a batch reply is positional - are
paid for and thrown away.

**The single biggest droppable key is a value nobody reads.** Ranked by bytes: `elem.h`
(9.0% - `ArrayElementValue.Hex`), `field.hex` (9.8%, read only by CSX), `field.value` (5.7%),
`field.array_inner_addr` (3.0%, a `read_array_elements` handle no exporter uses). The pattern is
consistent: CE XML output is **structural** (description + offset + CE type + drill-down), so every
decoded VALUE the walk carries is dead for it. The only values that ARE read are inside container
elements, where they become row labels and DropDownList pairs.

**Verdict: a lean walk mode could drop roughly a quarter to two-fifths of the bytes.** Weighting
only the scopes that scale with payload (`field` + `elem`, 73% of the sample): **~24% unused
outright, ~38% with `hex` gone if CSX opts out too.** Per-instance headers and the redundant
envelope `count` add a couple of points on top.

**Sampling caveat, stated because it moves the headline.** `PipeClient.LogBody` caps a logged body
at 1024 chars, so coverage is 6.2% of the 113 MB that crossed the pipe and the sample is *prefixes*.
Only whole `"key": value` pairs and whole field objects are counted, so nothing is half-counted -
but a response's envelope and its first instance header are always inside the prefix while the
field array is cut, which inflates those two scopes (the report shows 1.9 fields/instance sampled;
real classes have far more). That is why the whole-payload line reads a flattering 39% and the
per-scope table is the reading to trust. To settle it exactly: set `UE5DUMP_PIPE_LOG_FULL=1`
(uncaps the body log), do one Copy CE XML, re-run the script - rotation keeps the last ~32 MiB as
complete, unbiased payloads.

**What a lean mode would be.** A request flag (`lean: true`) that suppresses the export-dead keys:
the whole instance header bar the fields, `hex` / `value` / `str_value` / `enum_value` / `enum_name`
/ `ptr_name` / `array_inner_addr` per field, and `h` per array element. It attacks both remaining
costs at once - the payload-proportional IPC (~1,066 ms of §10.5's residual) and the UI parse
(653 ms) - and unlike batching it has no round-trip arithmetic to overshoot on: bytes removed are
bytes not sent. Precondition: CSX and the Live Walker must NOT get the lean payload (they read
`hex`, `value`, `bool_mask`, `bool_byte_offset`), so the flag belongs to the CE XML export path
only, and the equivalence test must prove a lean walk and a full walk produce the same XML.

### 10.7 SHIPPED (build 2351) - `lean: true`

Built exactly as §10.6 specified. The full drop list is in
[pipe-protocol.md](pipe-protocol.md) under `walk_instance`.

- **DLL:** `SerializeField(fv, lean)` / `EncodeInstanceWalkToJson(result, lean)` gate the dead
  keys; `walk_instance` and `walk_instance_batch` read `lean` (batch-level default, per-item
  override). **Subtractive only** - a lean object is the full object minus keys, so no client
  needs a new parsing branch and an older DLL that ignores the flag stays correct.
- **UI:** `lean` threaded through `IDumpService.WalkInstance(Batch)Async` and the three shared CE
  XML resolvers. The **default stays full-fat** because `CsxExportService` calls the same
  `ResolveDrilldownAsync` and genuinely reads `hex` / `bool_mask` / `bool_byte_offset`; only the
  three CE XML callers (Live Walker Copy CE XML + Copy CE Field, Instance Finder) pass `lean: true`.
  The Live Walker GRID is untouched - it loads fields for display, values included.
- **Test:** `WalkInstanceLeanTests` runs the same export twice, over full and over lean payloads,
  and asserts **byte-identical XML** - plus the wire flag (absent unless asked, survives the
  batch->single fallback) and that the shared resolver does not lean by default. Mutation-checked:
  blanking a key the exporter *does* read (`ptr_class`) fails the equivalence assert, so the test
  has teeth rather than passing vacuously.

### 10.8 IN-GAME VERIFIED (build 2353) - -41.0% payload, and the XML is unchanged

One Copy CE XML of the same object on SEED, before (DLL 2338, full) and after (DLL 2353, lean).
`"lean":true` confirmed on the wire in the DLL's own request log.

**Correctness first.** Both exports are 149,621 lines and 18,758 records (4,432 groups / 14,326
leaves). **15 lines differ, and every one is a per-session runtime value**: the root object's heap
address, and 14 `DropDownList` entries whose FName ComparisonIndex moved - the NAME half of each
pair is identical. Nothing structural moved: same records, offsets, CE types, descriptions.

**Payload - the measurement the flag exists for**, taken from the UI pipe log's response lengths
over the *same* 134 `walk_instance_batch` responses:

| | responses | payload | per response |
|---|---:|---:|---:|
| before (full) | 134 | 1,982,875 B | 14,798 B |
| after (lean) | 134 | 1,168,944 B | **8,723 B** |
| | | **-41.0%** | |

§10.6 predicted ~38% for the payload-scaling scopes plus a near-100%-dead instance header. The
prediction held.

**Wall-clock - honestly, this export was too small to attribute.** PERF records: wall 487.6 ->
364.2 / 310.8 ms, `dll` 146.7 -> 116.2 / 118.6 ms (**-20%, consistent across both runs** - fewer
keys to serialise is real DLL work removed), `ui` 133.9 -> 31.8 / 0.0 ms, but **`ipc` did not move**
(207.0 -> 216.2 / 212.9 ms). Per call that is 1.556 -> 1.601 / 1.625 ms while the bytes per call
nearly halved.

**That is a finding, not a disappointment: at ~15 KB per response and 134 calls, IPC is dominated by
FIXED per-call cost, not by bytes.** §10.5's "~1,066 ms is payload-proportional" was decomposed from
a 20,357-call export; nothing here contradicts it, but nothing here confirms it either. The two
runs also come from different game sessions, and the before-run carries first-run `ui` cost, so the
1.34-1.57x wall figure is not a claim. **To settle the wall-clock, repeat on the big export target**
(the ~20k-call object of §10.4/§10.5), where the payload is two orders of magnitude larger.

**Flatten sanity (same session).** The flatten export keeps **14,326 leaves - exactly the
non-flatten count** - with an identical per-type mix (4 Bytes 12,386 / Byte 1,698 / String 224 /
8 Bytes 2). Only wrappers collapsed: groups 4,432 -> 2,168, entries 18,758 -> 16,494 (-2,264 each,
i.e. every removed entry was a group). 4,792 leaves gained their parent's label as a
`"parent (off) > leaf (off)"` prefix; 9,534 kept their description verbatim. No leaf gained or lost.

## The pipe name is a single global — the wrong-game hazard (2026-07-27)

> **2026-11-06:** "the real fix, not built" below is planned. See todo.md `[PIPE-PER-PROCESS-2026-11-06]` Stage A:
> `UE5DumpBfx_<pid>`, a UI connection list, and a refusal when only an old DLL's pipe exists. It also measured how a
> second server on the name arises (a startup probe reading `ERROR_PIPE_BUSY` as "no pipe").
>
> **2026-11-10: built in build 3695** (dev-log 3695). The hazard below describes builds before it.

Separate from head-of-line blocking, and cheaper to hit: `\.\pipe\UE5DumpBfx` is ONE
well-known name with `maxInstances = 3`. **Every injected game serves that same name.** A
connecting client lands on whichever instance is free, and there is no way to ask for a
particular game.

Two failure modes follow, and the user cannot tell them apart from the UI:

1. **Connect fails** — the instances belong to a game you are not using. The CE `.CT` path
   already surfaces this (*"the Pipe Server failed to start. Most likely another process
   already owns …"*); the proxy/UI path had nothing.
2. **Connect SUCCEEDS against the wrong game** and quietly shows its data. Strictly worse,
   because nothing else on screen contradicts it.

A UI disconnect does **not** free the name — only the game exiting does. Observed: TQ2's server
logged `Stopped` twelve seconds after its clients disconnected, when the game was closed.

**Shipped mitigation (build 2471).** `get_pointers` now carries `pid` beside `module_name`, and
after connecting the UI enumerates processes hosting the dumper DLL (machinery that already
existed for the inject picker). More than one ⇒ a red top banner naming the process actually
reached and the others. Advisory only: it detects the ambiguity, it does not remove it.

**The real fix, not built.** A per-process pipe name (`UE5DumpBfx_<pid>`) plus client-side
discovery — named pipes are enumerable with `FindFirstFile` on `\.\pipe\`. That makes the
choice explicit instead of racy. It is a protocol change and needs its own pass. The CE mailbox
is shared memory and would be unaffected, but `scripts/UE5CEDumper.CT` and `dll/src/Methode.cpp`
hardcode the current name in user-facing strings.
