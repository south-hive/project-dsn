# C# 검증 기록

검증일: 2026-09-08. 환경: Android Termux arm64, .NET SDK 10.0.111, MSBuild 18.0.11, .NET/ASP.NET Core 10.0.11, RID linux-bionic-arm64 (Mono).

## 재현

```bash
bash scripts/check.sh
bash scripts/publish.sh
```

`check.sh`는 7개 프로젝트의 Release 빌드와 `tests/Dsn.Tests` 실행을 수행한다. 테스트는 임시 디렉터리 및 동적 할당 loopback 포트를 사용한다. test runner는 실패 시 exit 1. 외부 NuGet 테스트 패키지나 실제 Source가 필요하지 않다.

## 실제 결과

- `bash scripts/check.sh`: Release build 성공, 경고 0 / 오류 0. **13개 검증 그룹 통과, 실패 0**.
- 첫 실행에서 플러그인 파일 누락 예외 유형이 런타임에 따라 달라지는 문제를 발견했다. PluginLoader에서 파일 존재를 먼저 검증하도록 수정한 뒤 전체 suite가 통과했다.
- `bash scripts/publish.sh`: Host/Mock framework-dependent publish 성공. `artifacts/host`, `artifacts/mock` 생성.
- publish된 Host와 실제 TestSource CLI를 별도 프로세스로 실행: 5 notifications → echo/hex 10 records, HTTP 값 확인, SIGTERM exit 0 및 journal 10행 확인.
- publish된 독립 Mock과 실제 TestSource CLI 실행: `mock-smoke`의 정확한 hex echo 확인, SIGTERM exit 0.
- `git diff --check`: 통과.

## 검증 대상

| 범위 | 시험 |
| --- | --- |
| Ingress | version 선택, canonical base64/opaque bytes/빈 payload, notification 형식 및 달력 검증 |
| 소유권 | concurrent 중복 Checkin, use-after-checkin, 미반납 lease 자동 정리, 참조 ledger 0 |
| 실행 | FIFO 20건, 중복 목적지 한 번, unknown/실패 후 다음 대상 계속, registry 종료 |
| 포화/종료 | queue/memory 초과 폐기, active 호출 유지, queued root 회수, 늦은 완료 후 최종 회수 |
| 저장 | 실제 flush/reopen, 부분 마지막 frame 복구, scalar 복사, quota 거부, export 및 페이지 |
| 저장 대역 | Memory/Journal 공통 결과, 느린 저장 대기 전 Workspace Checkin |
| 오류/Admin | 전체 본문 동일성, first/last/count, 집계 용량 및 변경 snapshot 투영 |
| TCP | split/coalesced 수신, invalid version 후 지속, 중복 Source, 변경 Source, 해당 Source만 disconnect, 재접속 |
| RPC 경계 | 잘못된 RPC, frame 초과, truncated FIN, session cap |
| Plugin | 실제 DLL discovery/처리, 필수 DLL 없음 오류 |
| HTTP | 실제 Kestrel 요청, 401/403/400, scope별 field/export, 사용자별 정의 분리, 다중 Workspace field 투영 |
| 재시작/시작 실패 | record+View 정의 복구, 포트 충돌 rollback 후 storage 재사용 |

## 설계 계획과 인수 상태

| Track | C# 산출물 / 범위 |
| --- | --- |
| F | .NET solution, Contracts, 구현 RPC/Record/View/설정 결정 및 fixture |
| I | Protocol/Version1Decoder/RpcServer 및 독립 Mock |
| R | Lifetime/MessageContext, NoPolicy, SequentialExecutor, registry와 Workspace SDK |
| E | ErrorSink 기본 집계 및 Source 연결 종료 경계. E-02 후속 monitoring 보류 |
| A | 독립 집계 snapshot의 Admin record 투영 |
| P | MemoryRecordStore 대역, JournalStore 영속 adapter, query/export/재시작 |
| V | ViewService, 사용자별 ViewDefinitions, 인증·Workspace scope가 있는 HTTP API |
| H | Host/PluginLoader, lifecycle 및 publish/Docker/Compose 파일. 컨테이너 실행 미검증 |
| X | Termux 실제 TCP/HTTP/파일 기능 통합. Docker G4 및 최종 배포 인수 G5는 Linux 환경 검증이 남음 |

기존 planning/task-index.json의 최초 계획을 일괄 완료 처리하지 않는다. 특히 H-03/X-03/X-05의 Docker 배포 인수 기준은 아직 충족했다고 주장하지 않는다. E-02/X-04의 운영 monitoring·부하 기반 QA는 원래 후속 범위다.

## 남은 검증 범위

- Docker daemon이 없는 기기이므로 이미지 build/run, container volume 재시작, Linux CoreCLR 실행은 미검증.
- 실제 외부 C++ 앱·driver/eBPF는 테스트 Source로 대체. native Source SDK는 만들지 않았다.
- 전원 손실/파일시스템 장애 주입, 장시간 부하와 RSS·latency 목표, 임의 plugin의 비협력 동작에 대한 운영 대응은 미평가.
- View 조합은 column 투영이며 cross-record join/UI는 구현 범위로 추가하지 않았다. 저장 정의/원격 API는 구현했다.
- journal 및 Admin snapshot 이력은 유한 quota에 도달하면 새 기록을 거부한다. 보존/회전 정책은 후속 운영 결정이다.
