// ============================================================
// Frieren — 芙莉蓮, 葬送のフリーレン (主角 — Protagonist)
// ExportAPI: ~30 C ABI exports for CE Lua bridge
// ============================================================

#include "Frieren.h"
#define LOG_CAT "INIT"
#include "Sein.h"
#include "BuildStamp.h"
#include "Grimoire.h"
#include "Macht.h"
#include "Genau.h"
#include "Flamme.h"
#include "Aura.h"
#include "Serie.h"
#include "Ubel.h"
#include "Fern.h"
#include "Stark.h"
#include "Mimic.h"
#include "Wirbel.h"
#include "Solitar.h"
#include "Laufen.h"
#include "Hemmung.h"
#include "Solide.h"
#include "Dunste.h"
#include "Schlacht.h"
#include "Grausam.h"  // Grausam::SetForegroundLock — soft-disable the foreground lock on shutdown (L10)
#include "Tot.h"     // Tot::RequestShutdown — enter the shutdown window before joining workers (M5)

#include <string>
#include <cstring>
#include <mutex>
#include <atomic>
#include <algorithm>
#include <thread>
#include <chrono>
#include <vector>

// Global cached state (also accessed by PipeServer)
uintptr_t   g_cachedGObjects        = 0;
uintptr_t   g_cachedGNames          = 0;
uintptr_t   g_cachedGWorld          = 0;
uintptr_t   g_cachedSparseDelegates = 0;  // FSparseDelegateStorage::SparseDelegates (UE 5.0+, optional)
uint32_t    g_cachedUEVersion = 0;
bool        g_cachedVersionDetected = true;  // false if UE version detection failed (PE + memory scan)
bool        g_cachedIsUserOverride  = false; // true = ueVersion came from a user-set persistent override
bool        g_cachedIsLowConfidence = false; // true = Tier 3 bare-pattern OR publisher-bias fallback
bool        g_cachedVersionTooOld   = false; // true = engine predates 4.11 — scan skipped, see Genau.h
const char* g_cachedPublisherThumbprint = nullptr;  // e.g. "SQUARE_ENIX" (nullptr if no match)
const char* g_cachedGObjectsMethod        = "not_found";  // "aob", "data_scan", "not_found"
const char* g_cachedGNamesMethod          = "not_found";  // "aob", "string_ref", "pointer_scan", "not_found"
const char* g_cachedGWorldMethod          = "not_found";  // "aob", "not_found"
const char* g_cachedSparseDelegatesMethod = "not_found";  // "aob", "not_found"

// AOB Usage Tracking: PE hash, winning pattern IDs, scan statistics
char        g_cachedPeHash[17] = {0};
const char* g_cachedGObjectsPatternId        = nullptr;
const char* g_cachedGNamesPatternId          = nullptr;
const char* g_cachedGWorldPatternId          = nullptr;
const char* g_cachedSparseDelegatesPatternId = nullptr;
int         g_cachedGObjectsTried = 0, g_cachedGObjectsHit = 0;
int         g_cachedGNamesTried   = 0, g_cachedGNamesHit   = 0;
int         g_cachedGWorldTried   = 0, g_cachedGWorldHit   = 0;
uintptr_t   g_cachedGObjectsScanAddr        = 0;
uintptr_t   g_cachedGNamesScanAddr          = 0;
uintptr_t   g_cachedGWorldScanAddr          = 0;
uintptr_t   g_cachedSparseDelegatesScanAddr = 0;
const char* g_cachedGWorldAob    = nullptr;
int         g_cachedGWorldAobPos = 0;
int         g_cachedGWorldAobLen = 0;
// &GEngine — the static slot. Same triple as GWorld so a GameEngine-rooted CE export
// can be AOB-wrapped instead of baking in a stale UEngine* snapshot.
uintptr_t   g_cachedGEngine          = 0;
const char* g_cachedGEngineMethod    = "not_found";
const char* g_cachedGEnginePatternId = nullptr;
uintptr_t   g_cachedGEngineScanAddr  = 0;
const char* g_cachedGEngineAob       = nullptr;
int         g_cachedGEngineAobPos    = 0;
int         g_cachedGEngineAobLen    = 0;

// The init latch is set only at the very END of UE5_Init, after a multi-second scan,
// so it can never serialize two callers on its own — and there IS a designed-in second
// caller: proxy mode (Heiter) starts the pipe WITHOUT scanning, so a UI "Scan"
// (Fern::RunScan -> UE5_Init) and a CE hotkey (Mimic::EnsureInitialized -> UE5_Init)
// can both find the latch clear and both run a full init. Both write DynOff::*, Aura's
// array descriptor, Serie's pool state, and FindGEngineSlot's report wholesale, and the
// probes read back what earlier probes latched — so an interleave latches a MIX and
// still prints validated=yes. Atomic for the unlocked fast path; s_initMutex guards the
// body so a second caller waits and returns the first caller's result. (B4/B5 audit #4)
static std::atomic<bool> s_initialized{false};
static std::mutex  s_initMutex;
static Fern  s_pipeServer;
static std::mutex  s_walkMutex;
static ClassInfo   s_walkCache;

// Global scan progress for UI polling (updated by UE5_Init, read by scan_status)
namespace ScanProgress {
    std::atomic<int>  phase{0};
    std::string       statusText;
    std::mutex        statusMutex;

    void Set(int p, const char* text) {
        phase.store(p, std::memory_order_release);
        std::lock_guard<std::mutex> lock(statusMutex);
        statusText = text;
    }
    std::string GetStatusText() {
        std::lock_guard<std::mutex> lock(statusMutex);
        return statusText;
    }
}

// Helper: copy string to buffer safely
static bool CopyToBuffer(const std::string& src, char* buf, int32_t bufLen) {
    if (!buf || bufLen <= 0) return false;
    size_t copyLen = (std::min)(src.size(), static_cast<size_t>(bufLen - 1));
    memcpy(buf, src.c_str(), copyLen);
    buf[copyLen] = '\0';
    return true;
}

extern "C" {

bool UE5_Init() {
    if (s_initialized.load(std::memory_order_acquire)) {
        LOG_WARN("UE5_Init: Already initialized");
        return true;
    }

    // Serialize the body. try_lock first so the WAIT is visible in the log: a queued
    // caller is this guard doing its job, and it is the only externally observable
    // proof that the interleave was possible at all (see the log-derivable check in
    // todo.md). Then re-test the latch under the lock — the first caller may have
    // completed while we blocked, and re-scanning would be the very corruption
    // this guard exists to prevent.
    std::unique_lock<std::mutex> initLock(s_initMutex, std::try_to_lock);
    if (!initLock.owns_lock()) {
        LOG_WARN("UE5_Init: init already in progress on another thread — tid=%lu is waiting "
                 "(guard working, not an error)", GetCurrentThreadId());
        initLock.lock();
        LOG_INFO("UE5_Init: tid=%lu resumed after waiting (first caller %s)",
                 GetCurrentThreadId(),
                 s_initialized.load(std::memory_order_acquire)
                     ? "succeeded — returning its result, no second scan"
                     : "did NOT complete — this thread will scan");
    }
    if (s_initialized.load(std::memory_order_acquire)) {
        LOG_WARN("UE5_Init: Already initialized");
        return true;
    }

    LOG_INFO("UE5_Init: Starting initialization...");

    Genau::EnginePointers ptrs;
    Genau::FindAll(ptrs, [](int phase, const char* text) {
        ScanProgress::Set(phase, text);
    });

    g_cachedGObjects        = ptrs.GObjects;
    g_cachedGNames          = ptrs.GNames;
    g_cachedGWorld          = ptrs.GWorld;
    g_cachedSparseDelegates = ptrs.SparseDelegates;
    g_cachedUEVersion = ptrs.UEVersion;
    g_cachedVersionDetected = ptrs.bVersionDetected;
    g_cachedIsUserOverride  = ptrs.bUserOverride;
    g_cachedIsLowConfidence = ptrs.bLowConfidence;
    g_cachedVersionTooOld   = ptrs.bVersionTooOld;
    g_cachedPublisherThumbprint = ptrs.publisherThumbprint;
    g_cachedGObjectsMethod        = ptrs.gobjectsMethod;
    g_cachedGNamesMethod          = ptrs.gnamesMethod;
    g_cachedGWorldMethod          = ptrs.gworldMethod;
    g_cachedSparseDelegatesMethod = ptrs.sparseDelegatesMethod;

    // AOB Usage Tracking
    memcpy(g_cachedPeHash, ptrs.peHash, sizeof(g_cachedPeHash));
    g_cachedGObjectsPatternId        = ptrs.gobjectsPatternId;
    g_cachedGNamesPatternId          = ptrs.gnamesPatternId;
    g_cachedGWorldPatternId          = ptrs.gworldPatternId;
    g_cachedSparseDelegatesPatternId = ptrs.sparseDelegatesPatternId;
    g_cachedGObjectsTried = ptrs.gobjectsPatternsTried;
    g_cachedGObjectsHit   = ptrs.gobjectsPatternsHit;
    g_cachedGNamesTried   = ptrs.gnamesPatternsTried;
    g_cachedGNamesHit     = ptrs.gnamesPatternsHit;
    g_cachedGWorldTried   = ptrs.gworldPatternsTried;
    g_cachedGWorldHit     = ptrs.gworldPatternsHit;
    g_cachedGObjectsScanAddr        = ptrs.gobjectsScanAddr;
    g_cachedGNamesScanAddr          = ptrs.gnamesScanAddr;
    g_cachedGWorldScanAddr          = ptrs.gworldScanAddr;
    g_cachedSparseDelegatesScanAddr = ptrs.sparseDelegatesScanAddr;
    g_cachedGWorldAob    = ptrs.gworldAob;
    g_cachedGWorldAobPos = ptrs.gworldAobPos;
    g_cachedGWorldAobLen = ptrs.gworldAobLen;
    g_cachedGEngine          = ptrs.GEngine;
    g_cachedGEngineMethod    = ptrs.gengineMethod;
    g_cachedGEnginePatternId = ptrs.genginePatternId;
    g_cachedGEngineScanAddr  = ptrs.gengineScanAddr;
    g_cachedGEngineAob       = ptrs.gengineAob;
    g_cachedGEngineAobPos    = ptrs.gengineAobPos;
    g_cachedGEngineAobLen    = ptrs.gengineAobLen;

    // Initialize subsystems — only when their pointer was found
    ScanProgress::Set(5, "Initializing subsystems...");
    if (ptrs.GNames) {
        if (ptrs.bUE4NameArray) {
            Serie::InitUE4(ptrs.GNames, ptrs.ue4StringOffset);
        } else if (ptrs.namePayloadGap > 0) {
            // Obfuscated licensee fork — only ever set when the experimental gate is
            // on AND Genau proved the format by decoding entry 0 to "None".
            Serie::InitObfuscated(ptrs.GNames, ptrs.nameChunksOffset, ptrs.namePayloadGap,
                                  ptrs.nameKeyTableCtx);
        } else {
            Serie::Init(ptrs.GNames, ptrs.fnameEntryHeaderOffset);
        }
    }
    if (ptrs.GObjects) {
        Aura::Init(ptrs.GObjects);
    }

    // Sanity check + dynamic offset detection — only when BOTH are available
    ScanProgress::Set(6, "Validating offsets...");
    if (ptrs.GObjects && ptrs.GNames) {
        // Quick sanity check: verify name resolution works for a few objects
        {
            int verified = 0, tested = 0;
            for (int32_t i = 0; i < Aura::GetCount() && tested < 10; ++i) {
                uintptr_t obj = Aura::GetByIndex(i);
                if (!obj) continue;
                ++tested;
                uint32_t nameIdx = 0;
                if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx)) {
                    std::string name = Serie::GetString(nameIdx);
                    if (!name.empty() && name != "None") {
                        ++verified;
                        if (verified <= 3) {
                            LOG_INFO("UE5_Init: Sanity obj[%d] name='%s' (idx=%u)", i, name.c_str(), nameIdx);
                        }
                    }
                }
            }
            LOG_INFO("UE5_Init: Name sanity: %d/%d objects resolved", verified, tested);
            if (verified == 0 && tested >= 5) {
                LOG_WARN("UE5_Init: WARNING — No objects resolved names! Check FUObjectItem size or FNamePool.");
            }
        }

        // --- Decoy recovery (Avowed / Obsidian UE5.x) ---
        // A relaxed structural match can validate a .data structure that passes
        // ValidateGObjects yet yields 0 usable objects (observed on Avowed: the
        // real FUObjectArray lives on the heap and is only reachable via the
        // data-section scan, which the AOB winner pre-empts). When the chosen
        // GObjects produced no objects, re-resolve from the data-scan candidate
        // set. The data scan surfaces MULTIPLE validatable candidates — several
        // small real-ish object lists plus the one true master array — so we must
        // not accept the first that resolves a name (that picks a small partial
        // list, e.g. Count~1.4K). Instead score every candidate by a spread name-
        // resolution sample and pick the qualifying one with the LARGEST object
        // count: the master GObjects (~millions) dwarfs partial lists (~thousands).
        // STRICTLY ADDITIVE: runs only when Count==0, so titles that already
        // resolve objects are never affected. See docs/avowed-gobjects-fix.md.
        if (Aura::GetCount() == 0) {
            LOG_WARN("UE5_Init: GObjects=0x%llX produced 0 objects — likely a decoy match; "
                     "attempting recovery...",
                     static_cast<unsigned long long>(ptrs.GObjects));

            // Primary recovery: a STATIC FUObjectArray in the module's .data/BSS that
            // no AOB matches (Avowed / Obsidian UE5.3 — even patternsleuth fails). The
            // base is content-validated (first objects resolve to clean names), so the
            // layout is known — force UE5-Extended so Aura reads NumElements at +0x24.
            int staticStride = 0;
            uintptr_t staticBase = Genau::FindGObjectsStaticStruct(&staticStride);
            if (staticBase) {
                Aura::InitWithExtendedLayout(staticBase, staticStride);
                if (Aura::GetCount() > 0) {
                    LOG_INFO("UE5_Init: Recovery SUCCESS (static struct scan) — GObjects 0x%llX -> 0x%llX (Count=%d, stride=%d)",
                             static_cast<unsigned long long>(ptrs.GObjects),
                             static_cast<unsigned long long>(staticBase), Aura::GetCount(), staticStride);
                    ptrs.GObjects = staticBase;
                    g_cachedGObjects = staticBase;
                    g_cachedGObjectsMethod = "static_struct_recovery";
                    g_cachedGObjectsPatternId = nullptr;
                    Flamme::UpdateGObjectsMethod(g_cachedPeHash, "static_struct_recovery");
                }
            }
        }

        // Fallback recovery: GObjects genuinely HEAP-allocated (the .data slot holds a
        // pointer to it). Evaluate the data-scan candidates and pick the largest array
        // whose first slots resolve to names — a small partial list must not win.
        if (Aura::GetCount() == 0) {
            std::vector<uintptr_t> candidates;
            Genau::CollectGObjectsCandidates(candidates, ptrs.GObjects);
            LOG_INFO("UE5_Init: Recovery (heap fallback) — evaluating %zu candidate(s)", candidates.size());

            uintptr_t best = 0;
            int bestCnt = 0, bestResolved = 0, bestScanned = 0;
            for (uintptr_t cand : candidates) {
                Aura::Init(cand);
                int cnt = Aura::GetCount();
                if (cnt <= 0) continue;
                // Scan the FIRST slots and count resolvable names. These hold the
                // master array's permanent core objects (CoreUObject classes/packages)
                // — densely named, so a real object array resolves several names here.
                // DO NOT spread the sample across the whole array: the true master is
                // huge and SPARSE (live named objects are a tiny fraction of capacity),
                // so a spread sample resolves almost nothing and wrongly rejects it,
                // leaving a small dense partial-list to win. Early-out once we have
                // enough confirmations so the dense core is cheap to verify.
                int limit = cnt < 65536 ? cnt : 65536;
                int scanned = 0, resolved = 0;
                for (int i = 0; i < limit && resolved < 8; ++i) {
                    uintptr_t obj = Aura::GetByIndex(i);
                    if (!obj) continue;
                    ++scanned;
                    uint32_t nameIdx = 0;
                    if (Macht::ReadSafe(obj + Grimoire::OFF_UOBJECT_NAME, nameIdx)) {
                        std::string nm = Serie::GetString(nameIdx);
                        if (!nm.empty() && nm != "None") ++resolved;
                    }
                }
                LOG_INFO("UE5_Init: Recovery — candidate 0x%llX Count=%d, names %d resolved (scanned %d of first %d)",
                         static_cast<unsigned long long>(cand), cnt, resolved, scanned, limit);
                if (resolved < 2) continue;                 // not a real object array
                if (cnt > bestCnt) {                         // among real arrays, the master has the largest count
                    best = cand; bestCnt = cnt; bestResolved = resolved; bestScanned = scanned;
                }
            }

            if (best) {
                LOG_INFO("UE5_Init: Recovery SUCCESS — GObjects 0x%llX -> 0x%llX "
                         "(Count=%d, %d names resolved in first %d slots)",
                         static_cast<unsigned long long>(ptrs.GObjects),
                         static_cast<unsigned long long>(best), bestCnt, bestResolved, bestScanned);
                Aura::Init(best);                           // re-init Aura on the chosen master array
                ptrs.GObjects = best;
                g_cachedGObjects = best;
                g_cachedGObjectsMethod = "data_scan_recovery";
                // FindAll already wrote the hint cache with the decoy's "aob" method +
                // winning patternId (before this recovery ran), and the UI re-saves the
                // record from these globals over the pipe. Correct both: rewrite the
                // method and CLEAR the now-misleading patternId so neither the DLL hint
                // cache nor the UI's re-save prioritises the decoy-only AOB pattern.
                g_cachedGObjectsPatternId = nullptr;
                Flamme::UpdateGObjectsMethod(g_cachedPeHash, "data_scan_recovery");
            } else {
                LOG_WARN("UE5_Init: Recovery failed — no candidate qualified as an object array; "
                         "restoring original GObjects");
                Aura::Init(ptrs.GObjects);  // keep state consistent (Count stays 0)
            }
        }

        // Dynamically detect FField/FProperty/UStruct offsets
        // Must be called AFTER FNamePool + ObjectArray are initialized
        if (!Genau::ValidateAndFixOffsets(ptrs.UEVersion)) {
            LOG_WARN("UE5_Init: Offset validation failed — using default offsets (may be wrong for this UE version)");
        }

        // &GEngine, second pass. FindAll runs BEFORE the FNamePool and the DynOff offsets exist,
        // and ValidateGEngineSlot needs both to resolve the reflected "GameViewport" property —
        // so the slot can only be validated here. Skipping this step is what made the System tab
        // report "&GEngine — AOB not found" on every title while the patterns were in fact
        // landing on the right address.
        Genau::ResolveGEngineDeferred(ptrs);
        g_cachedGEngine          = ptrs.GEngine;
        g_cachedGEngineMethod    = ptrs.gengineMethod;
        g_cachedGEnginePatternId = ptrs.genginePatternId;
        g_cachedGEngineScanAddr  = ptrs.gengineScanAddr;
        g_cachedGEngineAob       = ptrs.gengineAob;
        g_cachedGEngineAobPos    = ptrs.gengineAobPos;
        g_cachedGEngineAobLen    = ptrs.gengineAobLen;

        // Post-DynOff version correction: UProperty mode definitively means UE4 pre-4.25.
        // Structural detection beats user input here — wrong offsets break exports far worse
        // than a wrong label, so we override even when bUserOverride is set (and log loudly
        // so the UI / log triage notices).
        if (!DynOff::bUseFProperty && ptrs.UEVersion >= 500) {
            uint32_t corrected = Aura::IsFlat() ? 418 : 424;
            if (ptrs.bUserOverride) {
                LOG_WARN("UE5_Init: User override = %u but UProperty mode detected — "
                         "structural correction wins, overriding to %u (flat=%s). "
                         "Update your UE Version override to UE4 if this game is misclassified.",
                         ptrs.UEVersion, corrected, Aura::IsFlat() ? "yes" : "no");
            } else {
                LOG_WARN("UE5_Init: UProperty mode detected (no FProperty) but version=%u (>= 500). "
                         "Overriding to %u (flat=%s)", ptrs.UEVersion, corrected,
                         Aura::IsFlat() ? "yes" : "no");
            }
            ptrs.UEVersion = corrected;
            g_cachedUEVersion = ptrs.UEVersion;
            g_cachedIsLowConfidence = true; // post-correction, user shouldn't blindly trust label
        }

        // Post-DynOff version correction (UPWARD): structural / property markers can
        // reveal that a stripped binary is a NEWER engine than the version-string scan
        // found. Heavily-stripped games (e.g. The Adventures of Elliot) lose every
        // version string and fall back to 4.27, yet are really UE5 — the structural
        // probes already proved it (tagged FFieldVariant = UE5.3+; FProperty mode).
        // Raise the floor so version-gated behaviour (the >=500 branches in Aura/Ubel)
        // and the UI badge are honest. Runtime-only: the cached RAW detection is left
        // as-is and re-corrected here on every init, so no cache delete is ever needed.
        if (DynOff::bUseFProperty && DynOff::bTaggedFFieldVariant && g_cachedUEVersion < 503) {
            LOG_WARN("UE5_Init: structural marker (tagged FFieldVariant = UE5.3+) but "
                     "version=%u — raising floor to 503 (UE5).", g_cachedUEVersion);
            ptrs.UEVersion    = 503;
            g_cachedUEVersion = 503;
        }
        // Property marker: UCharacterMovementComponent::GravityDirection (a reflected
        // FVector StructProperty) was added in UE5.4. Only probe for UE5 games (the
        // structural floor above already implies >=503), and only when below 504, so
        // genuine UE4 games never pay the GObjects walk. Best-effort + bounded.
        if (DynOff::bUseFProperty && g_cachedUEVersion >= 500 && g_cachedUEVersion < 504) {
            auto cmcSet = Aura::FindInstancesByClass("CharacterMovementComponent", false, 3);
            for (const auto& r : cmcSet.results) {
                if (!r.addr) continue;
                uintptr_t cls = Ubel::GetClass(r.addr);
                if (cls && Ubel::FindFieldOffset(cls, "GravityDirection", "GravityDirection",
                                                 nullptr, "StructProperty") >= 0) {
                    LOG_WARN("UE5_Init: property marker (CMC::GravityDirection) = UE5.4+ — "
                             "raising version %u -> 504.", g_cachedUEVersion);
                    ptrs.UEVersion    = 504;
                    g_cachedUEVersion = 504;
                    break;
                }
            }
        }
        // Structural marker: the UE5.7 reordered FUObjectItem — the standard 24-byte
        // item with the Object* moved to +0x08 (e.g. Solarpunk, the stock-UE5.7 repro).
        // Detected during the GObjects scan, so reliable even in a menu. The ==24 stride
        // guard excludes Avowed's CUSTOM 20-byte packed layout (UE5.3 — not a version
        // signal). The "unverified" packed reconstruction (IsPacked, never seen in a
        // real game) would also be 24B at +0x00; this offset test stays specific to the
        // reorder.
        if (DynOff::bUseFProperty && g_cachedUEVersion < 507
            && Aura::GetItemObjOffset() == 0x08 && Aura::GetItemSize() == 24) {
            LOG_WARN("UE5_Init: structural marker (FUObjectItem Object@+0x08, 24B = UE5.7+) "
                     "but version=%u — raising floor to 507.", g_cachedUEVersion);
            ptrs.UEVersion    = 507;
            g_cachedUEVersion = 507;
        }
    } else {
        LOG_WARN("UE5_Init: Partial init — GObjects=%s GNames=%s — skipping offset validation",
                 ptrs.GObjects ? "OK" : "MISSING", ptrs.GNames ? "OK" : "MISSING");
    }

    // --- GWorld recovery (Avowed / Obsidian UE5.3) ---
    // GWorld's AOB can land on a decoy (like GObjects did). Validate that *GWorld is a
    // UObject whose class is "World"; if not, recover in two stages: (1) find a live UWorld
    // instance in GObjects and the static .data pointer to it (ExtraScanGWorld); (2) if no
    // static slot exists, fall back to the engine object graph — GEngine->GameViewport->&World
    // (RecoverGWorldViaEngine, a live engine-updated UWorld* field). Needs a working GObjects +
    // offsets, so it runs after the GObjects recovery + offset validation above. Gated on a real
    // object array, so titles with a correct GWorld are unaffected. NOT gated on
    // ptrs.GWorld != 0: the hardened ValidateGWorldBasic now REJECTS a decoy during the
    // scan, so GWorld can legitimately be 0 here (no valid AOB, e.g. Avowed) and still
    // needs recovery — the guarded ReadSafe below leaves gworldOk=false → recovery runs.
    if (ptrs.GObjects && ptrs.GNames && Aura::GetCount() > 0) {
        bool gworldOk = false;
        uintptr_t uworld = 0;
        if (ptrs.GWorld && Macht::ReadSafe(ptrs.GWorld, uworld) && uworld) {
            uintptr_t cls = 0;
            if (Macht::ReadSafe(uworld + Grimoire::OFF_UOBJECT_CLASS, cls) && cls) {
                uint32_t cn = 0;
                if (Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, cn) &&
                    Serie::GetString(cn) == "World") {
                    gworldOk = true;
                }
            }
        }

        // TEST HOOK — the recovery path is otherwise only reachable on a game whose GWorld
        // AOB genuinely fails. Set UE5DUMP_FORCE_GWORLD_RECOVERY in the GAME process env to
        // exercise it on demand:
        //   =1       force the full recovery chain (ExtraScanGWorld, then engine path)
        //   =engine  also skip ExtraScanGWorld -> go straight to RecoverGWorldViaEngine
        // Non-destructive: if recovery fails the valid AOB GWorld is left untouched.
        char forceEnv[32] = {0};
        DWORD forceLen = GetEnvironmentVariableA("UE5DUMP_FORCE_GWORLD_RECOVERY", forceEnv, sizeof(forceEnv));
        bool forceRecovery   = (forceLen > 0 && forceLen < sizeof(forceEnv));
        bool forceEngineOnly = forceRecovery && _stricmp(forceEnv, "engine") == 0;
        if (forceRecovery && gworldOk) {
            LOG_WARN("UE5_Init: TEST HOOK UE5DUMP_FORCE_GWORLD_RECOVERY=%s — forcing recovery (AOB GWorld was valid)",
                     forceEnv);
            gworldOk = false;
        }

        if (!gworldOk) {
            LOG_WARN("UE5_Init: GWorld=0x%llX does not deref to a UWorld — recovering...",
                     static_cast<unsigned long long>(ptrs.GWorld));
            uintptr_t   rec    = 0;
            const char* method = nullptr;
            if (!forceEngineOnly) {
                rec = Genau::ExtraScanGWorld();          // primary: static .data slot
                if (rec) method = "instance_scan_recovery";
            }
            if (!rec) {
                rec = Genau::RecoverGWorldViaEngine();    // gap-fill: live GameViewport->World slot
                if (rec) method = "engine_recovery";
            }
            if (rec) {
                ptrs.GWorld = rec;
                g_cachedGWorld = rec;
                g_cachedGWorldMethod = method;
                g_cachedGWorldPatternId = nullptr;       // the GWLD_* AOB found a decoy — drop its stale label
                // The AOB symbol/scan fields would otherwise still describe the (decoy) AOB
                // match, leaving the UI's GWorld "AOB" symbol toggle wrongly enabled (it keys
                // on a non-empty gworld_aob via IsAobSymbolAvailable) and the Pointers panel
                // still showing the decoy's "AOB: <scan addr>" line. Recovery slots are not
                // static module symbols, so AOB-symbol export / disasm do not apply. Clearing
                // these makes the toggle auto-uncheck + gray out and the scan-addr line hide,
                // re-evaluated on the next connect; the UI shows the recovery method instead.
                g_cachedGWorldAob      = nullptr;
                g_cachedGWorldAobPos   = 0;
                g_cachedGWorldAobLen   = 0;
                g_cachedGWorldScanAddr = 0;
                LOG_INFO("UE5_Init: GWorld recovered via %s -> 0x%llX",
                         method, static_cast<unsigned long long>(rec));
            } else {
                LOG_WARN("UE5_Init: GWorld recovery failed (no static slot, no engine/viewport path)");
            }
        }
    }

    // A CE Disable that landed while this scan was running has already cleared the
    // latch (UE5_Shutdown) and torn the server down, and every cancellable loop above
    // bailed early on Tot::Requested() — so these results are partial by construction.
    // Latching true now would make the next enable's UE5_Init short-circuit and run the
    // whole session on them. Refuse the latch instead; the next enable re-scans. Safe
    // against a false positive: UE5_AutoStart calls Tot::ResetShutdown() before this.
    //
    // (MA1) The same argument now covers a PER-COMMAND cancel — a pipe client dropping
    // mid-scan — which this guard could not see, because it tests g_shutdown only.
    // ⚠ Do NOT "simplify" the added term to Tot::Requested(). g_perCommand stays latched
    // until Fern::AcceptLoop's firstConn calls ResetPerCommand, so a stale flag from an
    // EARLIER disconnect would make the auto-start UE5_Init refuse the latch on a scan that
    // completed perfectly — permanently disabling the DLL for that process. The note above
    // about ResetShutdown protecting against a false positive has no g_perCommand
    // equivalent; that asymmetry is the trap. `ptrs.bScanCancelled` says "THIS run actually
    // aborted", which is the predicate the guard wanted all along.
    if (Tot::ShutdownRequested() || ptrs.bScanCancelled) {
        LOG_WARN("UE5_Init: scan was cancelled (%s) — results are partial, "
                 "NOT latching initialized so the next enable re-scans",
                 Tot::ShutdownRequested() ? "shutdown" : "client disconnect");
        Sein::SetChannel(LogChannel::Pipe);
        return false;
    }

    s_initialized.store(true, std::memory_order_release);
    LOG_INFO("UE5_Init: Complete (UE%u, GObjects=0x%llX, GNames=0x%llX, Objects=%d)",
             ptrs.UEVersion,
             static_cast<unsigned long long>(ptrs.GObjects),
             static_cast<unsigned long long>(ptrs.GNames),
             Aura::GetCount());

    // Condensed summary for quick scan-log triage
    LOG_SUMMARY("build=%s config=%s UE=%u",
                BuildStamp::GitShort(), BuildStamp::Config(), ptrs.UEVersion);
    LOG_SUMMARY("GObjects=0x%llX GNames=0x%llX GWorld=0x%llX Objects=%d",
                static_cast<unsigned long long>(ptrs.GObjects),
                static_cast<unsigned long long>(ptrs.GNames),
                static_cast<unsigned long long>(ptrs.GWorld),
                Aura::GetCount());
    // validated= is now HONEST: it used to be stored true on the give-up paths too, so a
    // run could print validated=yes three lines after "Offset validation failed — using
    // default offsets". When it is no, say WHY, because the offsets below are then
    // unmeasured defaults rather than probe results.
    const bool offValidated = DynOff::bOffsetsValidated.load(std::memory_order_acquire);
    LOG_SUMMARY("DynOff: CPN=%s FProp=%s TagFFV=%s Outer=+0x%02X validated=%s%s%s",
                DynOff::bCasePreservingName ? "yes" : "no",
                DynOff::bUseFProperty ? "yes" : "no",
                DynOff::bTaggedFFieldVariant ? "yes" : "no",
                DynOff::UOBJECT_OUTER,
                offValidated ? "yes" : "NO (DEFAULTS)",
                offValidated ? "" : " reason=",
                offValidated ? "" : DynOff::g_offsetsFallbackReason);
    LOG_SUMMARY("  UStruct: Super=+0x%02X Children=+0x%02X ChildProps=+0x%02X PropsSize=+0x%02X",
                DynOff::USTRUCT_SUPER, DynOff::USTRUCT_CHILDREN,
                DynOff::USTRUCT_CHILDPROPS, DynOff::USTRUCT_PROPSSIZE);
    if (DynOff::bUseFProperty) {
        // FCName is here because its absence from every diagnostic channel is what made the
        // UE 5.8 drill-down failure cost a full RE session — it was the only UE offset
        // neither probed nor printed.
        LOG_SUMMARY("  FField: Next=+0x%02X Name=+0x%02X | FProp: Offset=+0x%02X ElemSize=+0x%02X StructProp=+0x%02X | FCName=+0x%02X",
                    DynOff::FFIELD_NEXT, DynOff::FFIELD_NAME,
                    DynOff::FPROPERTY_OFFSET, DynOff::FPROPERTY_ELEMSIZE, DynOff::FSTRUCTPROP_STRUCT,
                    DynOff::FFIELDCLASS_NAME);
    } else {
        LOG_SUMMARY("  UProperty: Next=+0x%02X Offset=+0x%02X ElemSize=+0x%02X",
                    DynOff::UFIELD_NEXT, DynOff::UPROPERTY_OFFSET, DynOff::UPROPERTY_ELEMSIZE);
    }

    ScanProgress::Set(7, "Complete");

    // Switch to Pipe channel — all subsequent runtime logging goes to pipe file
    Sein::SetChannel(LogChannel::Pipe);

    return true;
}

void UE5_Shutdown() {
    LOG_INFO("UE5_Shutdown: Cleaning up...");
    // Enter the shutdown window up-front: (a) in-flight scans bail so the worker
    // joins below complete fast, and (b) every module's StartWorker* refuses to
    // (re)spawn, so a pipe/mailbox command landing between the joins and the pipe
    // stop can't revive a just-joined worker that nothing would join again. Cleared
    // by Fern::Start (ResetShutdown) on re-enable in the same process. (M5)
    Tot::RequestShutdown();
    Mimic::StopThread();
    Solitar::StopWorker();   // join the GodMode re-assert worker before unload
    Laufen::StopWorker();    // join the movement-tuning re-assert worker before unload
    Hemmung::StopWorker();   // join the time-dilation re-assert worker before unload
    Solide::StopWorker();    // join the force-field hold worker before unload
    Dunste::StopWorker();    // join the fly worker before unload
    Schlacht::SetEnabled(false);  // un-hide occluders (contract) THEN join the worker; runs before Stark::Shutdown so the un-hide invokes can still dispatch (M3)
    Grausam::SetForegroundLock(false);  // soft-disable: g_enabled=false so the GFW/WndProc hooks pass through + release the masked cursor clip — otherwise the game believes it's foreground forever after a CE-Lua Disable (L10)
    // Full teardown: RemoveHook + MH_Uninitialize + drain pending invoke queue.
    // Pipe server is stopped after Shutdown() so any in-flight pipe thread
    // blocked on EnqueueInvoke receives its -7 result and unwinds cleanly.
    Stark::Shutdown();
    s_pipeServer.Stop();
    // Deliberately NOT under s_initMutex: taking it here would make a Disable block for
    // the full remaining scan, re-creating the wedged-teardown shape B49 just fixed.
    // Tot::RequestShutdown() above is the interlock — a scan still running past this
    // point sees it and refuses to latch (see UE5_Init).
    s_initialized.store(false, std::memory_order_release);
    // The pipe is gone, so stop advertising READY — a CE Lua re-enable after a
    // Disable must wait for the fresh auto-start rather than read a stale flag.
    g_invokeMailbox.initState = Mimic::INIT_IDLE;
}

uint32_t UE5_GetVersion() {
    // Lazy UE5.5 / 5.6 refines off markers discovered during walks / enum access (the
    // structural item + property markers ran at init; these two only surface later):
    //   • a reflected Utf8StrProperty / AnsiStrProperty (Ubel flag) ⇒ UE5.5+
    //   • the FNameData struct-of-arrays UEnum::Names container (DynOff flag, set by
    //     the lazy DetectUEnumNames) ⇒ UE5.6+ (this is the layout whose enum bug the
    //     UE5.6+ Neu reader fixed; e.g. Titan Quest II).
    // Monotonic + UE5-only (never lowers, never touches a UE4 label). Cheap reads; the
    // UI polls this for the badge, so the version self-corrects as the user browses.
    if (g_cachedUEVersion >= 500 && g_cachedUEVersion < 506) {
        uint32_t floor = g_cachedUEVersion;
        if (Ubel::SawUtf8OrAnsiStr() && floor < 505)          floor = 505;
        if (DynOff::bEnumNamesNewContainer && floor < 506)    floor = 506;
        if (floor != g_cachedUEVersion) {
            LOG_INFO("UE5_GetVersion: marker refine %u -> %u (UE5.5/5.6 type/enum marker).",
                     g_cachedUEVersion, floor);
            g_cachedUEVersion = floor;
        }
    }
    return g_cachedUEVersion;
}

uintptr_t UE5_GetGObjectsAddr() {
    return g_cachedGObjects;
}

uintptr_t UE5_GetGNamesAddr() {
    return g_cachedGNames;
}

void UE5_SetObjectDecryption(uintptr_t (*decryptFunc)(uintptr_t)) {
    Aura::SetDecryptFunc(decryptFunc);
    LOG_INFO("UE5_SetObjectDecryption: %s",
             decryptFunc ? "Custom decryption set" : "Decryption cleared");
}

int32_t UE5_GetObjectCount() {
    return Aura::GetCount();
}

uintptr_t UE5_GetObjectByIndex(int32_t index) {
    return Aura::GetByIndex(index);
}

bool UE5_GetObjectName(uintptr_t obj, char* buf, int32_t bufLen) {
    std::string name = Ubel::GetName(obj);
    return CopyToBuffer(name, buf, bufLen);
}

bool UE5_GetObjectFullName(uintptr_t obj, char* buf, int32_t bufLen) {
    std::string name = Ubel::GetFullName(obj);
    return CopyToBuffer(name, buf, bufLen);
}

uintptr_t UE5_GetObjectClass(uintptr_t obj) {
    return Ubel::GetClass(obj);
}

uintptr_t UE5_GetObjectOuter(uintptr_t obj) {
    return Ubel::GetOuter(obj);
}

uintptr_t UE5_FindObject(const char* fullPath) {
    if (!fullPath) return 0;
    return Aura::FindByName(fullPath);
}

uintptr_t UE5_FindClass(const char* className) {
    if (!className) return 0;

    uintptr_t result = 0;
    Aura::ForEach([&](int32_t /*idx*/, uintptr_t obj) -> bool {
        uintptr_t cls = Ubel::GetClass(obj);
        if (!cls) return true;

        std::string clsName = Ubel::GetName(cls);
        if (clsName == "Class") {
            std::string objName = Ubel::GetName(obj);
            if (objName == className) {
                result = obj;
                return false;
            }
        }
        return true;
    });
    return result;
}

int32_t UE5_WalkClassBegin(uintptr_t uclassAddr) {
    std::lock_guard<std::mutex> lock(s_walkMutex);
    s_walkCache = Ubel::WalkClass(uclassAddr);
    return static_cast<int32_t>(s_walkCache.Fields.size());
}

bool UE5_WalkClassGetField(int32_t index,
                           uintptr_t* outAddr,
                           char* nameOut, int32_t nameBufLen,
                           char* typeOut, int32_t typeBufLen,
                           int32_t* offsetOut,
                           int32_t* sizeOut)
{
    std::lock_guard<std::mutex> lock(s_walkMutex);
    if (index < 0 || index >= static_cast<int32_t>(s_walkCache.Fields.size())) return false;

    const auto& field = s_walkCache.Fields[index];
    if (outAddr)   *outAddr   = field.Address;
    if (offsetOut) *offsetOut = field.Offset;
    if (sizeOut)   *sizeOut   = field.Size;

    CopyToBuffer(field.Name, nameOut, nameBufLen);
    CopyToBuffer(field.TypeName, typeOut, typeBufLen);

    return true;
}

void UE5_WalkClassEnd() {
    std::lock_guard<std::mutex> lock(s_walkMutex);
    s_walkCache = ClassInfo{};
}

bool UE5_ResolveFName(uint64_t fname, char* buf, int32_t bufLen) {
    int32_t compIndex = static_cast<int32_t>(fname & 0xFFFFFFFF);
    int32_t number    = static_cast<int32_t>((fname >> 32) & 0xFFFFFFFF);

    std::string name = Serie::GetString(compIndex, number);
    return CopyToBuffer(name, buf, bufLen);
}

// ── Auto-start: the WORK, and the two ways to reach it ──────────────────────
//
// This used to be the body of UE5_AutoStart(), which every caller ran to
// completion. That is a multi-second job (DllMain + Sein::Init + a 2-8 s AOB
// scan + pipe start), and one of the callers is Cheat Engine's remote injection
// stub — which does not wait that long (audit #5 AB2):
//
//   CEFuncProc.pas:1346-1360   createremotethread, then a wait loop of
//                              `counter := 10000 div 10` × 10 ms = a HARD,
//                              unconfigurable 10 s ceiling.
//   CEFuncProc.pas:1332-1343   with Settings' `cbInjectDLLWithAPC` ticked there
//                              is no wait at all — CreateRemoteAPC then a flat
//                              `sleep(1000)`.
//   CEFuncProc.pas:1379-1387   `finally ... virtualfreeex(processhandle,
//                              injectionlocation, 0, MEM_RELEASE)` — UNCONDITIONAL
//                              on both paths.
//
// So the stub page is released while our function is still running on it, and
// the eventual `ret` lands on freed memory. **The victim is the GAME process**,
// not CE — and note AB1's module pin does not help: that protects our image in
// our address space, whereas this is CE's own VirtualAllocEx page in the game.
//
// The same shape reaches us a second way: generated CE Lua calls
// `callDLL('UE5_AutoStart')`, which goes through executeCodeEx — a
// WaitForSingleObject on the calling thread with no message pump, and its
// timeout path sets `dontfree := true` and leaks the stub permanently
// (docs/ce-plugin-sdk-notes.md §13).
//
// The fix is to make the exported entry point SPAWN AND RETURN, so the remote
// thread is long gone before either deadline. Readiness was already published
// through Mimic::InitState and the emitted scripts already poll it
// (CeReadinessLua::AppendPollLoop), so no caller loses information — the Lua
// inject path never passed a function name to injectDLL in the first place and
// has always relied on that poll.
static bool AutoStartWork() {
    LOG_INFO("UE5_AutoStart: entry");

    // Re-arm after a CE Disable. UE5_Shutdown latches Tot::RequestShutdown() and
    // joins the mailbox poller, and Mimic::StartThread's only other caller is
    // DllMain — which never runs twice — so without these two lines a re-enable
    // came back with no mailbox at all and the session was unrecoverable without
    // restarting the game (audit #4 B1(b)).
    //
    // Both must happen HERE, not later. Tot::ResetShutdown is otherwise only
    // reached from Fern::Start, which runs *after* UE5_Init below: a re-enable
    // would rescan with g_shutdown still latched (cancellable loops bail early)
    // and every module's StartWorker* would refuse to spawn, since that gate reads
    // the same flag. Both calls are no-ops on a first start.
    Tot::ResetShutdown();
    // (G2) Same argument, other flag — and it is now load-bearing rather than tidy.
    // Tot::Requested() is ALSO true for g_perCommand, the mid-command client-disconnect
    // latch, which Tot deliberately keeps set until a fresh session connects into an
    // empty registry (Fern AcceptLoop firstConn) so an orphaned scan keeps aborting.
    // That reset therefore runs AFTER this scan, exactly like ResetShutdown's. Genau's
    // recovery sweeps now honour Tot::Requested(), so without this line a stale latch
    // from a previous session's UI disconnect would abort a healthy game's GObjects/
    // GNames recovery at offset 0 — a scan that never ran, reported as one that found
    // nothing. No-op on a first start.
    Tot::ResetPerCommand();
    Mimic::StartThread();   // early-returns when the poller is already running
    // Publish progress into the mailbox so a CE Lua poller can stop sleeping a
    // fixed budget and react the moment we are actually ready (Mimic::InitState).
    g_invokeMailbox.initState = Mimic::INIT_RUNNING;
    // UE5_Init CAN fail. The "Always succeeds" comment this replaces was correct
    // when it was written (af7ff3a deleted the old `if (!UE5_Init()) return false;`
    // for Extra Scan), but audit #4's B49 added a real `return false` at :528 five
    // months later — a shutdown landing during the multi-second scan bails with
    // NOTHING latched — and this call site was never revisited. (audit #5 D5/FR1)
    const bool inited = UE5_Init();
    if (!inited) {
        LOG_WARN("UE5_AutoStart: UE5_Init ABORTED (shutdown landed mid-scan) — "
                 "pointers are partial and nothing was latched");
        // That same shutdown ran Mimic::StopThread(), which memsets the mailbox and
        // joins the poller — and StartThread's only other caller is DllMain, which
        // never runs twice. A CE .CT row would then write commands nobody collects,
        // leaving status = 0, which CLAUDE.md's own rule tells the user means "stale
        // g_invokeMailbox address" — a confidently WRONG diagnosis.
        //
        // Re-arm only if the shutdown is no longer latched. Reviving the poller
        // while it IS still latched would fight the user's own untick, and
        // Tot::RequestShutdown is deliberately sticky. So this covers the transient
        // case and deliberately does NOT resurrect against a live shutdown.
        if (!Tot::ShutdownRequested()) Mimic::StartThread();
    }
    bool ok = UE5_StartPipeServer();
    // Publish only AFTER StartPipeServer returns — a poller that sees READY must
    // be able to connect immediately. READY now also requires init to have
    // COMPLETED: the enum defines it as "init finished AND the pipe server is up",
    // and publishing it over an aborted scan told every CE feature row it was safe
    // to proceed.
    g_invokeMailbox.initState = (ok && inited) ? Mimic::INIT_READY : Mimic::INIT_FAILED;
    LOG_INFO("UE5_AutoStart: pipe server %s, init %s -> initState=%d",
             ok ? "started" : "FAILED to start",
             inited ? "complete" : "ABORTED",
             static_cast<int>(g_invokeMailbox.initState));
    return ok && inited;
}

// One auto-start at a time. Both entry points funnel through here, and they DO
// overlap in the ordinary CE-plugin flow: LoadLibrary spawns DllMain's auto-start
// thread, and CE's stub then calls the export a moment later. Before, that raced
// two full scans past each other and was survived only incidentally (UE5_Init's
// s_initialized latch plus the pipe-exists probe). Now the second caller returns
// at once instead of duplicating a multi-second scan.
static std::atomic<bool> s_autoStartInFlight{false};

bool UE5_AutoStartBlocking() {
    bool expected = false;
    if (!s_autoStartInFlight.compare_exchange_strong(expected, true)) {
        LOG_INFO("UE5_AutoStart: a start is already in flight — not starting a second");
        return true;   // "an auto-start is happening", which is what the caller wanted
    }
    bool ok = false;
    try {
        ok = AutoStartWork();
    } catch (...) {
        // A throw must not leave the latch set, or the session can never re-arm
        // after a CE Disable/Enable. Routine::RunThreadGuarded catches it again
        // on the thread path; this covers the inline path too.
        LOG_ERROR("UE5_AutoStart: unhandled exception during auto-start");
    }
    s_autoStartInFlight.store(false);
    return ok;
}

bool UE5_AutoStart() {
    // Cheat Engine hosting us is the one case where a background thread is the
    // wrong answer (audit #5 AB1: CE FreeLibrary's plugin DLLs, and a thread left
    // running in an unmapped image crashes CE). There is also no remote stub to
    // outrun there — an in-process call has no deadline — so run inline.
    wchar_t hostPath[MAX_PATH] = {};
    const bool mayThread = (GetModuleFileNameW(nullptr, hostPath, MAX_PATH) == 0)
                           || HostAllowsBackgroundThreads(hostPath);
    if (!mayThread) {
        LOG_INFO("UE5_AutoStart: host is Cheat Engine — running inline, no thread");
        return UE5_AutoStartBlocking();
    }

    HANDLE h = CreateThread(nullptr, 0, [](LPVOID) -> DWORD {
        // A throw out of a raw thread proc is std::terminate (B14).
        Routine::RunThreadGuarded("UE5_AutoStart", [] { UE5_AutoStartBlocking(); });
        return 0;
    }, nullptr, 0, nullptr);

    if (!h) {
        // Falling back to inline is strictly better than not starting at all: the
        // caller may well be CE's stub, but a scan that finishes late beats a game
        // that never gets one, and the 10 s ceiling is a crash risk rather than a
        // certainty.
        LOG_ERROR("UE5_AutoStart: CreateThread failed (error=%lu) — running inline "
                  "(a caller with a deadline may see this as a timeout)", GetLastError());
        return UE5_AutoStartBlocking();
    }
    // Not joined anywhere: cancellation is Tot's job (UE5_Shutdown latches
    // Tot::RequestShutdown and the scan loops bail), and a joinable handle nobody
    // joins is just a leak.
    CloseHandle(h);

    LOG_INFO("UE5_AutoStart: launched on a background thread — returning immediately "
             "(poll Mimic::InitState in the mailbox for readiness)");
    return true;
}

// === Property Detail Queries (for CE Lua dissect) ===

int32_t UE5_GetFieldBoolMask(uintptr_t fieldAddr) {
    if (!fieldAddr) return 0;
    // FBoolProperty/UBoolProperty: { FieldSize(1), ByteOffset(1), ByteMask(1), FieldMask(1) }
    // FProperty (UE4.25+/UE5): at FBOOLPROP_FIELDSIZE (~0x78)
    // UProperty (UE4 <4.25):   at UBOOLPROP_FIELDSIZE (~0x70)
    int baseOff = DynOff::bUseFProperty ? DynOff::FBOOLPROP_FIELDSIZE : DynOff::UBOOLPROP_FIELDSIZE;
    for (int tryOff : { baseOff, baseOff - 4, baseOff + 4, baseOff + 8, baseOff - 8 }) {
        if (tryOff < 0) continue;
        uint8_t boolBytes[4] = {};
        if (Macht::ReadBytesSafe(fieldAddr + tryOff, boolBytes, 4)) {
            uint8_t fieldSize = boolBytes[0];
            uint8_t fieldMask = boolBytes[3];
            if (fieldSize >= 1 && fieldSize <= 8 && fieldMask != 0 && (fieldMask & (fieldMask - 1)) == 0) {
                return static_cast<int32_t>(fieldMask);
            }
        }
    }
    return 0;
}

uintptr_t UE5_GetFieldStructClass(uintptr_t fieldAddr) {
    if (!fieldAddr) return 0;
    // FStructProperty stores UScriptStruct* at DynOff::FSTRUCTPROP_STRUCT.
    constexpr int kDeltas[] = { 0, -8, 8, -16, 16, 4, -4, 12 };
    for (int delta : kDeltas) {
        int tryOff = DynOff::FSTRUCTPROP_STRUCT + delta;
        if (tryOff < 0) continue;
        uintptr_t structPtr = 0;
        if (Macht::ReadSafe(fieldAddr + tryOff, structPtr) && structPtr) {
            std::string sname = Ubel::GetName(structPtr);
            if (!sname.empty() && sname != "None") return structPtr;
        }
    }
    return 0;
}

uintptr_t UE5_GetFieldPropertyClass(uintptr_t fieldAddr) {
    // FObjectPropertyBase::PropertyClass sits at the same offset as
    // FStructProperty::Struct (DynOff::FSTRUCTPROP_STRUCT).
    // Delegate to the same probe logic — both store a UClass*/UScriptStruct*.
    return UE5_GetFieldStructClass(fieldAddr);
}

int32_t UE5_GetClassPropsSize(uintptr_t classAddr) {
    if (!classAddr) return 0;
    int32_t propsSize = 0;
    Macht::ReadSafe(classAddr + DynOff::USTRUCT_PROPSSIZE, propsSize);
    return propsSize;
}

// === UFunction Invocation ===

uintptr_t UE5_FindInstanceOfClass(const char* className) {
    if (!className || !className[0]) return 0;

    auto rset = Aura::FindInstancesByClass(className, false, 100);

    // Prefer non-CDO instance
    for (const auto& r : rset.results) {
        if (r.addr && r.name.find("Default__") == std::string::npos) {
            LOG_INFO("UE5_FindInstanceOfClass: '%s' -> 0x%llX (%s)",
                     className, (unsigned long long)r.addr, r.name.c_str());
            return r.addr;
        }
    }

    // Fallback: return first result even if CDO
    if (!rset.results.empty() && rset.results[0].addr) {
        LOG_WARN("UE5_FindInstanceOfClass: '%s' -> only CDO: 0x%llX (%s)",
                 className, (unsigned long long)rset.results[0].addr,
                 rset.results[0].name.c_str());
        return rset.results[0].addr;
    }

    LOG_WARN("UE5_FindInstanceOfClass: '%s' -> not found (scanned=%d)",
             className, rset.scanned);
    return 0;
}

uintptr_t UE5_FindFunctionByName(uintptr_t classAddr, const char* funcName) {
    if (!classAddr || !funcName || !funcName[0]) return 0;

    auto funcs = Ubel::WalkFunctions(classAddr);

    // Exact match
    for (const auto& f : funcs) {
        if (f.name == funcName) {
            LOG_INFO("UE5_FindFunctionByName: '%s' -> 0x%llX (exact match)",
                     funcName, (unsigned long long)f.address);
            return f.address;
        }
    }

    // Case-insensitive fallback
    std::string lower(funcName);
    std::transform(lower.begin(), lower.end(), lower.begin(),
                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    for (const auto& f : funcs) {
        std::string fl = f.name;
        std::transform(fl.begin(), fl.end(), fl.begin(),
                       [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        if (fl == lower) {
            LOG_INFO("UE5_FindFunctionByName: '%s' -> 0x%llX (case-insensitive)",
                     funcName, (unsigned long long)f.address);
            return f.address;
        }
    }

    LOG_WARN("UE5_FindFunctionByName: '%s' not found (%d functions walked)",
             funcName, (int)funcs.size());
    return 0;
}

// ============================================================================
// Debug Camera robust enable/disable (shared by UI pipe + CE Lua)
//
// ToggleDebugCamera is a stateful toggle whose direction depends on internal
// CheatManager / DebugCameraController state. Some Shipping builds (e.g. Geri,
// UE4.27) strip DisableDebugCamera's cleanup, so the toggle can enable but
// never disable — the camera gets stuck in free-fly. These two exports give
// the UI (via the set_debug_camera pipe cmd) and CE Lua (via a direct export
// call from an AA [ENABLE]/[DISABLE] block) ONE shared, version-agnostic
// "force ON / force OFF": read the real active state, toggle only if needed,
// and when the toggle can't disable, hand-roll UPlayer::SwitchController to
// switch the local player's controller back to the original PlayerController.
//
// All field offsets are resolved live from reflection (Ubel::WalkClassEx) —
// nothing hardcoded, so this is UE4/UE5 version-agnostic.
// ============================================================================

// Resolve an ObjectProperty field offset by name within a class (inherited
// fields included). Thin wrapper over the shared Ubel::FindFieldOffset
// (extracted from here, build 1015) — the fuzzy branch stays
// ObjectProperty-only so DebugCameraControllerRef never matches the
// always-non-null DebugCameraControllerClass TSubclassOf slot.
// Returns -1 if not found.
static int DbgCam_FieldOffset(uintptr_t classAddr, const char* exact,
                              const char* contains, const char* excluding) {
    return Ubel::FindFieldOffset(classAddr, exact, contains, excluding, "ObjectProperty");
}

static uintptr_t DbgCam_ReadPtr(uintptr_t obj, int off) {
    if (!obj || off < 0) return 0;
    uintptr_t v = 0;
    Macht::ReadSafe(obj + (uintptr_t)off, v);
    return v;
}

static bool DbgCam_WritePtr(uintptr_t obj, int off, uintptr_t val) {
    if (!obj || off < 0) return false;
    return Macht::WriteBytes(obj + (uintptr_t)off, &val, sizeof(val));
}

// Two-hop active-state read matching what ToggleDebugCamera branches on:
//   Hop 1: CheatManager.DebugCameraControllerRef — a DCC exists? (this member
//          PERSISTS after disable in modern UE, so it is NOT the active flag).
//   Hop 2: DebugCameraController.OriginalControllerRef — the real active flag
//          (set on activate, nulled on deactivate).
// Returns 1=ON, 0=OFF, -1=unknown. Outputs the CheatManager + DCC for reuse.
static int DbgCam_ReadState(uintptr_t& outCm, uintptr_t& outDcc) {
    outCm = 0; outDcc = 0;
    uintptr_t cm = UE5_FindInstanceOfClass("CheatManager");
    outCm = cm;   // may be 0 (some titles spawn it lazily)

    // Hop 1: CheatManager.DebugCameraControllerRef.
    uintptr_t dcc = 0;
    if (cm) {
        uintptr_t cmClass = UE5_GetObjectClass(cm);
        int refOff = DbgCam_FieldOffset(cmClass, "DebugCameraControllerRef",
                                        "DebugCamera", "Class");
        if (refOff >= 0) dcc = DbgCam_ReadPtr(cm, refOff);
    }
    // Fallback: DebugCameraControllerRef can be null even with the camera live —
    // some titles don't populate it (DQIII HD-2D), or there are several
    // CheatManagers and FindInstanceOfClass picked the wrong one. Find the DCC by
    // instance scan; its OriginalControllerRef is the authoritative active flag.
    if (!dcc) {
        dcc = UE5_FindInstanceOfClass("DebugCameraController");
        if (dcc)
            LOG_INFO("DbgCam_ReadState: DCC 0x%llX via instance scan "
                     "(CheatManager ref empty)", (unsigned long long)dcc);
    }
    if (!dcc) return cm ? 0 : -1;   // none ⇒ OFF (or unknown if no CheatManager)
    outDcc = dcc;
    uintptr_t dccClass = UE5_GetObjectClass(dcc);
    int origOff = DbgCam_FieldOffset(dccClass, "OriginalControllerRef",
                                     "OriginalController", nullptr);
    if (origOff < 0) return -1;
    return DbgCam_ReadPtr(dcc, origOff) ? 1 : 0;
}

// Hand-roll UCheatManager::DisableDebugCamera when the game's toggle won't:
// switch the local player's active controller back to the original PC.
// Returns true if it wrote (camera should restore), false if it bailed without
// writing (any required field/pointer missing → never write a partial state).
static bool DbgCam_SwapControllerBack(uintptr_t dcc) {
    if (!dcc) return false;
    uintptr_t dccClass = UE5_GetObjectClass(dcc);
    int origOff   = DbgCam_FieldOffset(dccClass, "OriginalControllerRef",
                                       "OriginalController", nullptr);
    int playerOff = DbgCam_FieldOffset(dccClass, "Player", "Player", "Controller");
    int origPlOff = DbgCam_FieldOffset(dccClass, "OriginalPlayer",
                                       "OriginalPlayer", nullptr);
    if (origOff < 0 || playerOff < 0) return false;

    uintptr_t origPc      = DbgCam_ReadPtr(dcc, origOff);
    uintptr_t localPlayer = DbgCam_ReadPtr(dcc, playerOff);
    if (!origPc || !localPlayer) return false;   // not actually possessing

    uintptr_t lpClass = UE5_GetObjectClass(localPlayer);
    int pcOff = DbgCam_FieldOffset(lpClass, "PlayerController", "PlayerController", nullptr);
    if (pcOff < 0) return false;

    // Mirror UPlayer::SwitchController(origPC) + OnDeactivate cleanup.
    DbgCam_WritePtr(localPlayer, pcOff,     origPc);      // 1. view follows origPC (critical)
    DbgCam_WritePtr(origPc,      playerOff, localPlayer); // 2. origPC.Player = LP
    DbgCam_WritePtr(dcc,         playerOff, 0);           // 3. DCC.Player = null
    DbgCam_WritePtr(dcc,         origOff,   0);           // 4a. clear active flag
    if (origPlOff >= 0)
        DbgCam_WritePtr(dcc,     origPlOff, 0);           // 4b. clear OriginalPlayer

    LOG_INFO("DbgCam_SwapControllerBack: LP=0x%llX PC<-0x%llX "
             "(DCC=0x%llX pcOff=0x%X playerOff=0x%X)",
             (unsigned long long)localPlayer, (unsigned long long)origPc,
             (unsigned long long)dcc, pcOff, playerOff);
    return true;
}

int32_t UE5_GetDebugCameraState() {
    uintptr_t cm = 0, dcc = 0;
    return DbgCam_ReadState(cm, dcc);
}

int32_t UE5_SetDebugCamera(int32_t enable) {
    bool want = (enable != 0);
    uintptr_t cm = 0, dcc = 0;
    int state = DbgCam_ReadState(cm, dcc);
    if (state < 0) {
        LOG_WARN("UE5_SetDebugCamera: no readable CheatManager/DebugCamera state");
        return -1;
    }
    if ((state == 1) == want) {
        LOG_INFO("UE5_SetDebugCamera: already %s — no-op", want ? "ON" : "OFF");
        return state;
    }

    // Known state, opposite of desired → fire ToggleDebugCamera once.
    uintptr_t cmClass = UE5_GetObjectClass(cm);
    uintptr_t ufunc = UE5_FindFunctionByName(cmClass, "ToggleDebugCamera");
    if (!ufunc) {
        LOG_WARN("UE5_SetDebugCamera: ToggleDebugCamera UFunction not found");
        return -1;
    }
    int32_t r = UE5_CallProcessEvent(cm, ufunc, 0);
    if (r != 0) {
        LOG_WARN("UE5_SetDebugCamera: ToggleDebugCamera ProcessEvent r=%d", r);
        return -1;
    }

    state = DbgCam_ReadState(cm, dcc);

    // Escalation: the toggle fired OK but couldn't disable (stripped
    // DisableDebugCamera) → switch the controller back by hand.
    if (!want && state == 1 && dcc) {
        if (DbgCam_SwapControllerBack(dcc))
            state = DbgCam_ReadState(cm, dcc);
    }
    LOG_INFO("UE5_SetDebugCamera(%d) -> state=%d", enable, state);
    return state;
}

// ============================================================================
// Teleport (Wirbel) — thin export wrappers. All logic lives in Wirbel.cpp;
// the pipe (Fern) and the CE mailbox (Mimic CMD_TELEPORT=8) share these.
// docs/teleport-spec.md is the design contract.
// ============================================================================

static void Teleport_CopyPose(const Wirbel::Pose& p, double* out6) {
    if (!out6) return;
    out6[0] = p.X;     out6[1] = p.Y;   out6[2] = p.Z;
    out6[3] = p.Pitch; out6[4] = p.Yaw; out6[5] = p.Roll;
}

int32_t UE5_TeleportGetPose(double* outPose6, char* outMapName, int32_t mapNameCap) {
    Wirbel::Pose p{};
    int32_t rc = Wirbel::GetPose(p, outMapName, mapNameCap, nullptr);
    if (rc == 0) Teleport_CopyPose(p, outPose6);
    return rc;
}

int32_t UE5_TeleportSaveMarker(int32_t slot) {
    return Wirbel::SaveMarker(slot);
}

int32_t UE5_TeleportRecallMarker(int32_t slot, int32_t force) {
    return Wirbel::RecallMarker(slot, force != 0, nullptr);
}

int32_t UE5_TeleportToCursor(double zOffset, int32_t traceChannel,
                             int32_t fallbackToCenter) {
    return Wirbel::TeleportToCursor(zOffset, traceChannel, fallbackToCenter != 0,
                                    nullptr, nullptr, nullptr);
}

int32_t UE5_TeleportGetMarker(int32_t slot, double* outPose6,
                              char* outMapName, int32_t mapNameCap) {
    Wirbel::Marker m{};
    int32_t rc = Wirbel::GetMarker(slot, m);
    if (rc == 0) {
        Teleport_CopyPose(m.P, outPose6);
        if (outMapName && mapNameCap > 0)
            CopyToBuffer(m.MapName, outMapName, mapNameCap);
    }
    return rc;
}

int32_t UE5_TeleportClearMarker(int32_t slot) {
    return Wirbel::ClearMarker(slot);
}

int32_t UE5_TeleportRecallLast() {
    return Wirbel::RecallLast(nullptr);
}

int32_t UE5_TeleportGetLast(double* outPose6, char* outMapName, int32_t mapNameCap) {
    Wirbel::Marker m{};
    int32_t rc = Wirbel::GetLast(m);
    if (rc == 0) {
        Teleport_CopyPose(m.P, outPose6);
        if (outMapName && mapNameCap > 0)
            CopyToBuffer(m.MapName, outMapName, mapNameCap);
    }
    return rc;
}

int32_t UE5_TeleportGetPov(double* outPov11) {
    Wirbel::Pov pov{};
    int32_t rc = Wirbel::GetPov(pov);
    if (rc == 0 && outPov11) {
        outPov11[0] = pov.Cam.X;     outPov11[1] = pov.Cam.Y;   outPov11[2] = pov.Cam.Z;
        outPov11[3] = pov.Cam.Pitch; outPov11[4] = pov.Cam.Yaw; outPov11[5] = pov.Cam.Roll;
        outPov11[6] = pov.Fov;
        outPov11[7] = pov.Pawn.X;    outPov11[8] = pov.Pawn.Y;  outPov11[9] = pov.Pawn.Z;
        outPov11[10] = pov.HasPawn ? 1.0 : 0.0;
    }
    return rc;
}

int32_t UE5_TeleportRelative(double distance, int32_t horizontalOnly,
                             double* outNewPose6) {
    Wirbel::Pose p{};
    int32_t rc = Wirbel::TeleportRelative(distance, horizontalOnly != 0, p, nullptr);
    if (rc == 0) Teleport_CopyPose(p, outNewPose6);
    return rc;
}

int32_t UE5_TeleportRecallExplicit(double x, double y, double z,
                                   double pitch, double yaw, double roll,
                                   int32_t hasRot) {
    Wirbel::Pose p{};
    p.X = x; p.Y = y; p.Z = z;
    p.Pitch = pitch; p.Yaw = yaw; p.Roll = roll;
    return Wirbel::RecallExplicit(p, hasRot != 0, nullptr);
}

int32_t UE5_SetMouseCursor(int32_t show, int32_t* outState) {
    bool state = false;
    int32_t rc = Wirbel::SetMouseCursor(show != 0, &state);
    if (outState) *outState = state ? 1 : 0;
    return rc;
}

int32_t UE5_GetMouseCursor(int32_t* outState) {
    bool state = false;
    int32_t rc = Wirbel::GetMouseCursor(&state);
    if (outState) *outState = state ? 1 : 0;
    return rc;
}

// === GodMode (Solitar) ===

int32_t UE5_SetGodMode(int32_t enable) {
    return Solitar::SetGodMode(enable != 0);
}

int32_t UE5_GetGodMode() {
    return Solitar::GetGodMode();
}

int32_t UE5_GetProtectState(int32_t* outWant, int32_t* outLive, int32_t* outResolvable) {
    Solitar::State st{};
    int32_t rc = Solitar::GetState(st);
    if (outWant)       *outWant = st.want;
    if (outLive)       *outLive = st.live;
    if (outResolvable) *outResolvable = st.resolvable ? 1 : 0;
    return rc;
}

// === Movement tuning (Laufen) ===

int32_t UE5_SetMovementPercent(int32_t knobId, double percent) {
    return Laufen::SetKnobPercent(knobId, percent);
}

int32_t UE5_SetGravityDirection(double x, double y, double z) {
    return Laufen::SetGravityDirection(x, y, z);   // (0,0,0) = reset
}

// === Time dilation (Hemmung) ===

int32_t UE5_SetTimeDilation(int32_t target, double value) {
    return Hemmung::SetDilation(target, value);
}

int32_t UE5_ResetTimeDilation(int32_t target) {
    return Hemmung::ResetDilation(target);
}

// === Fly (Dunste) — no-gravity keyboard-driven 3D flight ===

int32_t UE5_SetFly(int32_t enable) {
    return Dunste::SetEnabled(enable != 0);
}

int32_t UE5_SetFlySpeed(double uuPerSec) {
    return Dunste::SetSpeed(uuPerSec);
}

int32_t UE5_SetFlyPreset(int32_t preset) {
    return Dunste::SetPreset(preset);
}

int32_t UE5_SetFlyNoclip(int32_t enable) {
    return Dunste::SetNoclip(enable != 0);
}

// ============================================================================
// ProcessEvent vtable detection (build 648+)
//
// Background: the pre-build-648 detector picked the vtable byte offset purely
// from a hardcoded UE-version table and "validated" the slot only by reading
// 1 byte to confirm it pointed to *some* code. Every UObject virtual passes
// that check, so on Geri (UE 4.27) and ES2 (UE 5.5) we silently hooked an
// adjacent virtual — the invoke queue never drained, KismetMathLibrary
// returns sat at 0, and the bug slept for 600+ builds because verify mode
// (the first reader of the return slot) only landed in build 637.
//
// New approach (cribbed from Dumper-7 vendor/Dumper-7/Dumper/Engine/Private/
// OffsetFinder/Offsets.cpp:15-74): iterate the UObject vtable slot-by-slot,
// fetch each candidate function's body, and look for two `TEST [reg+disp32],
// imm32` instructions that ProcessEvent uniquely checks against:
//   1) imm32 = 0x00000400  (FUNC_Native)         — within first 0x400 bytes
//   2) imm32 = 0x00400000  (FUNC_HasOutParms /
//                           FUNC_NetServer mask) — within first 0xF00 bytes
// Both `disp32` operands point at UFunction::FunctionFlags (~0x88..0xC0 across
// UE versions). We don't need to know the exact FunctionFlags offset — just
// that *some* `TEST DWORD PTR [reg+disp32], imm32` pair exists with the right
// immediates. Function-fingerprinting beats slot-fingerprinting because slot
// indexes drift with UObject virtual additions across versions (and across
// publisher forks), but the FUNC_Native test inside ProcessEvent is stable.
//
// Fallback: if no slot matches the pattern (e.g. heavily-optimised LTO build
// that rewrote the test sequence), fall back to the legacy version-based
// table so we degrade gracefully instead of bricking the whole invoke path.
// ============================================================================

// 10-byte signatures for `F7 /0 disp32, imm32` (TEST [reg+disp32], imm32):
//   [F7] [ModRM] [disp32 low byte = FF_off] [disp32 high 3 bytes = 0] [imm32]
// ModRM and the FF_off byte are wildcarded — FF_off varies by UE version
// (0x88..0xC0, always < 0x100 so the high 3 bytes of disp32 are 0).
// The remaining 7 bytes pin the instruction shape and the literal imm32.
namespace {
    constexpr uint8_t kPePat1Bytes[10] = { 0xF7, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00 };
    constexpr char    kPePat1Mask[11] =   "x??xxxxxxx";   // 10 chars + NUL
    constexpr uint8_t kPePat2Bytes[10] = { 0xF7, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00 };
    constexpr char    kPePat2Mask[11] =   "x??xxxxxxx";

    // Linear pattern search with 'x' = exact / '?' = wildcard mask.
    // Returns true if found within haystack[0..size).
    bool ContainsPattern(const uint8_t* haystack, size_t haystackSize,
                         const uint8_t* pattern, const char* mask, size_t patternSize) {
        if (haystackSize < patternSize) return false;
        for (size_t i = 0; i <= haystackSize - patternSize; ++i) {
            bool ok = true;
            for (size_t j = 0; j < patternSize; ++j) {
                if (mask[j] == 'x' && haystack[i + j] != pattern[j]) { ok = false; break; }
            }
            if (ok) return true;
        }
        return false;
    }

    // Sample-many-objects vtable extractor. Most slots only need one valid
    // UObject*, but a few pathological GObjects entries near the head can be
    // garbage CDOs with NULL or torn vtable pointers — walk a window of 200.
    //
    // Returns DISTINCT vtables rather than the first one, because the pattern scan is not
    // guaranteed to work on an arbitrary class. Measured on the DropIn 4.27.2 PDB:
    // AActor::ProcessEvent is a thin override that contains the FUNC_Native test but NOT
    // the high-flag test, so the two-pattern scan returns -1 for any Actor vtable and we
    // would drop to the version table. Sampling several classes makes that a non-event.
    void CollectCandidateVTables(std::vector<uintptr_t>& out, size_t maxDistinct) {
        for (int i = 0; i < Aura::GetCount() && i < 400; ++i) {
            uintptr_t obj = Aura::GetByIndex(i);
            if (!obj) continue;
            uintptr_t vt = 0;
            if (!Macht::ReadSafe(obj, vt) || !vt) continue;
            if (std::find(out.begin(), out.end(), vt) != out.end()) continue;
            out.push_back(vt);
            if (out.size() >= maxDistinct) return;
        }
    }
}  // namespace

// Pattern-based detection: scan a window of vtable slots, return the byte
// offset whose function body contains both Dumper-7 TEST patterns. -1 = miss.
static int DetectProcessEventVTableOffsetByPattern(uintptr_t vtable) {
    // Vtable window: every UE version we support places PE between 0x100 and
    // 0x300. Step 8 = pointer stride. ~64 slots × ~0xF00 byte read is fast.
    constexpr int kMinOffset   = 0x100;
    constexpr int kMaxOffset   = 0x300;
    // 0x2000, not the original 0xF00. Measured on the DropIn UE 4.27.2 PDB:
    // UObject::ProcessEvent is 5182 bytes and pattern 2 sits at byte 3537 — only 303 bytes
    // (7.9%) inside the old 3840-byte window. Any build that inlines a little more ahead of
    // the high-flag test would silently push it out and drop us onto the version table.
    constexpr size_t kBodySize = 0x2000;

    uint8_t body[kBodySize];

    for (int off = kMinOffset; off <= kMaxOffset; off += 8) {
        uintptr_t funcAddr = 0;
        if (!Macht::ReadSafe(vtable + off, funcAddr) || !funcAddr) continue;

        // Read function body; failure means this slot points somewhere
        // unreadable (broken vtable entry) — skip without logging spam.
        if (!Macht::ReadBytesSafe(funcAddr, body, kBodySize)) continue;

        // Pattern 1 (FUNC_Native test) must be within first 0x400 bytes —
        // ProcessEvent's prologue does this very early.
        if (!ContainsPattern(body, 0x400, kPePat1Bytes, kPePat1Mask, sizeof(kPePat1Bytes))) continue;

        // Pattern 2 (high-flag test) can live deeper into the function body.
        if (!ContainsPattern(body, kBodySize, kPePat2Bytes, kPePat2Mask, sizeof(kPePat2Bytes))) continue;

        LOG_INFO("DetectProcessEvent (pattern): match at vtable+0x%X -> 0x%llX",
                 off, (unsigned long long)funcAddr);
        return off;
    }
    return -1;
}

// Legacy version-based detection — kept as a fallback for cases where the
// pattern scanner whiffs (unusual compiler output, heavily-optimised LTO,
// custom publisher fork). Not authoritative anymore: a "success" return
// here will be cross-checked by post-install hook-fire-count validation
// in TryInstallGameThreadHook below.
static int DetectProcessEventVTableOffsetByVersion(uintptr_t vtable) {
    int primary;
    if (g_cachedUEVersion >= 550)      primary = 0x228;
    else if (g_cachedUEVersion >= 500) primary = 0x220;
    // 4.27 is 0x220, NOT the 0x218 this band used to return. 0x218 is slot 67 =
    // UObject::OverridePerObjectConfigSection, one slot BEFORE ProcessEvent — hooking it is
    // exactly the build-647 failure (invokes time out, static fast path returns 0).
    // Ground truth: the DropIn 4.27.2 PDB, plus four live games (Geri, SBDR, P3R, Elliot).
    // 4.25/4.26 are deliberately left on 0x218: we have no measured value for either, and
    // widening the 4.27 number over them would be a guess. Caveat for both: these are
    // non-editor builds — an editor build inserts the WITH_EDITOR virtual block and shifts
    // ProcessEvent later.
    else if (g_cachedUEVersion >= 427) primary = 0x220;
    else if (g_cachedUEVersion >= 425) primary = 0x218;
    else if (g_cachedUEVersion >= 420) primary = 0x210;
    else                               primary = 0x208;

    LOG_WARN("DetectProcessEvent (fallback): pattern scan missed, "
             "falling back to UE=%u version-table primary=0x%X",
             g_cachedUEVersion, primary);
    for (int delta = -16; delta <= 16; delta += 8) {
        int off = primary + delta;
        if (off < 0) continue;
        uintptr_t addr = 0;
        Macht::ReadSafe(vtable + off, addr);
        LOG_INFO("  vtable+0x%03X = 0x%llX%s",
                 off, (unsigned long long)addr,
                 delta == 0 ? "  <-- primary" : "");
    }

    uintptr_t funcAddr = 0;
    if (Macht::ReadSafe(vtable + primary, funcAddr) && funcAddr) {
        uint8_t test = 0;
        if (Macht::ReadBytesSafe(funcAddr, &test, 1)) {
            return primary;
        }
    }
    for (int d : { 8, -8, 16, -16 }) {
        int off = primary + d;
        if (off < 0) continue;
        funcAddr = 0;
        if (Macht::ReadSafe(vtable + off, funcAddr) && funcAddr) {
            uint8_t test = 0;
            if (Macht::ReadBytesSafe(funcAddr, &test, 1)) {
                return off;
            }
        }
    }
    return -1;
}

// Top-level resolver. Pattern-based first, version-table second. Caller is
// expected to additionally validate by post-install hook-fire-count check.
static int DetectProcessEventVTableOffset() {
    std::vector<uintptr_t> vtables;
    CollectCandidateVTables(vtables, 12);
    if (vtables.empty()) {
        LOG_ERROR("DetectProcessEvent: no valid UObject vtable available");
        return -1;
    }

    // Try the pattern scan across several distinct classes before giving up on it.
    // A class that overrides ProcessEvent (any AActor) hides the high-flag test in the
    // base implementation, so one sample is not enough to conclude "pattern scan failed".
    for (size_t i = 0; i < vtables.size(); ++i) {
        int off = DetectProcessEventVTableOffsetByPattern(vtables[i]);
        if (off > 0) {
            if (i > 0)
                LOG_INFO("DetectProcessEvent: pattern matched on candidate vtable #%zu "
                         "(earlier ones override ProcessEvent)", i);
            return off;
        }
    }

    int off = DetectProcessEventVTableOffsetByVersion(vtables[0]);
    if (off > 0) return off;

    LOG_ERROR("DetectProcessEvent: both pattern scan and version fallback failed");
    return -1;
}

static int s_processEventOffset = -2;  // -2 = not yet detected

/// Resolve the actual ProcessEvent function address from any valid UObject's vtable.
/// Used both for direct calls and for installing the game-thread hook.
static uintptr_t ResolveProcessEventAddr() {
    if (s_processEventOffset == -2) {
        s_processEventOffset = DetectProcessEventVTableOffset();
    }
    if (s_processEventOffset < 0) return 0;

    // Find any valid UObject to read its vtable. Use GetByIndex (not GetItem->Object)
    // so the UE5.7+ within-item object-ptr offset is applied — on a reordered item the
    // raw FUObjectItem::Object field at +0x00 is the int64 FlagsAndRefCount, not a ptr.
    uintptr_t testObj = 0;
    for (int idx = 1; idx < 100; idx++) {
        uintptr_t obj = Aura::GetByIndex(idx);
        if (obj) { testObj = obj; break; }
    }
    if (!testObj) return 0;

    uintptr_t vtable = 0;
    if (!Macht::ReadSafe(testObj, vtable) || !vtable) return 0;

    uintptr_t peAddr = 0;
    if (!Macht::ReadSafe(vtable + s_processEventOffset, peAddr) || !peAddr) return 0;

    return peAddr;
}

/// Try to install the game-thread ProcessEvent hook.
/// Called lazily on first UE5_CallProcessEvent invocation.
///
/// After a successful MinHook install, spawns a detached validator thread
/// (build 648+) that waits 1500ms and confirms Stark::GetHookFireCount() is
/// non-zero. UObject::ProcessEvent fires many times per second under normal
/// gameplay (every tick, every input dispatch, every anim notify), so a zero
/// reading after 1.5s is strong evidence we hooked an adjacent virtual
/// rather than PE itself. Logs ERROR loudly when that happens — silent
/// vtable-misdetection is the whole reason the bug at builds 1..647
/// stayed buried, so we now refuse to keep that failure mode silent.
// Hook-install RETRY policy (live finding, Elliot 2026-07-23).
//
// `MH_CreateHook` can fail with MH_ERROR_MEMORY_ALLOC — MinHook could not place a
// trampoline within reach of ProcessEvent, which depends on the process's VM
// layout at that instant. Observed intermittently: the same address hooked fine
// 19 minutes earlier in the same game. The old code latched a one-shot
// `s_hookAttempted` BEFORE attempting and never cleared it, so one unlucky
// allocation forced every later invoke in the process onto the unsafe direct
// call — 552 of them in 19 s, driven by a 10 Hz worker.
//
// So: bounded, rate-limited retries instead of a permanent latch. Cheap enough to
// sit on the lazy invoke path (a 10 Hz worker adds at most one attempt per
// cooldown), and bounded so a genuinely unhookable game stops trying.
static constexpr int      kMaxHookAttempts    = 8;
static constexpr uint64_t kHookRetryCooldownMs = 5000;
static std::atomic<int>      s_hookAttempts{0};
static std::atomic<uint64_t> s_lastHookAttemptMs{0};

// `force` = a user-initiated attempt (a feature being switched on). It skips the
// cooldown and the attempt cap, because the user is standing there waiting and a
// retry is cheap; the automatic lazy path stays bounded.
static void TryInstallGameThreadHook(bool force = false) {
    if (Stark::IsHookActive()) return;

    int attempts = s_hookAttempts.load(std::memory_order_relaxed);
    if (!force && attempts >= kMaxHookAttempts) return;
    if (!force && attempts > 0) {
        uint64_t now  = GetTickCount64();
        uint64_t last = s_lastHookAttemptMs.load(std::memory_order_relaxed);
        if (now - last < kHookRetryCooldownMs) return;   // too soon — stay quiet
    }
    // Only the AUTOMATIC path spends the budget. A forced attempt already skips the
    // cap and the cooldown, so counting it there just burned the automatic retries on
    // the user's behalf: two clicks of a feature that forces a hook (Live Funcs,
    // See-through) exhausted kMaxHookAttempts and the lazy retry went silent for the
    // rest of the session. The timestamp IS still stamped for both — a forced attempt
    // is a real attempt, and the cooldown should measure from it. (B24)
    if (!force) {
        s_hookAttempts.fetch_add(1, std::memory_order_relaxed);
        attempts += 1;
    }
    s_lastHookAttemptMs.store(GetTickCount64(), std::memory_order_relaxed);

    uintptr_t peAddr = ResolveProcessEventAddr();
    if (!peAddr) {
        LOG_WARN("GameThreadDispatch: cannot resolve ProcessEvent address for hooking");
        return;
    }

    if (!Stark::InstallHook(peAddr)) {
        LOG_WARN("GameThreadDispatch: hook install failed (attempt %d/%d), invoke will "
                 "use direct call (unsafe)%s",
                 attempts, kMaxHookAttempts,
                 attempts < kMaxHookAttempts ? " — will retry" : " — giving up");
        return;
    }
    if (attempts > 1) {
        LOG_INFO("GameThreadDispatch: hook RECOVERED on attempt %d — game-thread "
                 "dispatch is available again", attempts);
    }
    LOG_INFO("GameThreadDispatch: hook installed at 0x%llX, validator armed (1500ms)",
             (unsigned long long)peAddr);

    // Detached validator: don't block the caller — UE5_Init must keep
    // running. The validator just observes and logs; remediation (e.g.
    // unhook + re-detect with a different strategy) is intentionally out
    // of scope for v1 since unhook-while-game-thread-may-be-inside-the-
    // trampoline is itself unsafe (see Stark::RemoveHook comment).
    std::thread([peAddr]() {
        uint64_t before = Stark::GetHookFireCount();
        std::this_thread::sleep_for(std::chrono::milliseconds(1500));
        uint64_t after  = Stark::GetHookFireCount();
        uint64_t delta  = after - before;
        if (delta == 0) {
            LOG_ERROR("GameThreadDispatch: VALIDATION FAILED — hook at 0x%llX fired 0 "
                      "times in 1500ms. We are almost certainly hooked on the wrong "
                      "vtable slot. UFunction invokes via this path WILL time out. "
                      "(If the game is paused/main-menu, ignore; otherwise this is a "
                      "real misdetection — please collect logs.)",
                      (unsigned long long)peAddr);
        } else {
            LOG_INFO("GameThreadDispatch: validation OK — hook fired %llu times "
                     "in 1500ms (hook=0x%llX)",
                     (unsigned long long)delta, (unsigned long long)peAddr);
        }
    }).detach();
}

// First-time ProcessEvent bring-up (vtable-offset detection + game-thread hook
// install) MUST run exactly once even when the Mimic mailbox polling thread and
// one or more Fern pipe-lane threads issue their FIRST invoke at the same moment.
// Previously each entry point did an unsynchronized `if (s_processEventOffset ==
// -2) { detect; TryInstall; }`, so two threads could both detect and both reach
// Stark::InstallHook — a double MH_CreateHook on the same address can corrupt
// MinHook's state (and races the s_originalPE write the game thread reads),
// risking a crash (audit #3). std::call_once serializes the pair; late arrivals
// block until it finishes, then observe the fully-written offset via call_once's
// happens-before. Diagnostic: the arrival counter surfaces whether the race
// window was actually contended in this game (proof the guard earns its keep).
static std::once_flag s_peInitOnce;
static std::atomic<int> s_peInitArrivals{0};
// Arrival ordinal at the moment init FINISHED. Anything numbered above it turned
// up after the once was already satisfied and contended nothing. Without this the
// test was `arrivals > 1` — true for EVERY invoke the process ever makes: a live
// See-Through session (one invoke per ~100 ms tick) logged 437 of these WARNs in
// four minutes on a run whose own INFO line said "1 caller(s) arrived before init
// began". Every one a false positive, and the diagnostic worthless precisely
// because it always fired.
static std::atomic<int> s_peArrivalsAtInitEnd{0};
static void EnsureProcessEventReady() {
    int arrivals = s_peInitArrivals.fetch_add(1, std::memory_order_relaxed) + 1;
    std::call_once(s_peInitOnce, [arrivals]() {
        LOG_INFO("ProcessEvent: first-time init on thread %lu (offset detection + "
                 "game-thread hook install), serialized via call_once — %d "
                 "caller(s) arrived before init began",
                 (unsigned long)GetCurrentThreadId(), arrivals);
        s_processEventOffset = DetectProcessEventVTableOffset();
        TryInstallGameThreadHook();
        // Publish the high-water mark INSIDE the once, so it is visible to every
        // waiter call_once releases (and to later callers, via that same
        // happens-before) before any of them evaluates the test below.
        s_peArrivalsAtInitEnd.store(s_peInitArrivals.load(std::memory_order_relaxed),
                                    std::memory_order_relaxed);
        LOG_INFO("ProcessEvent: first-time init complete — offset=%d, hook_active=%d",
                 s_processEventOffset, Stark::IsHookActive() ? 1 : 0);
    });
    // Bounded, rate-limited retry of a FAILED install (see the policy note above).
    // call_once cannot re-run, so the retry lives out here on the ordinary path.
    if (s_processEventOffset >= 0 && !Stark::IsHookActive()) TryInstallGameThreadHook();

    // Warn ONLY for arrivals that were genuinely in flight while init ran — that
    // is the historic double-install crash window, and it is what this line
    // claims to be evidence of.
    if (arrivals > 1 &&
        arrivals <= s_peArrivalsAtInitEnd.load(std::memory_order_relaxed)) {
        LOG_WARN("ProcessEvent: concurrent first-invoke #%d serialized behind the "
                 "one-time init (audit #3 race window observed but guarded)",
                 arrivals);
    }
}

// (b) One line per hook-state TRANSITION, not per invoke. Starts "unknown" so the
// first observation of either state is reported once; the count of suppressed
// unsafe calls rides the recovery line so nothing is silently lost.
static std::atomic<int>      s_reportedHookState{-1};   // -1 unknown, 0 down, 1 up
static std::atomic<uint64_t> s_unsafeCallsSinceDown{0};
static void ReportHookState(bool active) {
    if (!active) s_unsafeCallsSinceDown.fetch_add(1, std::memory_order_relaxed);
    int want = active ? 1 : 0;
    int prev = s_reportedHookState.exchange(want, std::memory_order_relaxed);
    if (prev == want) return;
    if (active) {
        uint64_t n = s_unsafeCallsSinceDown.exchange(0, std::memory_order_relaxed);
        if (prev < 0) return;   // first-ever observation, and it is the healthy one
        LOG_INFO("UE5_CallProcessEvent: game-thread hook is ACTIVE again — invokes are "
                 "dispatched to the game thread (%llu invoke(s) took the fallback while "
                 "it was down)", (unsigned long long)n);
    } else {
        LOG_WARN("UE5_CallProcessEvent: game-thread hook NOT active — invokes fall back to "
                 "a direct call off the game thread (unsafe). Logged once per transition; "
                 "repeating WORKER invokes are refused outright.");
    }
}

// Internal size-aware entry. paramsSize > 0 makes the queued GameThreadDispatch
// request OWN a copy of the param bytes, so a timed-out-but-still-queued invoke
// can't dereference a freed caller buffer (use-after-free). Out-params are copied
// back on success. The direct fallback is synchronous (buffer stays alive), so
// size is irrelevant there. Declared extern "C" so Fern links to it directly.
extern "C" int32_t UE5_CallProcessEventEx(uintptr_t instance, uintptr_t ufunc,
                                          uintptr_t params, uint32_t paramsSize) {
    if (!instance || !ufunc) return -1;

    // Lazy, race-safe one-time detection + (retryable) hook install (audit #3).
    EnsureProcessEventReady();
    if (s_processEventOffset < 0) return -3;

    // Prefer game-thread dispatch via hook
    if (Stark::IsHookActive()) {
        ReportHookState(true);
        LOG_INFO("UE5_CallProcessEvent: dispatching to game thread inst=0x%llX func=0x%llX",
                 (unsigned long long)instance, (unsigned long long)ufunc);
        return Stark::EnqueueInvoke(instance, ufunc, params, paramsSize);
    }

    // Hook is down. Log the fallback ONCE per state transition, not once per
    // invoke: the per-invoke version produced 552 identical WARN+INFO triplets in
    // 19 s on a live run and buried everything else in the log.
    ReportHookState(false);

    // (c) A REPEATING worker invoke must not take the unsafe path. A user's
    // one-shot invoke accepting that risk is their call; a re-assert / feature
    // worker calling ProcessEvent off the game thread ~10x/s for minutes is not a
    // trade anyone opted into — and it is the historic crash shape. Refuse, and
    // let the feature surface "unavailable" instead of quietly gambling.
    if (Tot::IsBackgroundWorker()) return -8;

    // Fallback: direct call from current thread (unsafe for state-changing functions)
    // Read vtable from the target instance
    uintptr_t vtable = 0;
    if (!Macht::ReadSafe(instance, vtable) || !vtable) return -2;

    uintptr_t peAddr = 0;
    if (!Macht::ReadSafe(vtable + s_processEventOffset, peAddr) || !peAddr) return -3;

    typedef void (__fastcall *FnProcessEvent)(void*, void*, void*);
    auto pProcessEvent = reinterpret_cast<FnProcessEvent>(peAddr);

    __try {
        pProcessEvent(reinterpret_cast<void*>(instance),
                      reinterpret_cast<void*>(ufunc),
                      reinterpret_cast<void*>(params));
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        // An exception is per-call news, so this one stays unconditional.
        LOG_ERROR("UE5_CallProcessEvent: EXCEPTION during direct ProcessEvent call! "
                  "inst=0x%llX func=0x%llX pe=0x%llX",
                  (unsigned long long)instance, (unsigned long long)ufunc,
                  (unsigned long long)peAddr);
        return -4;
    }
    return 0;
}

extern "C" bool UE5_IsGameThreadHookActive() { return Stark::IsHookActive(); }

int32_t UE5_CallProcessEvent(uintptr_t instance, uintptr_t ufunc, uintptr_t params) {
    // Legacy 3-arg export (CE Lua + Mimic's mailbox). Size 0 = no owned copy:
    // callers here pass persistent buffers that outlive the queued request.
    return UE5_CallProcessEventEx(instance, ufunc, params, 0);
}

// Force the game-thread ProcessEvent hook to install NOW (reuses the audit-#3
// call_once, so it is race-safe and idempotent). Normally the hook installs
// lazily on the first invoke; the Live PE profiler (Linie) needs it up before
// any invoke so it can record the game's own PE calls. Returns whether the hook
// is active afterward (false ⇒ vtable-offset detection failed on this game;
// the profiler will record nothing and the UI warns).
extern "C" bool UE5_EnsureGameThreadHook() {
    EnsureProcessEventReady();          // one-time offset detection + first install attempt
    if (Stark::IsHookActive()) return true;

    // The first install can fail transiently — e.g. MH_ERROR_MEMORY_ALLOC, when MinHook
    // can't place a trampoline near ProcessEvent this session (reachable ±2GB region
    // occupied by another injected tool / a stale prior injection). Retry through the
    // ONE install path, forced: this call is always user-initiated (a feature switching
    // on), so it skips the automatic path's cooldown/cap. Stark::InstallHook is
    // idempotent + mutex-guarded, so repeated clicks safely re-attempt.
    if (s_processEventOffset < 0) return false;   // detection genuinely failed — nothing to retry
    TryInstallGameThreadHook(/*force=*/true);
    return Stark::IsHookActive();
}

// ProcessEvent vtable offset once detected (>=0), or a negative sentinel (-2 not
// yet attempted / -1 detection failed). Lets the UI distinguish "couldn't find
// ProcessEvent" (retry after an invoke) from "found it but the hook wouldn't
// install" (MinHook alloc / another tool — restart + re-inject).
extern "C" int UE5_GetProcessEventOffset() {
    return s_processEventOffset;
}

// Direct call entry point — never goes through GameThreadDispatch.
// Mirrors the fallback path of UE5_CallProcessEvent without the hook
// check; intended for callers (e.g. Mimic::HandleInvoke) that have
// independently verified the function is safe to call off-thread.
// Sharing the body via a static helper would tangle SEH+C++ object
// lifetimes; the duplication is small.
int32_t UE5_CallProcessEventDirect(uintptr_t instance, uintptr_t ufunc, uintptr_t params) {
    if (!instance || !ufunc) return -1;

    // Lazy, race-safe one-time detection + hook install (audit #3).
    EnsureProcessEventReady();
    if (s_processEventOffset < 0) return -3;

    uintptr_t vtable = 0;
    if (!Macht::ReadSafe(instance, vtable) || !vtable) return -2;

    uintptr_t peAddr = 0;
    if (!Macht::ReadSafe(vtable + s_processEventOffset, peAddr) || !peAddr) return -3;

    typedef void (__fastcall *FnProcessEvent)(void*, void*, void*);
    auto pProcessEvent = reinterpret_cast<FnProcessEvent>(peAddr);

    LOG_INFO("UE5_CallProcessEventDirect: inst=0x%llX func=0x%llX pe=0x%llX (caller-asserted safe)",
             (unsigned long long)instance, (unsigned long long)ufunc,
             (unsigned long long)peAddr);

    __try {
        pProcessEvent(reinterpret_cast<void*>(instance),
                      reinterpret_cast<void*>(ufunc),
                      reinterpret_cast<void*>(params));
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        LOG_ERROR("UE5_CallProcessEventDirect: EXCEPTION during direct ProcessEvent call!");
        return -4;
    }

    return 0;
}

// === Mailbox ===

uintptr_t UE5_GetMailboxAddr() {
    return Mimic::GetAddress();
}

// === Pipe Server ===

bool UE5_StartPipeServer() {
    // Guard: if another UE5Dumper instance (e.g., proxy DLL) already owns the pipe,
    // skip starting a competing pipe server to avoid connection failures.
    HANDLE testPipe = CreateFileW(
        Grimoire::PIPE_NAME,
        GENERIC_READ, 0, nullptr,
        OPEN_EXISTING, 0, nullptr);
    if (testPipe != INVALID_HANDLE_VALUE) {
        CloseHandle(testPipe);
        LOG_WARN("UE5_StartPipeServer: pipe already exists (another instance running) — skipping");
        return true;  // return true so CE Lua doesn't treat it as failure
    }
    return s_pipeServer.Start();
}

void UE5_StopPipeServer() {
    s_pipeServer.Stop();
}

bool UE5_IsPipeConnected() {
    return s_pipeServer.IsClientConnected();
}

} // extern "C"
