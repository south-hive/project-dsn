#pragma once
#include <chrono>
#include <cstdint>
#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace dsn {
struct SourceOptions {
    std::string source_id;
    std::string host = "127.0.0.1"; // Numeric IPv4 or IPv6; no DNS.
    std::uint16_t port = 7070;
    std::size_t capacity = 256;
    std::chrono::milliseconds timeout{1000};
};
struct SourceStats {
    std::uint64_t accepted, sent, failed, rejected, discarded;
};

class Source {
public:
    explicit Source(SourceOptions options);
    ~Source();
    Source(const Source&) = delete;
    Source& operator=(const Source&) = delete;
    // Copies bytes, briefly locks, and admits without waiting for network/queue space.
    // Invalid arguments throw. False means full or closed; true is not a delivery ACK.
    bool publish(std::string_view payload, const std::vector<std::string>& workspaces,
                 std::string_view event_type = "normal");
    bool flush(std::chrono::milliseconds timeout = std::chrono::seconds(5));
    SourceStats stats() const;
    // Discards queued frames and joins the active attempt. Owner calls close/destructor
    // after producer threads finish; publish/flush/stats may be called concurrently.
    void close();
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
}
