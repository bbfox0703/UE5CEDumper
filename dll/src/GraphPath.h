#pragma once

// ============================================================
// GraphPath — pure, header-only breadth-first shortest-path core for the
// UObject reference graph ("Locate in GWorld" feature).
//
// Given a root UObject (e.g. GWorld → UWorld) and a target UObject, find the
// SHORTEST (fewest-hop) chain of outgoing pointer edges root → … → target,
// bounded by a depth budget. BFS guarantees the first time the target is
// reached is via a shortest path, so "first found == shortest" is free.
//
// This header is DELIBERATELY free of any live-memory / UE-reflection
// dependency: the graph adjacency is supplied by a caller-provided
// `neighborFn` functor: the live search supplies a real GObjects/reflection
// enumerator, a unit test an in-memory mock graph. That keeps the search
// logic (cycles, depth bound, shortest-path reconstruction, abort)
// unit-testable without an injected process.
//
// neighborFn contract:
//   neighborFn(uintptr_t node, auto&& emit)
//     enumerate `node`'s outgoing object-pointer edges, calling
//       emit(child, fieldOffset, fieldName, fieldType, innerType, elementIndex,
//            elemStride, elemValueOffset)
//     for each. `elemStride`/`elemValueOffset` describe a container element's
//     geometry (stride between elements in the Data buffer + the followed
//     pointer's within-element offset) so a Map/Set element hop can be split into
//     container+element CE derefs; both are 0 for direct/non-element edges.
//     `emit` returns true when the search wants to STOP (target
//     found or the visited cap was hit) — neighborFn MUST stop enumerating
//     and return promptly when emit returns true. The visited-set check
//     happens inside emit BEFORE any per-edge string is retained, so passing
//     references into a cached metadata table is cheap on the discard path.
//
// abortFn contract:
//   abortFn() -> bool  — return true to abort the whole search (deadline /
//   cancellation). Polled every ~1024 dequeues (cheap).
// ============================================================

#include <cstdint>
#include <string>
#include <vector>
#include <deque>
#include <unordered_map>
#include <algorithm>

namespace Aura {

// One outgoing edge of the object graph (how a child object was reached).
struct GraphEdge {
    int32_t     fieldOffset  = 0;    // byte offset of the field within the parent UObject
    std::string fieldName;           // property name (Map matches append ".Key"/".Value")
    std::string fieldType;           // "ObjectProperty" / "ArrayProperty" / "MapProperty" / ...
    std::string innerType;           // container element type ("" for direct fields)
    int32_t     elementIndex = -1;   // -1 = direct field; >=0 array/map/set element (sparse idx)
    int32_t     elemStride   = 0;    // element stride in the container Data buffer (8 obj-array, pairStride map, elemStride set; 0 = n/a)
    int32_t     elemValueOffset = 0; // within-element offset of the followed ptr (map value); 0 for array/set/map-key
};

// One step of the resolved path: parent --edge--> child.
struct GraphPathStep {
    uintptr_t   fromObj      = 0;
    uintptr_t   toObj        = 0;
    int32_t     fieldOffset  = 0;
    std::string fieldName;
    std::string fieldType;
    std::string innerType;
    int32_t     elementIndex = -1;
    int32_t     elemStride   = 0;    // see GraphEdge — element geometry for Map/Set split
    int32_t     elemValueOffset = 0;
    // Resolved lazily by the live wrapper for path nodes only (empty in the
    // pure core / tests).
    std::string toName;
    std::string toClassName;
};

struct GraphPathResult {
    bool        found        = false;
    bool        aborted      = false;   // deadline or cancellation tripped the search
    // "ok" / "not_reachable" / "visited_cap" / "invalid" / "reconstruct_error".
    // The live wrapper rewrites "aborted" into "cancelled" / "deadline".
    std::string status;
    std::vector<GraphPathStep> steps;   // root -> steps[0].to -> … -> target (empty when root==target)
    int32_t     depthReached = 0;       // == steps.size()
    int32_t     visited      = 0;       // distinct objects discovered
    int64_t     durationMs   = 0;       // filled by the live wrapper
};

// Breadth-first shortest-path search over an abstract object graph.
//   root, target : UObject addresses (or any opaque non-zero node ids in tests)
//   maxDepth     : maximum hop count from root to target (target may sit at
//                  exactly maxDepth; nodes at depth maxDepth are not expanded)
//   maxVisited   : hard cap on discovered nodes (runaway guard)
template <typename NeighborFn, typename AbortFn>
inline GraphPathResult BfsShortestObjectPath(uintptr_t root, uintptr_t target,
                                             int32_t maxDepth, int32_t maxVisited,
                                             NeighborFn&& neighborFn, AbortFn&& abortFn)
{
    GraphPathResult res;
    if (!root || !target) { res.status = "invalid"; return res; }
    if (maxDepth < 1) maxDepth = 1;

    if (root == target) {
        // Target IS the root — the path is just the root node, no edges.
        res.found   = true;
        res.status  = "ok";
        res.visited = 1;
        return res;
    }

    struct ParentLink {
        uintptr_t parent = 0;
        GraphEdge edge;
    };
    std::unordered_map<uintptr_t, ParentLink> visited;
    std::deque<std::pair<uintptr_t, int32_t>> queue;

    visited.emplace(root, ParentLink{});
    queue.push_back({root, 0});

    uintptr_t foundChild = 0;
    bool      capHit     = false;
    uint32_t  pollCtr    = 0;

    while (!queue.empty()) {
        if ((pollCtr++ & 0x3FFu) == 0 && abortFn()) { res.aborted = true; break; }

        const uintptr_t obj   = queue.front().first;
        const int32_t   depth = queue.front().second;
        queue.pop_front();
        if (depth >= maxDepth) continue;   // depth budget exhausted — don't expand

        bool stop = false;
        neighborFn(obj, [&](uintptr_t child, int32_t fieldOffset,
                            const std::string& fieldName, const std::string& fieldType,
                            const std::string& innerType, int32_t elementIndex,
                            int32_t elemStride, int32_t elemValueOffset) -> bool {
            if (!child) return false;
            if (visited.find(child) != visited.end()) return false;  // seen — no retain, no copy
            if (static_cast<int32_t>(visited.size()) >= maxVisited) { capHit = true; stop = true; return true; }

            visited.emplace(child, ParentLink{
                obj, GraphEdge{fieldOffset, fieldName, fieldType, innerType, elementIndex,
                               elemStride, elemValueOffset}});

            if (child == target) { foundChild = child; stop = true; return true; }
            queue.push_back({child, depth + 1});
            return false;
        });
        if (stop) break;
    }

    res.visited = static_cast<int32_t>(visited.size());

    if (res.aborted) { res.status = "aborted"; return res; }
    if (!foundChild) {
        res.status = capHit ? "visited_cap" : "not_reachable";
        return res;
    }

    // Reconstruct target -> root via parent links, then reverse to root -> target.
    std::vector<GraphPathStep> rev;
    uintptr_t cur = target;
    const size_t guard = static_cast<size_t>(maxDepth) + 2;
    while (cur != root) {
        auto it = visited.find(cur);
        if (it == visited.end() || rev.size() > guard) { res.status = "reconstruct_error"; return res; }
        const ParentLink& pl = it->second;
        GraphPathStep st;
        st.fromObj      = pl.parent;
        st.toObj        = cur;
        st.fieldOffset  = pl.edge.fieldOffset;
        st.fieldName    = pl.edge.fieldName;
        st.fieldType    = pl.edge.fieldType;
        st.innerType    = pl.edge.innerType;
        st.elementIndex = pl.edge.elementIndex;
        st.elemStride       = pl.edge.elemStride;
        st.elemValueOffset  = pl.edge.elemValueOffset;
        rev.push_back(std::move(st));
        cur = pl.parent;
    }
    std::reverse(rev.begin(), rev.end());

    res.steps        = std::move(rev);
    res.found        = true;
    res.status       = "ok";
    res.depthReached = static_cast<int32_t>(res.steps.size());
    return res;
}

} // namespace Aura
