// User-mode bridge: reads preformatted payload lines from a driver reader's stdout.
// Kernel access and the device's IOCTL/read contract belong to that reader.
#include "dsn/source.hpp"
#include <iostream>
#include <stdexcept>

int main(int argc, char** argv) {
    try {
        if (argc < 2 || argc > 4) throw std::invalid_argument("usage: bench_bridge SOURCE_ID [HOST] [PORT]");
        dsn::SourceOptions options;
        options.source_id = argv[1];
        if (argc > 2) options.host = argv[2];
        if (argc > 3) {
            std::string port = argv[3]; std::size_t end{}; int n = std::stoi(port, &end);
            if (end != port.size() || n < 1 || n > 65535) throw std::invalid_argument("port");
            options.port = static_cast<std::uint16_t>(n);
        }
        dsn::Source source(options);
        std::string line;
        while (std::getline(std::cin, line)) {
            if (line.empty()) continue;
            if (!source.publish(line, {"bench"})) std::cerr << "Source queue full\n";
        }
        const bool flushed = source.flush(std::chrono::seconds(10));
        const auto stats = source.stats();
        std::cerr << "sent=" << stats.sent << " failed=" << stats.failed << " rejected=" << stats.rejected << '\n';
        return !flushed || stats.failed || stats.rejected ? 1 : 0;
    } catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
