"""SDK admission/lifetime tests and real C++/Python -> plugin -> journal -> View."""
import base64
from contextlib import contextmanager
import json
import os
from pathlib import Path
import queue
import shutil
import socket
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "sdk/source/python/src"))
from dsn_source import Source


def run(*args, cwd=ROOT, env=None, timeout=120):
    result = subprocess.run(args, cwd=cwd, env=env, capture_output=True, text=True, timeout=timeout)
    if result.returncode:
        raise AssertionError(result.stdout + result.stderr)
    return result.stdout


@contextmanager
def receiver():
    frames, errors = [], []
    listener = socket.socket()
    listener.bind(("127.0.0.1", 0))
    listener.listen()
    listener.settimeout(5)
    def receive():
        try:
            connection, _ = listener.accept()
            with connection:
                connection.settimeout(5)
                pending = b""
                while data := connection.recv(65536):
                    pending += data
                    while b"\n" in pending:
                        frame, pending = pending.split(b"\n", 1)
                        frames.append(json.loads(frame))
                assert not pending, "partial frame"
        except Exception as error:
            errors.append(error)
    thread = threading.Thread(target=receive, daemon=True)
    thread.start()
    try:
        yield listener.getsockname()[1], frames
    finally:
        thread.join(6)
        listener.close()
        assert not thread.is_alive(), "receiver did not finish"
        if errors:
            raise errors[0]


@contextmanager
def host(settings):
    process = subprocess.Popen(["dotnet", str(ROOT / "src/Dsn.Host/bin/Release/net10.0/Dsn.Host.dll"), str(settings)],
                               cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    lines = queue.Queue()
    def read():
        for line in process.stdout:
            lines.put(line)
        lines.put(None)
    reader = threading.Thread(target=read, daemon=True)
    reader.start()
    try:
        deadline = time.monotonic() + 30
        output = []
        while True:
            line = lines.get(timeout=max(0.01, deadline - time.monotonic()))
            if line is None:
                raise AssertionError("Host exited: " + "".join(output))
            output.append(line)
            try:
                ready = json.loads(line)
                if ready.get("status") == "ready":
                    break
            except json.JSONDecodeError:
                pass
        yield ready
    finally:
        if process.poll() is None:
            process.terminate()
        try:
            process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
            raise AssertionError("Host did not stop")
        reader.join(2)
        process.stdout.close()
        if process.returncode != 0:
            raise AssertionError(f"Host exited with {process.returncode}")


class SourceTests(unittest.TestCase):
    def test_python_binary_copy_and_limits(self):
        with receiver() as (port, frames):
            with Source("python-tests", port=port) as source:
                binary = bytearray(range(256))
                for payload in (b"", binary, b"\xff" * 16384):
                    self.assertTrue(source.publish(payload, ["echo", "hex"]))
                binary[:] = b"z" * 256
                for payload, targets in [(b"x" * 16385, ["echo"]), (b"x", []), (b"x", ["UPPER"])]:
                    with self.assertRaises(ValueError):
                        source.publish(payload, targets)
                self.assertTrue(source.flush())
                self.assertEqual(3, source.stats.sent)
        self.assertEqual([b"", bytes(range(256)), b"\xff" * 16384],
                         [base64.b64decode(f["params"]["payload"], validate=True) for f in frames])
        for frame in frames:
            self.assertEqual("dsn.publish", frame["method"])
            self.assertNotIn("id", frame)

    def test_python_queue_close_and_flush(self):
        entered, release = threading.Event(), threading.Event()
        class HeldSocket:
            def settimeout(self, value): pass
            def connect(self, address): pass
            def sendall(self, frame):
                entered.set()
                if not release.wait(5): raise TimeoutError()
            def close(self): pass
        with patch("dsn_source.socket.socket", return_value=HeldSocket()):
            source = Source("queue", capacity=1)
            try:
                self.assertTrue(source.publish(b"one", ["echo"]))
                self.assertTrue(entered.wait(2))
                self.assertTrue(source.publish(b"two", ["echo"]))
                self.assertFalse(source.publish(b"three", ["echo"]))
                self.assertFalse(source.flush(0))
                closer = threading.Thread(target=source.close)
                closer.start()
                deadline = time.monotonic() + 2
                while source.stats.discarded != 1 and time.monotonic() < deadline:
                    time.sleep(0.005)
                self.assertEqual(1, source.stats.discarded)
                self.assertTrue(closer.is_alive())
            finally:
                release.set()
                source.close()
                if "closer" in locals(): closer.join(2)
            self.assertFalse(source.publish(b"closed", ["echo"]))
            self.assertEqual((2, 1, 0, 2, 1), tuple(vars(source.stats).values()))

    def test_python_send_failure_is_not_replayed(self):
        attempts = []
        class FailedSocket:
            def settimeout(self, value): pass
            def connect(self, address): pass
            def sendall(self, frame):
                attempts.append(json.loads(frame)["params"]["payload"])
                if len(attempts) == 1: raise OSError("partial write failure")
            def close(self): pass
        with patch("dsn_source.socket.socket", side_effect=lambda *args: FailedSocket()):
            with Source("failure") as source:
                source.publish(b"first", ["echo"])
                source.publish(b"second", ["echo"])
                self.assertTrue(source.flush())
                self.assertEqual((1, 1), (source.stats.sent, source.stats.failed))
        self.assertEqual([b"first", b"second"], [base64.b64decode(x) for x in attempts])

    def test_cpp_binary_and_concurrent_frames(self):
        fixture = str(ROOT / "artifacts/source-cpp/source_test")
        with receiver() as (port, frames):
            run(fixture, "encode", str(port))
        self.assertEqual([b"", bytes(range(256)), b"\xff" * 16384],
                         [base64.b64decode(f["params"]["payload"], validate=True) for f in frames])
        with receiver() as (port, frames):
            run(fixture, "concurrent", str(port))
        self.assertEqual({f"{i}:{j}" for i in range(8) for j in range(20)},
                         {base64.b64decode(f["params"]["payload"]).decode() for f in frames})
        self.assertEqual(160, len(frames))

    def test_cpp_disconnect_saturation_and_bounded_close(self):
        fixture = str(ROOT / "artifacts/source-cpp/source_test")
        with socket.socket() as unavailable:
            unavailable.bind(("127.0.0.1", 0))  # Reserved but not listening: deterministic refusal.
            run(fixture, "failure", str(unavailable.getsockname()[1]))
        with socket.socket() as stalled:
            stalled.bind(("127.0.0.1", 0))
            stalled.listen()  # TCP connects, but the peer never consumes application data.
            run(fixture, "saturation", str(stalled.getsockname()[1]), timeout=15)

    def test_packaged_sdks_and_external_workspace_end_to_end(self):
        with tempfile.TemporaryDirectory(prefix="dsn-sdk-e2e-") as temporary:
            base = Path(temporary)
            package = ROOT / "artifacts/sdk"
            python_sdk = base / "python"
            with zipfile.ZipFile(package / "dsn_source-0.1.0-py3-none-any.whl") as wheel:
                wheel.extractall(python_sdk)
            cpp = base / "cpp"
            cpp.mkdir()
            shutil.copy(ROOT / "samples/temperature/source.cpp", cpp / "main.cpp")
            (cpp / "CMakeLists.txt").write_text('''cmake_minimum_required(VERSION 3.16)
project(consumer LANGUAGES CXX)
find_package(dsn_source 0.1 CONFIG REQUIRED)
add_executable(consumer main.cpp)
target_link_libraries(consumer PRIVATE dsn::source)
''')
            run("cmake", "-S", str(cpp), "-B", str(cpp / "build"), "-DCMAKE_PREFIX_PATH=" + str(package / "cpp"))
            run("cmake", "--build", str(cpp / "build"), "--parallel", "2")
            plugin = base / "plugin"
            plugin.mkdir()
            shutil.copy(ROOT / "samples/temperature/Dsn.Workspaces.Temperature/TemperatureWorkspace.cs", plugin)
            (plugin / "ExternalTemperature.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings><EnableDynamicLoading>true</EnableDynamicLoading></PropertyGroup>
<ItemGroup><PackageReference Include="Dsn.Contracts" Version="0.1.0">
<ExcludeAssets>runtime</ExcludeAssets></PackageReference></ItemGroup></Project>''')
            run("dotnet", "build", str(plugin), "-c", "Release", "--source", str(package), "-p:NuGetAudit=false",
                env={**os.environ, "NUGET_PACKAGES": str(base / "nuget")})
            output = plugin / "bin/Release/net10.0"
            self.assertFalse((output / "Dsn.Contracts.dll").exists())
            self.assertFalse((output / "Dsn.Runtime.dll").exists())
            settings = base / "settings.json"
            settings.write_text(json.dumps({"rpcPort": 0, "viewPort": 0, "dataDirectory": str(base / "data"),
                                           "plugins": [str(output / "ExternalTemperature.dll")]}))
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
            def rows(address):
                with opener.open(address + "/view?workspaces=temperature&fields=source_id,sensor,celsius,sequence,message_id", timeout=3) as response:
                    return json.load(response)
            with host(settings) as ready:
                run(sys.executable, str(ROOT / "samples/temperature/source.py"), "--port", str(ready["rpcPort"]),
                    env={**os.environ, "PYTHONPATH": str(python_sdk)})
                run(str(cpp / "build/consumer"), "127.0.0.1", str(ready["rpcPort"]))
                deadline = time.monotonic() + 5
                while True:
                    try:
                        saved = rows(ready["view"])
                        if len(saved) == 6: break
                    except urllib.error.HTTPError as error:
                        if error.code != 400: raise  # Fields appear after the first persisted record.
                    if time.monotonic() > deadline: self.fail("Six records were not persisted")
                    time.sleep(0.02)
                expected = {(source, sequence, 20 + sequence * 0.5, "lab-01")
                            for source in ("temperature-python", "temperature-cpp") for sequence in range(1, 4)}
                self.assertEqual(expected, {(r["source_id"], r["sequence"], r["celsius"], r["sensor"]) for r in saved})
            with host(settings) as ready:
                self.assertEqual(saved, rows(ready["view"]))


if __name__ == "__main__":
    unittest.main()
