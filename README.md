# DSN

다양한 Source의 메시지를 수신하고 Workspace에서 해석·저장하여 사용자별 View로 제공하는 C#/.NET 10 서버다. Source는 테스트용 CLI만 포함하며, 실제 앱·driver/eBPF의 내부 발행 코드는 범위 밖이다.

[아키텍처 문서](docs/README.md): Project Overview → System Overview → Architectural Drivers → Top Level Design → Component Level Design → Architecture Evaluation.

## 실행과 검증

```bash
# Termux
pkg install dotnet-sdk-10.0 make
make check
# 터미널 1: TCP 입력 7070 / HTTP 7071
dotnet run --project src/Dsn.Host -c Release --no-build
# 터미널 2
dotnet run --project tests/Dsn.TestSource -c Release --no-build -- 7070 hello 3
curl 'http://127.0.0.1:7071/view?workspaces=echo,hex&fields=id,workspace,payload_utf8,payload_hex'
```

설정 파일은 Host의 첫 인자로 전달한다. 기본값은 [settings.example.json](settings.example.json), API는 [System Overview](docs/02-system-overview.md)에 있다. 기본 바인딩은 loopback, 종료는 Ctrl+C/SIGTERM이다.

```bash
# 독립 Mock: 동일 RPC 입력의 envelope와 hex 출력
dotnet run --project src/Dsn.Mock -c Release --no-build -- 7072
# 배포용 DLL 생성
make publish
dotnet artifacts/host/Dsn.Host.dll settings.example.json
```

## Docker

Docker Engine과 Compose v2가 있는 Linux에서 실행한다. 이미지는 Host와 예제 plugin만 포함하며, Mock·SDK·소스·디버그 심볼은 제외한다. 런타임은 shell/패키지 관리자가 없는 [ASP.NET Chiseled Extra](https://github.com/dotnet/dotnet-docker/blob/main/documentation/image-variants.md)를 사용하여 ICU·시간대 지원을 유지한다. 동적 plugin 로딩을 위해 trimming/AOT는 사용하지 않는다.

```bash
make deploy       # 최초 설정 생성 → 이미지 빌드 → 백그라운드 실행
make ps
make logs
make down         # 데이터 volume 유지
```

`make docker-config`는 `settings.docker.json`에 `bind=0.0.0.0`, `dataDirectory=/data`, 임의의 64자리 토큰을 가진 `operator` 사용자(echo/hex 권한)를 생성한다. 기존 파일은 보존한다. 필요하면 배포 전에 이 명령을 먼저 실행하고 설정을 편집한다. HTTP 요청에는 파일에 저장된 토큰을 `Authorization: Bearer <token>`으로 보낸다. 설정 파일은 컨테이너 사용자가 읽을 수 있어야 하며 Git과 이미지 빌드 컨텍스트에서 제외된다.

Compose는 호스트 loopback에 7070/7071만 공개하고 `dsn-data` volume에 record와 View 정의를 유지한다. 외부 HTTP 노출에는 별도 TLS proxy를 구성한다. [배포 구조](docs/04-top-level-design.md#deployment-view)

## Build / CI / CD

로컬과 CI에서 같은 [Makefile](Makefile) 명령을 사용한다. 검증·publish는 기존 `scripts/check.sh`, `scripts/publish.sh`를 재사용하며, 이 스크립트를 직접 실행해도 된다.

| 명령 | 동작 |
| --- | --- |
| `make` | 전체 명령 목록 |
| `make build` | Release 빌드 |
| `make check` | 빌드 + 전체 테스트 (`make test`도 동일) |
| `make publish` | `artifacts/host`, `artifacts/mock`에 배포 파일 생성 |
| `make docker-build` | Host Docker 이미지 빌드 |
| `make ci` | 테스트 성공 후 Docker 이미지 빌드 |
| `make deploy` | 최초 설정 생성 + 이미지 빌드 + Compose 실행 |
| `make down` | Compose 종료, 데이터 volume 보존 |

```bash
make ci IMAGE=ghcr.io/OWNER/dsn:COMMIT        # 테스트 성공 후 이미지 빌드
make docker-push IMAGE=ghcr.io/OWNER/dsn:COMMIT  # registry 로그인 후 게시
# 배포 서버: 게시한 동일 이미지 실행
make deploy-image IMAGE=ghcr.io/OWNER/dsn:COMMIT
```

CI는 .NET 10 SDK·Make·Bash·Docker, 배포 서버는 Make·Bash·Docker Compose v2가 필요하다. `IMAGE` 기본값은 `dsn:local`이다. `deploy`는 소스에서 빌드하고, `deploy-image`는 지정한 이미지를 가져온다. 두 명령 모두 현재 Docker context에 컨테이너를 재생성하여 설정 변경도 적용한다. 자동 배포 트리거나 서버 접속 설정은 포함하지 않는다.

검증된 환경은 Termux SDK 10.0.111 / runtime 10.0.11이다. Docker 실행은 미검증이며, 구체적인 통과 범위와 한계는 [Architecture Evaluation](docs/06-architecture-evaluation.md)에 기록했다. 이전 TypeScript 실험은 `prototype/`에 보존한다.

솔루션은 `DSN.sln`이다. 기능 프로젝트는 `Dsn.Contracts`만 참조하며 빌드에서 의존 경계를 검사한다. 예제 plugin은 `samples/Dsn.Workspaces.Examples`, 단위·통합 테스트는 각각 `tests/Dsn.UnitTests`, `tests/Dsn.IntegrationTests`에 있다.
