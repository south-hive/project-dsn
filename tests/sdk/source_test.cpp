#include <dsn/source.hpp>
#include <iostream>
#include <stdexcept>
#include <thread>

void check(bool value) { if (!value) throw std::runtime_error("SDK assertion failed"); }
template<class F> void invalid(F action) {
    try { action(); } catch (const std::invalid_argument&) { return; }
    throw std::runtime_error("Expected invalid_argument");
}
int main(int argc, char** argv) {
    try {
        check(argc == 3);
        std::string mode = argv[1];
        dsn::SourceOptions options;
        options.source_id = "cpp-tests";
        options.port = static_cast<std::uint16_t>(std::stoi(argv[2]));
        options.timeout = std::chrono::milliseconds(100);
        if (mode == "saturation") options.capacity = 1;
        dsn::Source source(options);
        invalid([&] { source.publish(std::string(16385, 'x'), {"echo"}); });
        invalid([&] { source.publish("x", {}); });
        invalid([&] { source.publish("x", {"UPPER"}); });
        invalid([&] { source.publish("x", {"echo"}, "bad\"event"); });
        if (mode == "encode") {
            std::string binary;
            for (int i = 0; i < 256; ++i) binary += static_cast<char>(i);
            check(source.publish("", {"echo", "hex"}));
            check(source.publish(binary, {"echo", "hex"}));
            binary.assign(256, 'z'); // publish must already own its encoded copy.
            check(source.publish(std::string(16384, '\xff'), {"echo", "hex"}));
            check(source.flush()); check(source.stats().sent == 3);
        } else if (mode == "concurrent") {
            std::vector<std::thread> producers;
            for (int i = 0; i < 8; ++i) producers.emplace_back([&, i] {
                for (int j = 0; j < 20; ++j) source.publish(std::to_string(i) + ":" + std::to_string(j), {"echo"});
            });
            for (auto& producer : producers) producer.join();
            check(source.flush()); check(source.stats().sent == 160); check(source.stats().rejected == 0);
        } else if (mode == "failure") {
            check(source.publish("x", {"echo"}));
            check(source.flush()); check(source.stats().failed == 1); check(source.stats().sent == 0);
        } else if (mode == "saturation") {
            for (int i = 0; i < 2000; ++i) source.publish(std::string(16384, 'x'), {"echo"});
            check(source.stats().rejected > 0);
        } else throw std::invalid_argument("Unknown test mode");
        auto start = std::chrono::steady_clock::now();
        source.close(); source.close();
        check(std::chrono::steady_clock::now() - start < std::chrono::seconds(2));
        check(!source.publish("after-close", {"echo"}));
        auto stats = source.stats();
        check(stats.accepted == stats.sent + stats.failed + stats.discarded);
        std::cout << "PASS " << mode << '\n';
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
