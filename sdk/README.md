# DSN SDK

Source SDK는 bytes를 전송하고, Workspace SDK는 서버 안에서 그 bytes를 해석·저장하는 계약이다. 같은 저장소에서 관리하되 언어별 산출물은 독립적이다. Source SDK는 C# 서버 프로젝트를 참조하지 않는다.

| 개발 대상 | 위치 / 배포 형태 | 참조하는 계약 |
| --- | --- | --- |
| Python Source | [source/python](source/python), `dsn-source` wheel | TCP notification v1 |
| C++ Source | [source/cpp](source/cpp), CMake `dsn::source` static library | 같은 TCP notification v1 |
| C# Workspace | [개발 안내](workspace/README.md), `Dsn.Contracts` NuGet | plugin API v1, lease·record·diagnostics |

두 Source와 Workspace가 같은 규격으로 연동하는 [온도 예제](../samples/temperature/README.md)를 먼저 실행하면 전체 흐름을 볼 수 있다. 업무 payload 규격은 해당 예제/업무 모듈이 소유하며 Source SDK에 넣지 않는다.

실제 개발 순서는 [Source 개발자 가이드](../docs/guides/source.md)와 [Workspace 개발자 가이드](../docs/guides/workspace.md)를 참고한다.

## 빌드와 배포

```bash
# Termux: C# 서버 검증 환경에 추가
pkg install clang cmake python
make sdk-check     # 서버 테스트, 패키징, 언어별 SDK 시험, 실제 Host 연동
make sdk-pack      # 패키징만: artifacts/sdk/
```

필요 도구는 .NET 10 SDK, Make/Bash, CMake 3.16+, C++17 compiler, Python 3.10+와 pip다. Python wheel 빌드 도구는 requirements-dev.lock의 버전/hash로 선택적 `.dev/python-tools` 디렉터리에 설치하며 venv를 사용하지 않는다. 기본 .NET 빌드에는 Python이 필요 없다. `OFFLINE=1`은 반입한 wheel만 사용한다. [격리 환경·반입 절차](../docs/development-environment.md). 외부 registry에 자동 게시하지 않는다.

| 산출물 | 다른 프로젝트에서 사용 |
| --- | --- |
| `dsn_source-0.1.0-py3-none-any.whl` | `python -m pip install <wheel 경로>` |
| `cpp/`의 include·lib·CMake 설정 | `-DCMAKE_PREFIX_PATH=<cpp 경로>`, `find_package(dsn_source CONFIG REQUIRED)` 후 `target_link_libraries(app PRIVATE dsn::source)` |
| `Dsn.Contracts.0.1.0.nupkg` | 로컬 NuGet feed에서 참조. [Workspace 안내](workspace/README.md) |

C++ SDK는 Linux/Android(Termux)를 대상으로 하며 바이너리는 해당 OS·CPU·C++ toolchain에 맞게 다시 빌드한다. Windows/macOS C++ 전송기는 아직 없다. Python은 표준 라이브러리만 사용하지만 현재 실행 검증 환경은 Termux다. SDK 설치만 필요할 때는 `cmake -S sdk/source/cpp -B artifacts/source-cpp-only`로 예제·시험 없이 빌드할 수 있다.

## Source API

```python
from dsn_source import Source

with Source("tool-python", host="127.0.0.1", port=7070) as source:
    accepted = source.publish(b"hello", ["echo"], event_type="normal")
    completed = source.flush(timeout=5)  # 프로그램 종료 전 로컬 전송 시도만 기다림
    print(accepted, completed, source.stats)
```

```cpp
#include <dsn/source.hpp>
dsn::SourceOptions options;
options.source_id = "tool-cpp";
dsn::Source source(options);
bool accepted = source.publish("hello", {"echo"});
bool completed = source.flush();
auto stats = source.stats();
```

`publish`는 payload 복사/base64·JSON encoding과 짧은 mutex 획득을 수행하고, 네트워크나 빈 queue 자리를 기다리지 않는다. 큐에 들어가면 true, 포화/종료 상태면 false다. 잘못된 인자는 Python 예외/C++ `invalid_argument`로 보고한다. lock-free나 실시간 지연 상한을 보장하는 API는 아니다.

| 항목 | v0.1 규칙 |
| --- | --- |
| source_id / event_type | ASCII `[A-Za-z0-9._:-]`, 각각 1–128 / 1–64자. 서버보다 좁은 SDK 입력 범위 |
| 목적지 | 숫자 IPv4/IPv6 주소, 포트 1–65535. DNS 해석 없음 |
| payload / Workspace | 불투명 bytes 최대 16,384, `[a-z][a-z0-9-]{0,63}` 이름 1–32개 |
| 봉투 | version=1, UTC 발행 시각 자동 생성, source_description 생략 |
| queue | 기본 256개, 설정 1–65,536개. 대기 frame 외 전송 중 frame 최대 1개 |
| 전송 | 한 Source 인스턴스에 고정 source_id와 TCP 연결 하나. LF JSON, canonical base64 |
| 실패 | 현재 frame 폐기, 연결 닫기. 이후 새 frame은 재연결 가능. 실패 frame 재전송 없음 |
| flush | queue와 진행 중 전송 시도가 끝날 때까지 제한 시간 대기. 실패도 완료에 포함 |
| close | 신규 접수 중단, 대기 frame 폐기, 진행 중 시도 종료 후 반환. 필요하면 먼저 flush |

`accepted`는 로컬 큐 접수 수, `sent`는 로컬 socket write 완료 수, `failed`는 전송 실패 수, `rejected`는 접수 거부 수, `discarded`는 종료 시 대기열 폐기 수다. **어느 것도 서버의 접수·해석·저장 ACK가 아니다.** Source당 활성 연결은 서버에서 하나만 허용하므로 실행 중인 producer마다 source_id를 구분한다.

네트워크 timeout 기본값은 1초다. C++은 연결+전송 시도 전체에 적용하고, Python은 connect와 sendall에 각각 적용한다. close는 그 진행 중 시도를 기다릴 수 있다. Python은 context manager 또는 명시적 close가 필요하며 daemon thread는 프로세스 강제 종료 시 전송을 보존하지 않는다. C++ close/destructor는 producer thread를 종료한 뒤 소유자가 호출한다. 관련 OS 규칙은 [Python socket](https://docs.python.org/3/library/socket.html)과 [Linux send](https://man7.org/linux/man-pages/man2/send.2.html)에 있다.
