// ============================================================
// Edel — 艾德爾 (催眠魔法 — Hypnosis, "noble")
// CurrentTarget: auto-detect the actor the local player is currently targeting.
//
// Resolution chain (§ mirrors Wirbel/Teleport, intentionally DUPLICATED here as
// a small read-only copy so Edel stays decoupled from Wirbel's teleport-specific
// Chain/TP_ERR vocabulary): GWorld → OwningGameInstance → LocalPlayers[0] →
// PlayerController → Pawn. Every field name is declared on an ENGINE base class,
// so Ubel::FindFieldOffset makes the lookups game- and version-agnostic.
//
// Then: enumerate the outgoing object pointers of {PC, Pawn, and their owned
// ActorComponents} (Aura::CollectOutgoingObjectPtrs — the public façade over the
// battle-tested edge walker), keep the ones that walk like an AActor and aren't
// the player's own PC/pawn, and SCORE each by name-keyword + structural signal.
// ============================================================

#define LOG_CAT "WALK"
#include "Sein.h"
#include "Edel.h"
#include "Grimoire.h"
#include "Macht.h"
#include "Aura.h"
#include "Ubel.h"
#include "Tot.h"

#include <algorithm>
#include <cctype>
#include <climits>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

// &GWorld — the global pointer slot; deref ONCE for the live UWorld* (same as
// Wirbel::DerefWorld). Defined elsewhere in the DLL; declared extern here.
extern uintptr_t g_cachedGWorld;

// Target-detection scan budgets (Edel):
static constexpr int kTargetScanEdgeCap   = 1024;  // max outgoing object-ptr edges collected per root/component
static constexpr int kComponentScanBudget = 16;    // per-root owned-ActorComponent scan budget (fairness cap)

namespace {

// ---- low-level reads (mirror Wirbel) ----

uintptr_t DerefWorld() {
    if (!g_cachedGWorld) return 0;
    uintptr_t w = 0;
    if (!Macht::ReadSafe(g_cachedGWorld, w)) return 0;
    return w;
}

uintptr_t ReadPtrAt(uintptr_t obj, int32_t off) {
    if (!obj || off < 0) return 0;
    uintptr_t v = 0;
    Macht::ReadSafe(obj + static_cast<uintptr_t>(off), v);
    return v;
}

// Resolve the LOCAL PlayerController: engine chain first
// (GWorld → OwningGameInstance → LocalPlayers[0] → PlayerController), then an
// instance-scan fallback preferring the PC whose UPlayer* Player field is set.
// A faithful read-only copy of Wirbel::ResolveLocalPC.
uintptr_t ResolveLocalPC(uintptr_t world) {
    do {
        if (!world) break;
        uintptr_t worldClass = Ubel::GetClass(world);
        int32_t giOff = Ubel::FindFieldOffset(worldClass, "OwningGameInstance",
                                              "GameInstance", nullptr, "ObjectProperty");
        uintptr_t gi = ReadPtrAt(world, giOff);
        if (!gi) break;
        uintptr_t giClass = Ubel::GetClass(gi);
        int32_t lpOff = Ubel::FindFieldOffset(giClass, "LocalPlayers", "LocalPlayers",
                                              nullptr, "ArrayProperty");
        if (lpOff < 0) break;
        Macht::TArrayView arr;
        if (!Macht::ReadTArray(gi + static_cast<uintptr_t>(lpOff), arr) || arr.Count <= 0)
            break;
        uintptr_t lp = Macht::ReadTArrayElement(arr, 0);
        if (!lp) break;
        uintptr_t lpClass = Ubel::GetClass(lp);
        int32_t pcOff = Ubel::FindFieldOffset(lpClass, "PlayerController",
                                              "PlayerController", nullptr, "ObjectProperty");
        uintptr_t pc = ReadPtrAt(lp, pcOff);
        if (pc) {
            LOG_INFO("Edel: local PC 0x%llX via GWorld chain", (unsigned long long)pc);
            return pc;
        }
    } while (false);

    // [FINDINSTANCE-INTERNAL-SUBSTRING-FALLBACKS] By derivation, the local player's controller first.
    const Aura::LocalPcScan s = Aura::ScanForLocalPlayerController();
    if (s.pick == Aura::LocalPcPick::LocalPlayer)
        LOG_INFO("Edel: local PC 0x%llX via instance scan (Player set)", (unsigned long long)s.pc);
    else if (s.pick == Aura::LocalPcPick::OtherPlayer)
        LOG_WARN("Edel: PC 0x%llX via instance scan (a remote Player; no local one found)",
                 (unsigned long long)s.pc);
    else if (s.pick == Aura::LocalPcPick::NoPlayer)
        LOG_WARN("Edel: PC 0x%llX via instance scan WITHOUT a Player", (unsigned long long)s.pc);
    return s.pc;
}

// When the Debug Camera is active the LocalPlayer's controller IS the
// DebugCameraController (pawnless); hop to the real gameplay PC.
uintptr_t HopThroughDebugCamera(uintptr_t pc) {
    if (!pc) return pc;
    uintptr_t cls = Ubel::GetClass(pc);
    // [FINDINSTANCE-INTERNAL-SUBSTRING-FALLBACKS] Derivation: a game's debug camera may be named without the word.
    if (!Aura::ClassDerivesFromAny(cls, {Grimoire::AURA_USERPICK_DCC_CLASS})) return pc;
    int32_t origOff = Ubel::FindFieldOffset(cls, "OriginalControllerRef",
                                            "OriginalController", nullptr, "ObjectProperty");
    uintptr_t orig = ReadPtrAt(pc, origOff);
    return orig ? orig : pc;
}

uintptr_t ResolvePawn(uintptr_t pc) {
    if (!pc) return 0;
    uintptr_t cls = Ubel::GetClass(pc);
    uintptr_t pawn = ReadPtrAt(pc, Ubel::FindFieldOffset(cls, "Pawn"));
    if (pawn) return pawn;
    return ReadPtrAt(pc, Ubel::FindFieldOffset(cls, "AcknowledgedPawn"));
}

// ---- structural type test (super-class FName walk) ----

// Walk obj's class → Super → Super…, comparing each class node's FName. Returns
// "Pawn" (a Pawn/Character/DefaultPawn target — the prize), "Actor" (any other
// AActor), "ActorComponent" (a component — never a target, but used to decide
// whether to descend into it), or "" (anything else). Bounded to 64 hops with a
// self-loop break (mirrors Wirbel::FindFunc) so a corrupt super pointer can't
// hang. Cheap: a class super-chain is ~5-10 deep.
const char* ClassifyBySuperChain(uintptr_t obj) {
    uintptr_t cls = Ubel::GetClass(obj);
    for (int guard = 0; cls && guard < 64; ++guard) {
        std::string n = Ubel::GetName(cls);
        if (n == "Pawn" || n == "Character" || n == "DefaultPawn") return "Pawn";
        if (n == "Actor")          return "Actor";
        if (n == "ActorComponent") return "ActorComponent";
        uintptr_t super = 0;
        if (!Macht::ReadSafe(cls + static_cast<uintptr_t>(DynOff::USTRUCT_SUPER), super)
            || !super || super == cls)
            break;
        cls = super;
    }
    return "";
}

// True when child's Outer chain reaches root within maxHops (a small read-only
// analogue of Aura's internal IsOwnedBy — used to confirm a candidate component
// is genuinely owned by the player object before descending into it).
bool OuterChainsBackTo(uintptr_t child, uintptr_t root, int maxHops) {
    uintptr_t o = child;
    for (int h = 0; h < maxHops; ++h) {
        o = Ubel::GetOuter(o);
        if (!o) return false;
        if (o == root) return true;
    }
    return false;
}

// ---- keyword tables ----

std::string ToLower(const std::string& s) {
    std::string r = s;
    for (auto& c : r) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return r;
}

bool MatchesAny(const std::string& lower, const std::vector<std::string>& table) {
    for (const auto& kw : table)
        if (lower.find(kw) != std::string::npos) return true;
    return false;
}

// Positive: a field whose name looks like "the actor the player is acting on".
// Stems are deliberate so plurals match too: "enem" covers enemy/enemies/enemyactor;
// "target" covers target/targets/targetactor; "hostile" covers hostile/hostiles.
const std::vector<std::string> kPositive = {
    "target", "focus", "lockon", "lockedon", "lockedtarget", "enem", "aimtarget",
    "selectedunit", "selectedactor", "selectedtarget", "combattarget", "hostile",
    "currenttarget", "interactable", "lasthitactor", "lastdamage", "attacktarget",
    "engagedtarget", "markedtarget",
};

// Negative: owner / self / infra FIELD names that contain a positive substring
// but must NOT win (ViewTarget == own camera focus; NetTarget; owner/instigator;
// …). Matched against the FIELD NAME ONLY — never the candidate's class name.
const std::vector<std::string> kNegative = {
    "owner", "instigator", "controller", "playerstate", "camera", "cameramanager",
    "spectator", "viewtarget", "input", "hud", "localplayer", "defaultpawn",
    "cheatmanager", "gamemode", "gamestate", "gameinstance", "world", "level",
    "mesh", "capsule", "movement", "anim", "audio", "widget",
};

}  // namespace

namespace Edel {

CurrentTargetResult DetectCurrentTarget(int32_t maxCandidates) {
    CurrentTargetResult res;
    if (maxCandidates <= 0)  maxCandidates = 8;
    if (maxCandidates > 64)  maxCandidates = 64;

    // --- Resolve the player chain (world may be 0; ResolveLocalPC falls back) ---
    res.world = DerefWorld();
    uintptr_t pc = ResolveLocalPC(res.world);
    if (!pc) {
        res.note = res.world
            ? "No local PlayerController found (not in gameplay?)."
            : "GWorld not available and no PlayerController found — open a level / let the game finish loading, then retry.";
        return res;
    }
    uintptr_t rawPc = pc;                 // pre-hop controller (DebugCameraController)
    pc = HopThroughDebugCamera(pc);
    res.playerController = pc;

    uintptr_t pawn = ResolvePawn(pc);
    res.playerPawn = pawn;
    if (!pawn) {
        res.note = "PlayerController has no Pawn (menu / spectator?). No target to detect.";
        return res;
    }

    // Self-exclusion: a pointer back to the player's own PC/pawn is NOT the
    // target. Seeding this BEFORE scoring is the single most important
    // correctness guard (else the player's own pawn auto-picks). Include BOTH
    // Pawn and AcknowledgedPawn (they can differ on a networked client — whichever
    // ResolvePawn didn't return would otherwise surface as a high-scoring is-Pawn)
    // and the pre-hop controller.
    std::unordered_set<uintptr_t> exclude = { pc, pawn, rawPc };
    {
        uintptr_t pcCls = Ubel::GetClass(pc);
        if (uintptr_t p2 = ReadPtrAt(pc, Ubel::FindFieldOffset(pcCls, "Pawn"))) exclude.insert(p2);
        if (uintptr_t p3 = ReadPtrAt(pc, Ubel::FindFieldOffset(pcCls, "AcknowledgedPawn"))) exclude.insert(p3);
    }
    std::unordered_map<uintptr_t, TargetCandidate> best;   // keyed by addr, keep highest score

    auto consider = [&](const Aura::OutgoingPtr& e, uintptr_t srcObj,
                        const std::string& srcClass, bool nearCombat) {
        uintptr_t child = e.target;
        if (!child || exclude.count(child)) return;

        std::string kind = ClassifyBySuperChain(child);
        bool isPawn  = (kind == "Pawn");
        bool isActor = isPawn || (kind == "Actor");

        std::string lowerField = ToLower(e.fieldName);
        uintptr_t childCls = Ubel::GetClass(child);

        int32_t score = 0;
        std::string reason;
        auto addReason = [&](const char* r) { if (!reason.empty()) reason += ", "; reason += r; };

        if (MatchesAny(lowerField, kPositive)) { score += 50; addReason(("field '" + e.fieldName + "'").c_str()); }
        if (isPawn)        { score += 30; addReason("is-Pawn"); }
        else if (isActor)  { score += 15; addReason("is-Actor"); }
        // Infra-negative is a FIELD-name filter ONLY — matching it against the
        // candidate's class name would demote a legit actor whose class merely
        // contains a generic substring (e.g. BP_WorldBoss_C eating "world").
        if (MatchesAny(lowerField, kNegative)) { score -= 40; addReason("infra-field"); }
        if (!isActor)      { score -= 60; addReason("not-Actor"); }   // a target must be an actor
        if (nearCombat)    { score += 10; }                          // closer to combat code than the PC

        int32_t idx = -1;
        bool haveIdx = Macht::ReadSafe(child + Grimoire::OFF_UOBJECT_INDEX, idx);
        if (haveIdx && idx >= 0) score += 5;                         // a real, tracked UObject

        if (score <= -60) return;   // drop hard non-Actor noise

        auto it = best.find(child);
        if (it != best.end() && it->second.score >= score) return;  // keep the better edge

        TargetCandidate c;
        c.addr         = child;
        c.index        = (haveIdx ? idx : -1);
        c.name         = Ubel::GetName(child);
        c.className    = childCls ? Ubel::GetName(childCls) : std::string();
        c.score        = score;
        c.sourceObject = srcObj;
        c.sourceClass  = srcClass;
        c.fieldName    = e.fieldName;
        c.fieldOffset  = (e.elementIndex >= 0) ? -1 : e.fieldOffset;  // element lives in heap buffer
        c.reason       = reason;
        best[child]    = std::move(c);
    };

    auto scanRoot = [&](uintptr_t root, bool nearCombat) {
        if (!root) return;
        uintptr_t rootCls = Ubel::GetClass(root);
        std::string rootClass = rootCls ? Ubel::GetName(rootCls) : std::string();
        std::vector<Aura::OutgoingPtr> edges;
        Aura::CollectOutgoingObjectPtrs(root, edges, kTargetScanEdgeCap);
        for (const auto& e : edges) consider(e, root, rootClass, nearCombat);
    };

    // --- Roots: the PlayerController, then the Pawn (combat fields often here) ---
    scanRoot(pc,   /*nearCombat*/ false);
    if (Tot::Requested()) { res.note = "Cancelled."; return res; }
    scanRoot(pawn, /*nearCombat*/ true);

    // --- + depth-1 owned ActorComponents of the PC and Pawn (a custom
    //     LockOn/Targeting component frequently holds the lock). Each root gets
    //     its OWN 16-component budget so a component-heavy pawn can't starve the
    //     PC scan; the PC is scanned first (targeting components often live on
    //     it). Each component's edges are scored as near-combat. ---
    std::unordered_set<uintptr_t> scannedComps;
    auto scanOwnedComponents = [&](uintptr_t root) {
        if (!root || Tot::Requested()) return;
        std::vector<Aura::OutgoingPtr> edges;
        Aura::CollectOutgoingObjectPtrs(root, edges, kTargetScanEdgeCap);
        int compCount = 0;   // per-root budget
        for (const auto& e : edges) {
            if (compCount >= kComponentScanBudget) break;
            uintptr_t comp = e.target;
            if (!comp || scannedComps.count(comp)) continue;
            if (std::string(ClassifyBySuperChain(comp)) != "ActorComponent") continue;
            if (!OuterChainsBackTo(comp, root, 2)) continue;
            scannedComps.insert(comp);
            ++compCount;
            uintptr_t compCls = Ubel::GetClass(comp);
            std::string compClass = compCls ? Ubel::GetName(compCls) : std::string();
            std::vector<Aura::OutgoingPtr> cedges;
            Aura::CollectOutgoingObjectPtrs(comp, cedges, kTargetScanEdgeCap);
            for (const auto& ce : cedges) consider(ce, comp, compClass, /*nearCombat*/ true);
        }
    };
    scanOwnedComponents(pc);
    scanOwnedComponents(pawn);

    // --- Sort best-first; auto-pick = [0] when its score is positive ---
    res.candidates.reserve(best.size());
    for (auto& kv : best) res.candidates.push_back(std::move(kv.second));
    std::sort(res.candidates.begin(), res.candidates.end(),
        [](const TargetCandidate& a, const TargetCandidate& b) {
            if (a.score != b.score) return a.score > b.score;
            // Prefer a direct field (small offset) over a container element
            // (-1) on a tie; then deterministic by address.
            int32_t ka = (a.fieldOffset < 0) ? INT_MAX : a.fieldOffset;
            int32_t kb = (b.fieldOffset < 0) ? INT_MAX : b.fieldOffset;
            if (ka != kb) return ka < kb;
            return a.addr < b.addr;
        });
    if (static_cast<int32_t>(res.candidates.size()) > maxCandidates)
        res.candidates.resize(maxCandidates);

    // Auto-pick (resolved=true) only when the top candidate has a positive score
    // AND clears the runner-up by a margin — otherwise several equally-plausible
    // actors (e.g. JRPG party members, all bare is-Pawn ~35) would yield an
    // arbitrary, wrong auto-pick. A lone candidate (no runner-up) auto-picks.
    constexpr int32_t kAutoPickMargin = 20;
    bool confident = !res.candidates.empty() && res.candidates[0].score > 0
        && (res.candidates.size() == 1
            || res.candidates[0].score - res.candidates[1].score >= kAutoPickMargin);

    if (confident) {
        res.resolved = true;
        const auto& top = res.candidates[0];
        res.note = "Detected target: " + top.name + " (" + top.className + ") — score "
                 + std::to_string(top.score) + " via " + top.fieldName + ".";
    } else if (!res.candidates.empty()) {
        res.note = "No clear current target (multiple / weak candidates). Showing best guesses — click one to inspect; verify before trusting.";
    } else {
        res.note = "Player resolved but no target-like reference found. Lock onto / click an enemy, then Detect again — or use Instance Finder.";
    }
    LOG_INFO("Edel: DetectCurrentTarget resolved=%d pc=0x%llX pawn=0x%llX candidates=%zu",
             res.resolved ? 1 : 0, (unsigned long long)pc, (unsigned long long)pawn,
             res.candidates.size());
    return res;
}

}  // namespace Edel
