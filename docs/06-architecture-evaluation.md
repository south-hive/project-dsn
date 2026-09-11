# 6. Architecture Evaluation

## 평가 근거

2026-09-09 Termux Android arm64, .NET SDK 10.0.111 / Mono 환경에서 검증했다. 로컬 기능과 실패·수명 계약의 평가이며 정식 ATAM이나 운영 규모 부하 인수는 아니다. 실제 드라이버 연동·테스트는 수행하지 않았다.

| 실행 | 확인 결과 |
| --- | --- |
| `DSN_BUILD_JOBS=1 bash scripts/check.sh` | Release 경고 0·오류 0, 단위 17 + 통합 10개 그룹 통과 |
| `python tests/sdk/test_sources.py` | C++·Python 앱 SDK 6개 검사 통과; 외부 NuGet plugin 소비·실제 Host·재시작 |
| `python tests/pipeline.py` | 앱 입력 3건 → 결과 2건·원본 3건, SQLite BLOB 확인; 재시작·조건 변경 후 제외 원본 replay → 새 결과·revision, 원본 수 불변 |
| `dotnet publish … -r linux-arm64 --self-contained false` | Host·Workspace plugin·단일 native SQLite 포함, Mock/PDB 제외 확인; Linux 실행은 미검증 |
| `python tests/bench.py --browser` | 세 Python 앱 프로세스의 6건 보존·재시작, Chromium 표·Source 필터·차트·다운로드·replay·393px 배치 확인 |

브라우저 재처리 후 결과는 7건, 원본은 6건이다. 화면 산출물은 `artifacts/presenter-desktop.png`, `artifacts/presenter-mobile.png`이며 Git에서 제외한다. 재현 명령과 선택적 브라우저 준비는 [tests/README.md](../tests/README.md)를 따른다.

## Driver별 평가

| 대상 | 근거 | 한계 |
| --- | --- | --- |
| UC1 / C2·C3 | opaque TCP framing·Source SDK·독립 업무 Workspace | true는 큐 접수, 내구 저장 ACK나 exactly-once 아님 |
| UC2 / QA6 | 인증·Workspace scope·사용자 View·원본 전체 대상 ACL·replay 권한 | TLS 배치·입력 인증·침투 시험 미포함 |
| UC3 / QA7 | 제외 원본을 새 Filter 조건으로 replay, ID 유지·revision 변경 | 재처리는 append, 반복 요청은 별도 실행; 분석 규칙의 정확성 미평가 |
| UC4 / QA3 | Filter 순서·drop·scope/provenance 보호·설정 오류·Sink 실패 검사 | 임의 graph·window 집계·시간 격리·다중 Sink 원자성 없음 |
| UC5 / QA4 | SQLite BLOB·ID·View 재시작, 소유 lock, quota, legacy 이전 전체 rollback·반복 방지 | 전원 차단·파일시스템/디스크 장애 주입 미평가 |
| QA1·QA2 | 큐/메모리 포화·중복 Checkin·예외·종료 후 최종 참조 회수 | 장기 GC·운영 처리량/p99·손실률 미측정 |
| QA5 | Workspace 실패 격리·원본 quota 실패·Filter/Sink 실패 보고 | 느린 plugin/저장소는 공유 FIFO 지연, 비협력 작업 종료시간 상한 없음 |
| C6 | Contracts 단독 기능 의존·금지 참조 빌드 거부·DLL 로딩 | plugin은 신뢰 코드이며 프로세스 격리 없음 |

## Trade-off와 후속 평가

| 선택 | 이득 | 남은 과제 |
| --- | --- | --- |
| 로컬 SQLite | 외부 DB 없이 트랜잭션·BLOB·SQL 페이지 조회 | record별 트랜잭션 비용 측정 후 배치 writer 검토 |
| Filter 전에 원본 보존 | 실패·제외 후에도 재해석 | 큐 접수→원본 저장 사이 손실; ACK/outbox는 별도 설계 |
| 논리 quota | 보존 한도에서 명시적으로 거부 | 물리 DB/WAL 감시·기간별 보존·자동 회전 필요 |
| 동일 저장 식별자·View | 단일 PC 운영 유지, 이후 통합 기반 | 중앙 업로드·중복 제거·checkpoint·통합 cursor 미구현 |
| 순차 처리·같은 replay 큐 | 완료·소유권 추적 단순 | 실시간/replay 간 공정성·처리량 측정 필요 |

현재 범위는 독립 PC에서 Source → Workspace/Filter → Sink → Presenter를 실제 실행할 수 있는 단계다. 수천 PC·PC별 2~8 DUT의 운영 인수, 중앙 동기화, Docker 이미지 build/run·Linux CoreCLR 실행은 아직 완료한 것으로 보지 않는다. 다음 평가는 실제 앱 입력률·payload 크기·보존 기간을 정한 뒤 처리량/지연·디스크/RSS를 측정하고, 저장 장애와 재시작 복구를 주입하는 순서다.

## 단일 PC 우선 과제 (2026-09-10, 미구현 backlog)

| 순서 / ID | 항목 | 완료 기준 |
| --- | --- | --- |
| 1 / L1 | 물리 디스크·WAL·quota·큐 적체·거부/저장 실패 가시화와 보존 정책 | 상한 전 경고, 기간/용량별 명시적 회전·삭제 정책, 디스크 부족 시 앱 수집 상태 확인 |
| 2 / L2 | PC별 2~8 DUT 앱 부하·저장 장애 검증 | 실제 입력률/크기로 처리량·p99·RSS·손실 측정, 강제 종료/디스크 부족 후 복구; 필요 시 배치 writer |
| 3 / L3 | 전체 저장 이력 조회 | PC/DUT/Source/시험/기간 조건의 서버 측 검색·집계·다운샘플 차트, 현재 페이지 필터 한계 해소 |
| 4 / L4 | 로컬 운영 수명 | 자동 시작·실패 재시작·정상 종료 절차, 필요 데이터에 대한 SDK spool/ACK·재전송/중복 정책 결정 |
| 5 / O1 | 독립 snapshot DB 내보내기 | 수집 중 일관된 파일, BLOB/ID/View 보존, 무결성·실패/미완료 출력 처리, schema/원본 PC/생성 시점 식별 |
| 6 / O2 | 사무 PC 읽기 전용 Viewer | 기존 웹 재사용, 원본 DB 변경 없음, Ingress/plugin/Admin/replay 없음, 인터넷·시험 PC 연결 없이 동작 |
| 7 / O3 | 파일 선택·호환성·배포 | 새 snapshot 선택, 미지원 schema/손상 파일 안내, 대상 OS용 오프라인 실행 묶음; 여러 PC 통합은 후속 |

기본 원본 quota는 100,000건이라 지속 10건/초만 들어와도 약 2.8시간에 count 상한에 도달한다(byte 상한은 더 먼저 닿을 수 있음). 따라서 현재 기본 설정을 장기 무인 수집의 보존 정책으로 간주하지 않는다. 자동 삭제는 사용자 보존 요구를 정한 뒤 적용한다.

O1~O3는 네트워크 차단 환경에서 DB만 옮겨 같은 웹으로 보는 후속 옵션으로 등록했다. 설계는 [Top Level Design의 후속 배포 옵션](04-top-level-design.md#후속-배포-옵션-망-분리-환경의-사무-pc-열람-미구현)을 따른다. 이 항목들은 구현/검증 완료 내역과 구분한다.

## v0.1.0 미리보기 릴리즈 검증 (2026-09-10)

Release 빌드 경고/오류 0, C# 단위 17 + 통합 10개 그룹, SDK 6개, 파이프라인 조건 변경·재처리·재시작 검사를 다시 통과했다. Portable ZIP을 임시 디렉터리에 풀고 포함된 Python wheel을 오프라인 설치하여 세 앱 입력 30건 → 원본 30건/결과 8건, SQLite·처리 경로·웹 UI·정상 종료를 확인했다. 약 17MB ZIP은 .NET 런타임을 포함하지 않으며 ASP.NET Core Runtime 10 또는 SDK 10을 별도 설치해야 한다. Termux 외 실행과 조회 전용 Viewer는 이번 릴리즈의 검증/구현 범위 밖이다.

## 자동 앱 데모 추가 검증 (2026-09-10)

[자동 E2E 데모](../samples/e2e/README.md)는 DUT별 별도 Python 프로세스 → telemetry-demo Workspace → scale → SQLite → 전용 /demo View를 연결한다. 4 DUT 192건, 8 DUT 384건에서 원본/결과 대응, 네 구간 판정, API 빈도·단위 변환, Host 재시작 뒤 동일 식별자·값을 확인했다. Chromium의 자동 페이지 조회·DUT 카드·추세·갱신 정지/재개·393px 화면 배치도 통과했다. 이는 합성 앱 기반 기능 검증이며 운영 부하나 실제 불량 판정 정확성을 검증한 것은 아니다.

추가 단위 검사 포함 C# 18 + 통합 10개 그룹(경고/오류 0)을 통과했다. 데모 runner를 중단한 뒤 Host와 모든 DUT Source 프로세스가 종료되는 것도 확인했다.

## 격리 환경·오프라인 의존 검증 (2026-09-10)

초기 구성(커밋 `4640ae1`, 현재 환경 준비 방법은 [개발 환경 안내](development-environment.md) 참조)의 `make offline-check DSN_BUILD_JOBS=1`은 빈 의존성 cache에서 로컬 feed만 사용하여 Release build(경고/오류 0), C# 18+10개 그룹, SDK 6개, 4 DUT 192건 E2E 및 재시작 검증을 통과했다. 일반 외부 HTTP proxy는 연결 불가 주소로 지정했고 loopback 테스트만 우회했다. NuGet 5개와 Python 빌드 도구 3개의 원본 package·manifest·SHA-256을 약 20MB 반입 ZIP으로 만들었다. OS 수준 네트워크 격리 시험이나 SDK/OS 도구 자체의 오프라인 설치 검증은 아니다.

당시 SDK 정책은 10.0.1xx 계열 안의 patch 허용이었으며 실행 SDK는 Termux 10.0.111이다. 현재 정책은 [개발 환경 안내](development-environment.md)를 따른다. linux-arm64 framework-dependent publish와 해당 RID의 plugin·SQLite native 파일 포함을 확인했다. Docker 빌드는 온라인을 전제로 한다. Docker SDK 10.0.103 태그의 존재를 확인했지만 Docker 실행은 미검증이다. [환경 구성과 명령](development-environment.md).

## .NET 기본 개발 환경으로 정리 (2026-09-10)

기본 env/build/check/publish에서 Python venv 생성·활성화와 pip 설정을 제거했다. SDK 선택(global.json), NuGet lock, 프로젝트별 NuGet cache와 CLI 상태를 사용한다. Python SDK 패키징 도구는 선택 명령에서만 별도 디렉터리에 설치한다.

Python/pip 명령을 실패하도록 차단한 PATH로 `make doctor`와 `OFFLINE=1 make check DSN_BUILD_JOBS=1`을 실행하여 Release build(경고/오류 0), 단위 18 + 통합 10개 그룹을 통과했다. venv 없이 로컬 wheel 공급처에서 Python SDK 패키징도 통과했다. 시스템 Python으로 자동 데모 4 DUT 192건의 원본·결과·HTTP 조회 및 재시작 보존을 다시 확인했다.

## 호스트 개발 환경 호환성 수정 (2026-09-11)

SDK 정책을 `latestFeature`로 바꿔 .NET 10.0의 다른 feature band를 허용했다. `make env-check`에서 SDK 부재/실패 안내, 프로젝트 SDK 우선 선택, Python 부재·python3 단독·python fallback·명시적 실행 파일, venv 미생성을 확인했다. Termux SDK 10.0.111에서 doctor·locked restore, Python SDK wheel 생성과 의존성 ZIP 검증이 통과했다. 다른 SDK feature band의 실제 빌드를 검증한 것은 아니다. 기존 로컬 검증 환경에 남아 있던 venv 디렉터리는 제거했다.
