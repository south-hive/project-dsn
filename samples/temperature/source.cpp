#include <dsn/source.hpp>
#include <iostream>
#include <locale>
#include <sstream>
#include <stdexcept>

int main(int argc, char** argv) {
    try {
        if (argc > 4) throw std::invalid_argument("Usage: temperature_source [host] [port] [count=3]");
        dsn::SourceOptions options;
        options.source_id = "temperature-cpp";
        if (argc > 1) options.host = argv[1];
        int port = argc > 2 ? std::stoi(argv[2]) : 7070;
        int count = argc > 3 ? std::stoi(argv[3]) : 3;
        if (port < 1 || port > 65535 || count < 1 || count > 256) throw std::invalid_argument("port/count out of range");
        options.port = static_cast<std::uint16_t>(port);
        dsn::Source source(options);
        for (int sequence = 1; sequence <= count; ++sequence) {
            std::ostringstream payload;
            payload.imbue(std::locale::classic());
            payload << "{\"schema\":\"temperature.v1\",\"sensor\":\"lab-01\",\"celsius\":"
                    << 20 + sequence * 0.5 << ",\"sequence\":" << sequence << '}';
            if (!source.publish(payload.str(), {"temperature"})) throw std::runtime_error("local queue rejected a sample");
        }
        if (!source.flush()) throw std::runtime_error("local send timeout");
        auto stats = source.stats();
        std::cout << "accepted=" << stats.accepted << " sent=" << stats.sent << " failed=" << stats.failed << '\n';
        if (stats.sent != static_cast<std::uint64_t>(count)) throw std::runtime_error("local send failed; no automatic replay");
        std::cout << "Local writes completed; verify persisted records through HTTP View.\n";
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
