# 6. Architecture Evaluation

## 평가 방법과 근거

[Architectural Drivers](03-architectural-drivers.md)의 시나리오와 구현 경계를 대조하고, 기존 기능 테스트·실제 프로세스 실행 결과로 확인 가능한 범위를 판단했다. 정식 ATAM 워크숍이나 운영 부하 평가는 수행하지 않았다.

이번 구조 변경의 실행 근거는 **2026-09-09** Termux Android arm64, .NET SDK 10.0.111, .NET/ASP.NET Core 10.0.11, `linux-bionic-arm64` Mono 환경이다. 12개 프로젝트를 Release로 다시 빌드하고, 기존 동작과 새 의존·호출 수명 경계를 검증했다.

- Release build: 경고 0, 오류 0. [단위 테스트](../tests/Dsn.UnitTests/Program.cs) 11개 + [통합 테스트](../tests/Dsn.IntegrationTests/Program.cs) 7개, 총 18개 그룹 통과.
- publish 산출물 검사: 제거된 Core assembly 없음, 예제는 plugins 디렉터리로 배치, Mock은 Ingress/Contracts만 포함.
- publish된 Host + 테스트 Source: notification 5개 → echo/hex record 10개, 실제 HTTP 값·journal 확인, SIGTERM exit 0.
- publish된 독립 Mock + 테스트 Source: 정확한 payload hex, SIGTERM exit 0.
- Docker 이미지 build/run과 Linux CoreCLR 실행은 미검증.

재현 명령은 저장소 루트에서 `bash scripts/check.sh`, `bash scripts/publish.sh`다. 후자는 산출물 생성이며 프로세스 smoke test나 Docker 인수를 자동 수행하지 않는다.

## Driver별 평가

| 대상 | 근거·관찰 | 판정과 한계 |
| --- | --- | --- |
| UC1/4, C2/3 | decoder·실제 TCP split/coalesced·Mock/CLI | 기능 확인. production Source 비대기 성능·전송 전 손실은 검증 안 함 |
| QA1 / 성능·자원 | queue/memory 포화 시 신규 거부, FIFO 기능 테스트 | 한도 동작만 확인. p99 지연·처리량·CPU·RSS·손실률 미평가 |
| QA2 / 메모리 | 동시 중복 Checkin, 별도 context 동시 읽기, 지연/실패 호출 완료 후 정리, 저장 전 반납 | 시험 사례에서 단일 반환과 최종 참조 0. 장기 부하/GC·pool 동작 일반화 불가 |
| QA3 / 격리 | Source별 연결 종료, 다른 Source 수신, 재접속 | TCP session 격리 확인. 공유 실행부의 plugin 지연 격리는 제공 안 함 |
| QA4 / 복구 | flush/reopen·부분 tail 복구·id·값·export | 정상 재시작과 구성된 파일 사례 확인. 전원 손실·디스크 장애 주입 미평가 |
| QA5 / 종료 | active 원본 유지, 대기분 회수, 늦은 완료, 포트 충돌 rollback | 자원 수명 기능 확인. 비협력 호출의 종료시간 상한 없음 |
| QA6 / 접근 | HTTP 401/403/400, 사용자 정의 분리, 재시작 복구 | 기능 확인. 침투 시험·TLS 배치·RPC 신원 검증은 범위 밖 |
| QA7 / 변경 | 기능 assembly의 Contracts 단독 의존, Runtime 내부 원본 비공개, 금지된 프로젝트 참조 빌드 거부, 예제 DLL 로딩 | 프로젝트 경계와 예제 plugin 경로 확인. 부적합 API 버전/복잡한 의존 DLL 조합은 검증 확대 필요 |
| QA8 / 관측 | 본문 동일성·count·한도·snapshot 투영 | 기본 집계 확인. 주기 전송의 동시성/부분 실패·장기간 quota 영향 미평가 |
| C1 / 배포 | Termux build/publish/별도 프로세스 실행 | C# 실행 확인. Docker 구성의 실행 인수는 남음 |

## 민감 지점과 Trade-off

| 지점 | 설계상의 이득 | 민감도·잔여 위험 | 다음 평가 |
| --- | --- | --- | --- |
| 순차 executor + 동기 flush | FIFO와 실패·수명 추적 단순 | plugin/저장 지연이 전체 backlog와 root 보유 시간을 늘림 | 입력률·payload·flush 지연을 바꿔 queue/지연/손실 측정 |
| journal + 메모리 index | 추가 DB 없이 단순 복구 | quota 이후 쓰기 거부, 재시작 비용·RSS 증가 | 데이터 크기별 복구 시간/RSS, 저장 장애 주입 |
| 협력 종료 | active 메모리 조기 회수 방지 | 비협력 plugin은 종료 지연, 강제 프로세스 종료 시 미저장 손실 | 비협력 작업 시 운영 종료 절차 결정 |
| 오류 snapshot append | 접수와 저장 분리, 누적 이력 | snapshot 중간 실패 시 재시도로 일부 중복 가능; revision과 snapshot의 동시성, dropped 가시성 제한 | 부분 저장 실패/동시 Report 주입 후 count·revision 비교 |
| 신뢰 경계와 Host 집중 | 단순 plugin/HTTP 조립 | 신뢰하지 않는 plugin 실행 위험, 인증 정책과 배포 신뢰 경계는 Host에 집중 | 격리 수준과 외부 인증 요구를 별도 검토 |

## 결론

프로젝트 분리는 수명·순서 계약의 범용 병렬화를 의미하지 않는다. 순차 실행만 지원하며, 공유 원본을 서로 다른 context로 동시에 읽는 테스트는 병렬 Runtime의 인수가 아니다.

현재 구조는 수신 → 해석 → 영속 저장 → 사용자별 조회의 기본 기능과 주요 수명·실패 계약을 시험 환경에서 충족한다. 성능·장기 운영 안정성·Linux 배포까지 검증된 상태는 아니다. 다음 평가는 Docker 실행/volume 복구, 저장 장애·snapshot 부분 실패, 부하 시 순차 처리 비용 순으로 수행하며 측정 전에 목표 workload와 허용 기준을 정한다.
