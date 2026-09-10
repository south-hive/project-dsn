#include "dsn/source.hpp"
#include <arpa/inet.h>
#include <sys/socket.h>
#include <poll.h>
#include <unistd.h>
#include <cerrno>
#include <condition_variable>
#include <ctime>
#include <deque>
#include <mutex>
#include <stdexcept>
#include <thread>

namespace dsn {
namespace {
bool letter(char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'); }
bool digit(char c) { return c >= '0' && c <= '9'; }
void identifier(std::string_view text, std::size_t limit) {
    if (text.empty() || text.size() > limit) throw std::invalid_argument("metadata length");
    for (char c : text)
        if (!letter(c) && !digit(c) && c != '.' && c != '_' && c != ':' && c != '-')
            throw std::invalid_argument("metadata must be an ASCII identifier");
}
void workspace(std::string_view text) {
    if (text.empty() || text.size() > 64 || text[0] < 'a' || text[0] > 'z')
        throw std::invalid_argument("workspace name");
    for (char c : text)
        if (!(c >= 'a' && c <= 'z') && !digit(c) && c != '-') throw std::invalid_argument("workspace name");
}
std::string base64(std::string_view bytes) {
    constexpr char alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string result;
    result.reserve((bytes.size() + 2) / 3 * 4);
    for (std::size_t i = 0; i < bytes.size(); i += 3) {
        unsigned n = static_cast<unsigned char>(bytes[i]) << 16;
        if (i + 1 < bytes.size()) n |= static_cast<unsigned char>(bytes[i + 1]) << 8;
        if (i + 2 < bytes.size()) n |= static_cast<unsigned char>(bytes[i + 2]);
        result += alphabet[(n >> 18) & 63]; result += alphabet[(n >> 12) & 63];
        result += i + 1 < bytes.size() ? alphabet[(n >> 6) & 63] : '=';
        result += i + 2 < bytes.size() ? alphabet[n & 63] : '=';
    }
    return result;
}
std::string timestamp() {
    auto now = std::chrono::system_clock::to_time_t(std::chrono::system_clock::now());
    std::tm utc{};
    if (!gmtime_r(&now, &utc)) throw std::runtime_error("UTC time unavailable");
    char text[32]{};
    if (!std::strftime(text, sizeof(text), "%Y-%m-%dT%H:%M:%SZ", &utc)) throw std::runtime_error("UTC time invalid");
    return text;
}
using Clock = std::chrono::steady_clock;
bool writable(int fd, Clock::time_point deadline) {
    for (;;) {
        auto left = std::chrono::duration_cast<std::chrono::milliseconds>(deadline - Clock::now()).count();
        if (left <= 0) return false;
        pollfd descriptor{fd, POLLOUT, 0};
        int result = ::poll(&descriptor, 1, static_cast<int>(left));
        if (result < 0 && errno == EINTR) continue;
        return result > 0 && (descriptor.revents & POLLOUT) && !(descriptor.revents & (POLLERR | POLLHUP | POLLNVAL));
    }
}
}

struct Source::Impl {
    SourceOptions options;
    sockaddr_storage address{};
    socklen_t address_size{};
    mutable std::mutex mutex;
    std::condition_variable changed;
    std::deque<std::string> queue;
    bool closed = false, active = false;
    SourceStats counts{};
    std::thread worker;

    explicit Impl(SourceOptions value) : options(std::move(value)) {
        identifier(options.source_id, 128);
        if (!options.port || !options.capacity || options.capacity > 65536 ||
            options.timeout.count() < 1 || options.timeout > std::chrono::seconds(60))
            throw std::invalid_argument("port, capacity or timeout");
        auto* v4 = reinterpret_cast<sockaddr_in*>(&address);
        auto* v6 = reinterpret_cast<sockaddr_in6*>(&address);
        if (inet_pton(AF_INET, options.host.c_str(), &v4->sin_addr) == 1) {
            v4->sin_family = AF_INET; v4->sin_port = htons(options.port); address_size = sizeof(*v4);
        } else if (inet_pton(AF_INET6, options.host.c_str(), &v6->sin6_addr) == 1) {
            v6->sin6_family = AF_INET6; v6->sin6_port = htons(options.port); address_size = sizeof(*v6);
        } else throw std::invalid_argument("host must be a numeric IPv4 or IPv6 address");
        worker = std::thread([this] { run(); });
    }
    bool send_frame(int& fd, const std::string& frame) {
        auto deadline = Clock::now() + options.timeout;
        if (fd < 0) {
            fd = ::socket(address.ss_family, SOCK_STREAM | SOCK_NONBLOCK | SOCK_CLOEXEC, 0);
            if (fd < 0) return false;
            if (::connect(fd, reinterpret_cast<sockaddr*>(&address), address_size) < 0) {
                if (errno != EINPROGRESS || !writable(fd, deadline)) return false;
                int error = 0; socklen_t size = sizeof(error);
                if (::getsockopt(fd, SOL_SOCKET, SO_ERROR, &error, &size) < 0 || error) return false;
            }
        }
        std::size_t offset = 0;
        while (offset < frame.size()) {
            if (Clock::now() >= deadline) return false;
            auto sent = ::send(fd, frame.data() + offset, frame.size() - offset, MSG_NOSIGNAL);
            if (sent > 0) { offset += static_cast<std::size_t>(sent); continue; }
            if (sent < 0 && errno == EINTR) continue;
            if (sent < 0 && (errno == EAGAIN || errno == EWOULDBLOCK) && writable(fd, deadline)) continue;
            return false;
        }
        return true;
    }
    void run() {
        int fd = -1;
        for (;;) {
            std::string frame;
            {
                std::unique_lock<std::mutex> lock(mutex);
                changed.wait(lock, [this] { return closed || !queue.empty(); });
                if (closed) break;
                frame = std::move(queue.front()); queue.pop_front(); active = true;
            }
            bool success = send_frame(fd, frame);
            if (!success && fd >= 0) { ::close(fd); fd = -1; }
            {
                std::lock_guard<std::mutex> lock(mutex);
                if (success) ++counts.sent; else ++counts.failed;
                active = false;
            }
            changed.notify_all();
        }
        if (fd >= 0) ::close(fd);
    }
};

Source::Source(SourceOptions options) : impl_(std::make_unique<Impl>(std::move(options))) {}
Source::~Source() { close(); }
bool Source::publish(std::string_view payload, const std::vector<std::string>& workspaces, std::string_view event_type) {
    identifier(event_type, 64);
    if (payload.size() > 16384 || workspaces.empty() || workspaces.size() > 32)
        throw std::invalid_argument("payload or workspace count exceeds limits");
    std::string targets;
    for (const auto& name : workspaces) {
        workspace(name);
        if (!targets.empty()) targets += ',';
        targets += '"' + name + '"';
    }
    // Metadata is validated ASCII; payload is opaque bytes encoded as canonical base64.
    std::string frame = "{\"jsonrpc\":\"2.0\",\"method\":\"dsn.publish\",\"params\":{\"version\":1,\"source_id\":\"" +
        impl_->options.source_id + "\",\"time\":\"" + timestamp() + "\",\"event_type\":\"" + std::string(event_type) +
        "\",\"workspace\":[" + targets + "],\"payload\":\"" + base64(payload) + "\"}}\n";
    std::lock_guard<std::mutex> lock(impl_->mutex);
    if (impl_->closed || impl_->queue.size() >= impl_->options.capacity) { ++impl_->counts.rejected; return false; }
    impl_->queue.push_back(std::move(frame)); ++impl_->counts.accepted;
    impl_->changed.notify_all();
    return true;
}
bool Source::flush(std::chrono::milliseconds timeout) {
    if (timeout.count() < 0) throw std::invalid_argument("negative flush timeout");
    std::unique_lock<std::mutex> lock(impl_->mutex);
    return impl_->changed.wait_for(lock, timeout, [this] { return impl_->queue.empty() && !impl_->active; });
}
SourceStats Source::stats() const { std::lock_guard<std::mutex> lock(impl_->mutex); return impl_->counts; }
void Source::close() {
    {
        std::lock_guard<std::mutex> lock(impl_->mutex);
        impl_->closed = true; impl_->counts.discarded += impl_->queue.size(); impl_->queue.clear();
    }
    impl_->changed.notify_all();
    if (impl_->worker.joinable()) impl_->worker.join();
}
}
