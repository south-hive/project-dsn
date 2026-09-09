# DSN

다양한 Source의 메시지를 수신하고 Workspace에서 해석·저장하여 사용자별 View로 제공하는 C#/.NET 10 서버다. Source는 테스트용 CLI만 포함하며, 실제 앱·driver/eBPF의 내부 발행 코드는 범위 밖이다.

[아키텍처 문서](docs/README.md): Project Overview → System Overview → Architectural Drivers → Top Level Design → Component Level Design → Architecture Evaluation.

## 실행과 검증

```bash
# Termux
pkg install dotnet-sdk-10.0
bash scripts/check.sh
# 터미널 1: RPC 7070 / HTTP 7071
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
bash scripts/publish.sh
dotnet artifacts/host/Dsn.Host.dll settings.example.json
```

## Docker

`settings.example.json`을 `settings.docker.json`으로 복사하고 `bind`를 `0.0.0.0`, `dataDirectory`를 `/data`로 바꾼다. `users`에는 사용자별 `token`(16자 이상)과 허용 `workspaces` 배열을 설정한다. HTTP 요청에는 `Authorization: Bearer <token>`을 보낸다.

```bash
docker compose up --build -d
docker compose logs dsn
docker compose down
```

Compose는 호스트 loopback에 포트를 공개하고 `dsn-data` volume에 record와 View 정의를 유지한다. 외부 HTTP 노출에는 별도 TLS proxy를 구성한다. [배포 구조](docs/04-top-level-design.md#deployment-view)

검증된 환경은 Termux SDK 10.0.111 / runtime 10.0.11이다. Docker 실행은 미검증이며, 구체적인 통과 범위와 한계는 [Architecture Evaluation](docs/06-architecture-evaluation.md)에 기록했다. 이전 TypeScript 실험은 `prototype/`에 보존한다.
