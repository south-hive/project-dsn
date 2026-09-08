# DSN — C# implementation

다양한 Source의 불투명 메시지를 RPC로 받아 Workspace에서 record로 변환하고, 영속 저장 및 사용자별 View를 제공한다. C#/.NET 10 본체는 `src/`, **테스트 전용 Source**는 `tests/Dsn.TestSource/`에 있다. C++ 앱·driver/eBPF·production Source SDK는 외부 담당 범위다. 기존 TypeScript 실행 모델은 `prototype/`에 보존한다.

## 부서원 개발 문서

[전체에서 내 작업까지](docs/development/README.md)에서 시작한다. 전체 목적 → 기능 분해도 → 작업 배분도 → 담당자별 부분 그림·지시서 순서로 읽는다. 역할별로 필요한 코드·입출력·완료 조건을 연결했다.

## Termux에서 실행

```bash
pkg install dotnet-sdk-10.0
bash scripts/check.sh
# 터미널 1: RPC 7070 / HTTP 7071
dotnet run --project src/Dsn.Host -c Release --no-build
# 터미널 2: 테스트 메시지 발행
dotnet run --project tests/Dsn.TestSource -c Release --no-build -- 7070 hello 3
curl 'http://127.0.0.1:7071/view?workspaces=echo,hex&fields=id,workspace,message_id,payload_utf8,payload_hex'
```

기본 설정은 loopback, 인증 없는 로컬 조회다. `Ctrl+C` 또는 SIGTERM으로 종료한다. 설정 파일을 첫 인자로 넘긴다. 기본값과 모든 키는 [settings.example.json](settings.example.json)에 있다. 상대 경로는 실행한 작업 디렉터리 기준이다.

```bash
dotnet run --project src/Dsn.Host -c Release --no-build -- settings.example.json
# 독립 Mock: 동일 RPC 입력, envelope와 앞 256 bytes의 hex 출력
dotnet run --project src/Dsn.Mock -c Release --no-build -- 7072
# Source는 network write를 기다리는 시험 도구이며 전달 성공을 확인하지 않는다.
dotnet run --project tests/Dsn.TestSource -c Release --no-build -- 7072 hello
```

검증 환경은 Termux Android arm64, SDK 10.0.111, runtime/ASP.NET Core 10.0.11 (`linux-bionic-arm64`)이다. Termux 패키지는 Mono runtime을 사용한다. Linux 컨테이너의 CoreCLR 환경과 성능을 동일하다고 가정하지 않는다. 외부 NuGet 패키지를 요구하지 않는다. [Termux 공식 패키지 정의](https://github.com/termux/termux-packages/blob/master/packages/dotnet10.0/build.sh)에서 포팅 구성을 확인할 수 있다.

## 구성

| 프로젝트 | 책임 |
| --- | --- |
| Dsn.Contracts | Workspace·lease·record·진단 공개 계약 |
| Dsn.Core | RPC decoder/session, ref count, bounded FIFO, 순차 실행, 오류 집계, journal, View, plugin loader |
| Dsn.Workspaces | payload 해석 예제인 echo/hex DLL plugin |
| Dsn.Host | ASP.NET Core View, 사용자 scope, 시작·종료·Admin 조립 |
| Dsn.Mock | 실제 DSN 저장/Workspace 없이 독립 RPC console echo |
| tests/Dsn.TestSource | 테스트 전용 notification 발행 CLI |
| tests/Dsn.Tests | 외부 테스트 framework 없이 실행하는 기능·통합 검증 |

## View

- `GET /fields`: 접근 가능한 Workspace별 field 목록.
- `GET /view?workspaces=echo,hex&fields=id,workspace,payload_utf8,payload_hex&afterId=0&limit=100`: record 순서로 field 선택·조합. 없는 값은 null.
- `GET /export?workspaces=echo&afterId=0&limit=100`: 원래 record의 NDJSON export.
- `PUT /views/{name}`: `{"workspaces":["echo","hex"],"fields":["id","workspace","payload_utf8","payload_hex"]}`로 사용자별 정의 저장.
- `GET /views`: 현재 사용자의 정의 목록. `GET /views/{name}`: 저장된 정의로 조회. `afterId`/`limit` 지원.

field 조합은 여러 Workspace record를 동일 column 목록에 투영하는 방식이다. 암묵적 timestamp join은 하지 않는다. 같은 입력에서 파생된 echo/hex record는 `message_id`로 상관관계를 확인한다. `id`를 선택하면 마지막 id로 다음 페이지를 조회할 수 있다. limit 1–1000, 존재하지 않는 field는 400, 해당 row에만 없는 field는 null이다.

원격 바인딩에는 `users` 설정이 필요하다. 예:

```json
"users": {
  "tester": {"token": "replace-with-a-random-secret", "workspaces": ["echo", "hex"]},
  "operator": {"token": "replace-with-another-secret", "workspaces": ["admin", "echo", "hex"]}
}
```

`Authorization: Bearer <token>`으로 사용자를 식별한다. 정의는 사용자별로 격리하고 record는 허용된 Workspace 전체 범위에서 조회한다. Source별 ACL은 제공하지 않는다. 토큰 오류는 401, scope 밖 Workspace는 403이다. HTTP endpoint를 외부에 노출할 때는 TLS reverse proxy를 사용한다.

## Plugin

`IWorkspacePlugin`의 public 기본 생성자, `ApiVersion = 1`, `Create(WorkspaceServices)`를 구현한다. [echo/hex 구현](src/Dsn.Workspaces/Workspaces.cs)이 예제다. 설정의 `plugins`에 assembly 절대/상대 경로를 지정하면 기본 echo/hex 대신 해당 DLL의 모든 factory를 로딩한다. 의존 DLL과 `.deps.json`을 함께 배치한다. Contracts assembly는 Host와 공유한다. 시작 시에만 로딩하며 신뢰하는 로컬 DLL을 전제로 한다.

등록 이름 중복은 거부한다. 명시한 plugin은 필수이므로 로드/버전/등록 실패는 Host 시작을 실패시킨다. 예외 진단을 먼저 준비하고 입력 포트를 열기 전에 등록을 완료한다. Workspace에는 queue·registry·원본 할당/회수 API를 제공하지 않는다.

## 저장 및 종료

`data/records.ndjson`은 single-writer append journal이다. Append 성공은 `Flush(true)` 완료 및 조회 가시성을 뜻한다. `data/views.json`은 사용자 View 정의이며 임시 파일 flush 후 rename으로 교체한다. 재시작 때 record를 복원하며 마지막의 LF 없는 불완전 record만 제거한다. 완성된 중간 record가 손상됐으면 시작을 실패시킨다. 저장 한도 초과는 기존 record를 지우지 않고 새 저장을 실패시킨다.

종료는 신규 RPC 접수 중단 → session 종료 → HTTP 중단 → Admin 주기 중단 → FIFO drain → 최종 Admin 저장 → 저장소 닫기 순서다. timeout은 대기 메시지를 폐기하고 실행 중 호출에 취소를 요청한다. 취소를 무시하는 Workspace는 실제 반환까지 기다리며 보유 중인 원본/저장소를 강제로 회수하지 않는다.

## 배포

```bash
bash scripts/publish.sh
dotnet artifacts/host/Dsn.Host.dll settings.example.json
```

배포물에는 테스트 Source가 포함되지 않는다. Docker는 .NET 10 SDK/ASP.NET 이미지를 사용한다. `settings.example.json`을 `settings.docker.json`으로 복사해 `bind`를 `0.0.0.0`, `dataDirectory`를 `/data`로 바꾸고 `users`에 토큰을 설정한다.

```bash
docker compose up --build -d
docker compose logs dsn
docker compose down
```

Compose는 RPC/View를 호스트 loopback에만 publish한다. 외부 Source는 호스트 7070으로 보낸다. 다른 장비의 View 접근은 TLS proxy로 연결한다. `dsn-data` volume에 record와 View 정의가 남는다. Docker 실행은 Termux 검증 범위 밖이다.

[구현 계약·설계 결정](docs/implementation/contracts.md) · [검증 결과](docs/implementation/verification.md) · [기존 설계](docs/arch/README.md)
