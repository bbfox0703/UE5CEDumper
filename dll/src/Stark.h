// ============================================================
// Stark — 修塔爾克 (勇者戰士 — Brave Warrior)
// GameThreadDispatch: MinHook ProcessEvent hook + game-thread queue
//
// Architecture:
//   Pipe thread calls EnqueueInvoke() → pushes request to queue
//   Game thread's ProcessEvent hook drains queue → executes requests
//   Result returned via std::future (blocks pipe thread until done)
//
// This ensures ProcessEvent is always called from the game thread,
// which UE expects for state-changing operations (UI, rendering,
// spawning actors, etc.).
// ============================================================
#pragma once

#include <cstdint>

namespace Stark {

/// Install the ProcessEvent hook at the given address.
/// @param processEventAddr  Absolute address of UObject::ProcessEvent
/// @return true if hook installed successfully
bool InstallHook(uintptr_t processEventAddr);

/// Remove the hook and clean up. Safe to call even if not installed.
/// Does NOT call MH_Uninitialize — use Shutdown() at DLL unload instead.
void RemoveHook();

/// Full teardown: remove hook AND uninitialize MinHook globally.
/// Call ONLY from DLL_PROCESS_DETACH (via UE5_Shutdown). After this,
/// InstallHook() can no longer be used unless MinHook is re-initialized.
void Shutdown();

/// @return true if the ProcessEvent hook is active
bool IsHookActive();

/// Enqueue a ProcessEvent invocation for game-thread execution.
/// Blocks until the game thread executes it (or timeout).
///
/// @param instance  UObject instance pointer
/// @param ufunc     UFunction pointer
/// @param params    Parameter buffer pointer (already allocated/written by caller)
/// @return 0 on success, -4 if SEH exception, -5 if timeout, -7 if hook not active
int32_t EnqueueInvoke(uintptr_t instance, uintptr_t ufunc, uintptr_t params);

/// Default invoke timeout (compile-time baseline, used when no override is set).
constexpr int32_t kDefaultInvokeTimeoutMs = 5000;

/// Override the EnqueueInvoke timeout in milliseconds. Set to 0 to revert to
/// kDefaultInvokeTimeoutMs. Clamped to [100, 600000] (100ms .. 10min). Thread-safe.
void SetInvokeTimeoutMs(int32_t timeoutMs);

/// Read the current invoke timeout in milliseconds (post-clamping).
int32_t GetInvokeTimeoutMs();

} // namespace Stark
