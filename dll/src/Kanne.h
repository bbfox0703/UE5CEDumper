// ============================================================
// Kanne — カンネ (水を操る魔法使い — "watering can", the mage who moves water)
// Watch samples ([PIPE-PER-PROCESS-2026-11-06] Stage C): what a pipe `watch` keeps, and how its
// sampling thread hands each sample to the connection that asked without either touching the
// other -- the flow from game memory to the pipe, carried in a can.
//
// The watch thread used to write its own events to its connection's pipe. The pipes are
// synchronous, and a synchronous handle lets one I/O through at a time, so that WriteFile waited
// behind the owner's parked ReadFile -- holding the connection's write lock -- and every join of
// the thread (an unwatch, a replace, the connection's teardown, Stop) waited with it for as long
// as the owner sent nothing; an interval of minutes made the join wait minutes more. Now the
// thread only reads memory and leaves the latest sample here, and the owner's own handler thread
// sends it after each command, so each handle is touched by one thread and a join is bounded by
// one interval wake plus one read of at most 64 KB.
//
// Header-only and std-only, so dll_helpers_test exercises it; Fern.cpp, which uses it, is
// compiled by no test target.
// ============================================================
#pragma once

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <mutex>
#include <vector>

namespace Kanne {

// A watch is keyed by the connection that asked AND the address: two clients watching one
// address each get their own, and neither can stop or replace the other's.
struct WatchKey {
    uint64_t  connSeq = 0;
    uintptr_t addr    = 0;
    bool operator==(const WatchKey& o) const noexcept { return connSeq == o.connSeq && addr == o.addr; }
};

struct WatchKeyHash {
    size_t operator()(const WatchKey& k) const noexcept {
        return std::hash<uint64_t>{}((k.connSeq * 0x9E3779B97F4A7C15ull) ^ static_cast<uint64_t>(k.addr));
    }
};

// How many of `connSeq`'s watches `table` (keyed by WatchKey) holds.
template <class Table>
size_t CountOwned(const Table& table, uint64_t connSeq) {
    size_t n = 0;
    for (const auto& kv : table)
        if (kv.first.connSeq == connSeq) ++n;
    return n;
}

// What one sampling thread shares with its connection's handler, and nothing else.
class WatchSlot {
public:
    struct Sample {
        std::vector<uint8_t> bytes;
        int64_t              timestampMs = 0;   // when it was read (system clock, ms since the epoch)
        uint32_t             samples     = 0;   // how many reads this one stands for since the last take
    };

    // The sampling thread waits here between reads. True: read again. False: stop was requested.
    bool WaitInterval(std::chrono::milliseconds interval) {
        std::unique_lock<std::mutex> lk(mu_);
        cv_.wait_for(lk, interval, [this] { return stop_; });
        return !stop_;
    }

    void RequestStop() {
        {
            std::lock_guard<std::mutex> lk(mu_);
            stop_ = true;
        }
        cv_.notify_all();
    }

    // The latest read replaces any the handler has not sent yet.
    void Publish(const uint8_t* bytes, size_t n, int64_t timestampMs) {
        std::lock_guard<std::mutex> lk(mu_);
        bytes_.assign(bytes, bytes + n);
        timestampMs_ = timestampMs;
        if (pending_ != UINT32_MAX) ++pending_;
    }

    // The latest read not yet sent; false when none came since the last take.
    bool TakeLatest(Sample& out) {
        std::lock_guard<std::mutex> lk(mu_);
        if (pending_ == 0) return false;
        out.bytes       = bytes_;
        out.timestampMs = timestampMs_;
        out.samples     = pending_;
        pending_ = 0;
        return true;
    }

private:
    std::mutex              mu_;
    std::condition_variable cv_;
    bool                    stop_ = false;
    std::vector<uint8_t>    bytes_;
    int64_t                 timestampMs_ = 0;
    uint32_t                pending_ = 0;
};

}  // namespace Kanne
