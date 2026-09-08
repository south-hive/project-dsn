# Track F: 공통 계약과 기준

[전체 순서 및 의존성](../10-implementation-plan.md) · [검증 기준](../11-verification.md)

## 입력 문서

- [06-structural-views.md](../06-structural-views.md)
- [07-types-and-interfaces.md](../07-types-and-interfaces.md)
- [05-decisions.md](../05-decisions.md)

## 작업 순서

선행 task가 모두 완료되면 착수한다. 같은 track에서도 의존 관계가 없으면 병렬 진행할 수 있다. 하위 작업은 해당 task 안의 구현 순서다.

| Task | 선행 task | 산출물 |
| --- | --- | --- |
| [F-01: DSN 환경과 프로젝트 경계 고정](#f-01) | 없음 | DSN 환경 matrix, 프로젝트 배치안, build 절차 |
| [F-02: RPC와 Envelope v1 계약 정의](#f-02) | [F-01](track-f.md#f-01) | RPC 계약/IDL 또는 동등 규격, envelope v1, 공통 fixture |
| [F-03: Source 전달용 RPC 인터페이스 패키지 정의](#f-03) | [F-02](track-f.md#f-02) | Source 연동 규격, RPC 인터페이스/IDL, fixture 및 호출 예시 |
| [F-04: DSN 실행·메모리·플러그인 API 고정](#f-04) | [F-01](track-f.md#f-01) | 계약 프로젝트, 소유권 표, 대역 Workspace, 기본 오류 모델 |
| [F-05: Record·조회·export·View 경계 정의](#f-05) | [F-01](track-f.md#f-01) | Record/저장/조회/export 규격, 예제 record·query fixture |
| [F-06: Quality Attribute·위험·관측 아이디어 정리](#f-06) | [F-01](track-f.md#f-01) | QA·위험 등록부, 관측 아이디어, 단계 구분 |

<a id="f-01"></a>
## F-01. DSN 환경과 프로젝트 경계 고정

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** 없음

**하위 작업**

- [ ] `F-01.1` DSN host·CPU·Docker 및 RPC 검증 환경 기록
- [ ] `F-01.2` .NET과 DSN 공통 프로젝트·build 경계 선택
- [ ] `F-01.3` DSN build/test 명령과 버전 고정 목록 작성

**산출물:** DSN 환경 matrix, 프로젝트 배치안, build 절차

**완료 기준:** DSN과 Mock을 빌드할 수 있다. Source native/kernel toolchain 선정·구현은 외부 담당 범위다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.


**담당자 결정:** 확정된 RPC·모듈 계약 안의 구현 및 문서·검증 구성

**협의 조건:** RPC 입력/응답 의미·Source 식별·소유권·DSN 배포 또는 공개 계약을 새로 정하거나 변경할 때

**협의 대상·관련 경계:** I·H·해당 DSN 계약 소비자; RPC 외부 호환성은 Source 담당자 — DC-02, DC-10 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.

<a id="f-02"></a>
## F-02. RPC와 Envelope v1 계약 정의

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** F-01

**하위 작업**

- [ ] `F-02.1` RPC 방식·서비스/메서드·요청 schema 및 envelope version 표현 정의
- [ ] `F-02.2` 최대 요청 크기·호환성·RPC session/source_id 매핑·Docker endpoint 정의
- [ ] `F-02.3` 정상·잘못된 요청 fixture와 RPC 응답/상태의 의미 정의

**산출물:** RPC 계약/IDL 또는 동등 규격, envelope v1, 공통 fixture

**완료 기준:** Source와 DSN/Mock이 동일 RPC 계약을 사용한다. RPC 방식은 선택 전이며 업무 ACK/NACK과 재전송 보장을 추가하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.


**담당자 결정:** 확정된 RPC·모듈 계약 안의 구현 및 문서·검증 구성

**협의 조건:** RPC 입력/응답 의미·Source 식별·소유권·DSN 배포 또는 공개 계약을 새로 정하거나 변경할 때

**협의 대상·관련 경계:** I·H·해당 DSN 계약 소비자; RPC 외부 호환성은 Source 담당자 — DC-02, DC-10 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.

<a id="f-03"></a>
## F-03. Source 전달용 RPC 인터페이스 패키지 정의

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** F-02

**하위 작업**

- [ ] `F-03.1` RPC 호출 계약과 envelope/payload 경계를 전달 문서로 정리
- [ ] `F-03.2` 발행 비대기·실패 비전파·손실 허용의 연동 요구 명시
- [ ] `F-03.3` Mock endpoint·정상/오류 fixture·호출 예시 및 책임 경계 제공

**산출물:** Source 연동 규격, RPC 인터페이스/IDL, fixture 및 호출 예시

**완료 기준:** 각 Source 담당자가 내부 구현을 선택할 수 있는 RPC 계약을 제공한다. 탄창·ABI·thread·kernel/eBPF·중계 구현을 DSN에서 지정하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.


**담당자 결정:** 확정된 RPC·모듈 계약 안의 구현 및 문서·검증 구성

**협의 조건:** RPC 입력/응답 의미·Source 식별·소유권·DSN 배포 또는 공개 계약을 새로 정하거나 변경할 때

**협의 대상·관련 경계:** I·H·해당 DSN 계약 소비자; RPC 외부 호환성은 Source 담당자 — DC-02, DC-10 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.

<a id="f-04"></a>
## F-04. DSN 실행·메모리·플러그인 API 고정

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** F-01

**하위 작업**

- [ ] `F-04.1` 공개 Workspace 계약과 내부 Lifetime·Queue·Executor 계약 분리
- [ ] `F-04.2` Checkout·Checkin·root 인계·종료 정리 및 기본 ErrorEvent 모델 정의
- [ ] `F-04.3` 대상 배열 순서·미등록/중복 대상·처리 실패 후 다음 대상 동작 정의

**산출물:** 계약 프로젝트, 소유권 표, 대역 Workspace, 기본 오류 모델

**완료 기준:** 플러그인은 공개 계약만으로 빌드하며 count 0·중복 반납·실패 경로 및 큐 인계의 기대 결과가 명확하다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**개발 계약 보완:** GAP-04·05·09: Process 반환·유효하지 않은 lease·대상 실패 규칙, Queue/Lifetime 초기 한도와 ErrorEvent 필드를 명시한다. [점검 목록](../12-engineering-readiness.md)

**담당자 결정:** 공통 타입·반납 ledger·API 예제 초안 작성

**협의 조건:** 공개 계약·root/lease 인계·Process·대상 실패·오류 모델을 정할 때

**협의 대상·관련 경계:** R의 해당 모듈 담당과 I; 오류 E, lifecycle H, plugin 소비자 A — DC-04, DC-05, DC-08 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.

<a id="f-05"></a>
## F-05. Record·조회·export·View 경계 정의

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** F-01

**하위 작업**

- [ ] `F-05.1` record 소유 데이터·Workspace별 field 식별과 타입 정의
- [ ] `F-05.2` Append 반환·실패·Query pagination/일관성·Export 부분 실패 정의
- [ ] `F-05.3` View 선택·조합·결합 키·사용자 구분·정의 보존 범위 결정

**산출물:** Record/저장/조회/export 규격, 예제 record·query fixture

**완료 기준:** 서로 다른 Workspace record로 사용자별 View 기대 결과를 기술하고 Workspace 직접 조회가 필요하지 않다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**개발 계약 보완:** GAP-06·07·08: Append 접수/영속·중복 쓰기·조회 가시성·취소, field 타입/null/schema와 View field 목록 계약을 명시한다. [점검 목록](../12-engineering-readiness.md)

**담당자 결정:** record/query/fixture의 후보 작성

**협의 조건:** field·Append 반환·query 가시성·취소·export·View 조합 의미를 정할 때

**협의 대상·관련 경계:** P와 해당 소비자 R·A·V; 종료 영향 시 H — DC-06, DC-07 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.

<a id="f-06"></a>
## F-06. Quality Attribute·위험·관측 아이디어 정리

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** F-01

**하위 작업**

- [ ] `F-06.1` 기존 설계의 Quality Attribute와 위험 시나리오 정리
- [ ] `F-06.2` 관측 가능한 위치·지표 후보와 관측 한계 기록
- [ ] `F-06.3` 1차 기능 검증과 후속 monitoring·평가·조치의 범위 구분

**산출물:** QA·위험 등록부, 관측 아이디어, 단계 구분

**완료 기준:** 위험을 구현 결함과 구분하여 미평가 상태로 기록한다. 수치 목표·도구·수집 주기·상세 구현은 확정하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**담당자 결정:** QA·위험·관측 후보를 미평가 초안으로 기록

**협의 조건:** 다른 모듈의 기대 응답·관측 범위 또는 단계 구분을 기술할 때

**협의 대상·관련 경계:** 해당 위험의 모듈 담당과 E·X — DC-11 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.
