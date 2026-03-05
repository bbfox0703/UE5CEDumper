// ============================================================
// GameThreadDispatch.cpp — Game-thread ProcessEvent dispatch
//
// Hooks UObject::ProcessEvent using MinHook. Every game-thread PE
// call first drains a lock-protected queue of pending invocations
// submitted from the pipe handler thread.
//
// Empty-queue fast path: one mutex lock/unlock per ProcessEvent
// call (negligible vs ProcessEvent's own cost).
// ============================================================

#define LOG_CAT "PIPE"
#include "Logger.h"
#include "GameThreadDispatch.h"

#include <MinHook.h>
#include <Windows.h>

#include <atomic>
#include <chrono>
#include <future>
#include <memory>
#include <mutex>
#include <queue>
#include <vector>

namespace GameThreadDispatch {

// ---- Types ----

/// A single queued ProcessEvent invocation request.
/// Shared ownership: pipe thread holds shared_ptr while waiting on future,
/// game thread holds shared_ptr while executing. This prevents use-after-free
/// if the pipe thread times out and releases its reference.
struct InvokeRequest {
    uintptr_t instance;
    uintptr_t ufunc;
    uintptr_t params;
    std::promise<int32_t> promise;
};

// ---- State ----

// Original ProcessEvent function pointer (set by MinHook)
typedef void(__fastcall* FnProcessEvent)(void* thisObj, void* ufunc, void* params);
static FnProcessEvent s_originalPE = nullptr;

// Pending invoke queue
static std::mutex s_queueMutex;
static std::queue<std::shared_ptr<InvokeRequest>> s_invokeQueue;

// Hook state
static std::atomic<bool> s_hookActive{false};
static std::atomic<bool> s_mhInitialized{false};
static uintptr_t s_hookedAddr = 0;

// Timeout for waiting on game-thread execution
static constexpr auto INVOKE_TIMEOUT = std::chrono::seconds(5);

// ---- SEH-isolated helper ----

/// Call ProcessEvent with SEH protection. Isolated into a separate function
/// because MSVC does not allow __try in functions with C++ objects that
/// require unwinding (shared_ptr, vector, promise, etc.).
/// Returns 0 on success, -3 if no original PE, -4 on SEH exception.
static int32_t CallProcessEventSEH(uintptr_t instance, uintptr_t ufunc, uintptr_t params) {
    if (!s_originalPE) return -3;
    __try {
        s_originalPE(
            reinterpret_cast<void*>(instance),
            reinterpret_cast<void*>(ufunc),
            reinterpret_cast<void*>(params));
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return -4;
    }
    return 0;
}

// ---- Hook function ----

/// Hooked ProcessEvent — called on the game thread for every UObject event.
/// Drains the invoke queue first, then calls the original PE for the game's own call.
static void __fastcall HookedProcessEvent(void* thisObj, void* ufunc, void* params) {
    // Drain pending invocations from pipe thread
    {
        std::vector<std::shared_ptr<InvokeRequest>> pending;

        {
            std::lock_guard<std::mutex> lock(s_queueMutex);
            while (!s_invokeQueue.empty()) {
                pending.push_back(std::move(s_invokeQueue.front()));
                s_invokeQueue.pop();
            }
        }

        // Execute all pending requests outside the lock
        for (auto& req : pending) {
            int32_t result = CallProcessEventSEH(req->instance, req->ufunc, req->params);

            if (result == -4) {
                LOG_ERROR("GameThreadDispatch: SEH exception during queued PE call "
                          "inst=0x%llX func=0x%llX",
                          (unsigned long long)req->instance,
                          (unsigned long long)req->ufunc);
            }

            // Fulfill the promise — unblocks the waiting pipe thread
            try {
                req->promise.set_value(result);
            } catch (...) {
                // Promise already satisfied (shouldn't happen, but be safe)
            }
        }
    }

    // Now handle the game's own ProcessEvent call
    if (s_originalPE) {
        s_originalPE(thisObj, ufunc, params);
    }
}

// ---- Public API ----

bool InstallHook(uintptr_t processEventAddr) {
    if (s_hookActive.load()) {
        LOG_WARN("GameThreadDispatch: hook already active");
        return true;
    }

    if (!processEventAddr) {
        LOG_ERROR("GameThreadDispatch: null processEventAddr");
        return false;
    }

    // Initialize MinHook (once)
    if (!s_mhInitialized.load()) {
        MH_STATUS status = MH_Initialize();
        if (status != MH_OK && status != MH_ERROR_ALREADY_INITIALIZED) {
            LOG_ERROR("GameThreadDispatch: MH_Initialize failed: %s",
                      MH_StatusToString(status));
            return false;
        }
        s_mhInitialized.store(true);
    }

    // Create hook
    MH_STATUS status = MH_CreateHook(
        reinterpret_cast<LPVOID>(processEventAddr),
        reinterpret_cast<LPVOID>(&HookedProcessEvent),
        reinterpret_cast<LPVOID*>(&s_originalPE));

    if (status != MH_OK) {
        LOG_ERROR("GameThreadDispatch: MH_CreateHook failed: %s",
                  MH_StatusToString(status));
        return false;
    }

    // Enable hook
    status = MH_EnableHook(reinterpret_cast<LPVOID>(processEventAddr));
    if (status != MH_OK) {
        LOG_ERROR("GameThreadDispatch: MH_EnableHook failed: %s",
                  MH_StatusToString(status));
        MH_RemoveHook(reinterpret_cast<LPVOID>(processEventAddr));
        return false;
    }

    s_hookedAddr = processEventAddr;
    s_hookActive.store(true);
    LOG_INFO("GameThreadDispatch: ProcessEvent hook installed at 0x%llX",
             (unsigned long long)processEventAddr);
    return true;
}

void RemoveHook() {
    if (!s_hookActive.load()) return;

    if (s_hookedAddr) {
        MH_DisableHook(reinterpret_cast<LPVOID>(s_hookedAddr));
        MH_RemoveHook(reinterpret_cast<LPVOID>(s_hookedAddr));
    }

    s_hookActive.store(false);
    s_originalPE = nullptr;
    s_hookedAddr = 0;

    LOG_INFO("GameThreadDispatch: hook removed");

    // Note: we intentionally do NOT call MH_Uninitialize() here.
    // MH_Uninitialize removes ALL hooks, which could interfere if other
    // components also use MinHook. Safe to leave initialized.
}

bool IsHookActive() {
    return s_hookActive.load();
}

int32_t EnqueueInvoke(uintptr_t instance, uintptr_t ufunc, uintptr_t params) {
    if (!s_hookActive.load()) {
        return -7; // Hook not active
    }

    auto req = std::make_shared<InvokeRequest>();
    req->instance = instance;
    req->ufunc = ufunc;
    req->params = params;

    auto future = req->promise.get_future();

    {
        std::lock_guard<std::mutex> lock(s_queueMutex);
        s_invokeQueue.push(std::move(req));
    }

    LOG_INFO("GameThreadDispatch: enqueued invoke inst=0x%llX func=0x%llX, waiting...",
             (unsigned long long)instance, (unsigned long long)ufunc);

    // Wait for game thread to execute the request
    auto status = future.wait_for(INVOKE_TIMEOUT);
    if (status == std::future_status::timeout) {
        LOG_ERROR("GameThreadDispatch: invoke timeout (5s) inst=0x%llX func=0x%llX",
                  (unsigned long long)instance, (unsigned long long)ufunc);
        return -5;
    }

    int32_t result = future.get();
    LOG_INFO("GameThreadDispatch: invoke completed result=%d", result);
    return result;
}

} // namespace GameThreadDispatch
