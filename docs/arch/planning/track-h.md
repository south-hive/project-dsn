# Track H: Host·Plugin Loading·Docker

[전체 순서 및 의존성](../10-implementation-plan.md) · [검증 기준](../11-verification.md)

## 입력 문서

- [08-sequences.md](../08-sequences.md)
- [09-deployment.md](../09-deployment.md)
- [07-types-and-interfaces.md](../07-types-and-interfaces.md)

## 작업 순서

선행 task가 모두 완료되면 착수한다. 같은 track에서도 의존 관계가 없으면 병렬 진행할 수 있다. 하위 작업은 해당 task 안의 구현 순서다.

| Task | 선행 task | 산출물 |
| --- | --- | --- |
| [H-01: Host lifecycle·plugin 로딩 규약](#h-01) | [F-01](track-f.md#f-01), [F-04](track-f.md#f-04) | Host 규약, plugin 발견 계약, 종료 모드 표 |
| [H-02: Host와 Plugin Loader 구현](#h-02) | [H-01](track-h.md#h-01), [R-05](track-r.md#r-05), [E-01](track-e.md#e-01), [P-02](track-p.md#p-02) | Host/Loader, lifecycle 테스트 |
| [H-03: Docker 이미지와 실행 구성](#h-03) | [H-02](track-h.md#h-02), [I-03](track-i.md#i-03), [P-03](track-p.md#p-03), [V-03](track-v.md#v-03), [R-06](track-r.md#r-06) | Docker 이미지, 실행 설정, 배포 가이드 |

<a id="h-01"></a>
## H-01. Host lifecycle·plugin 로딩 규약

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** F-01, F-04

**하위 작업**

- [ ] `H-01.1` 발견·호환성·필수 plugin·시작 실패 정책 정의
- [ ] `H-01.2` 시작 순서·registry 완료·입력 접수 시점 고정
- [ ] `H-01.3` drain/폐기·flush·협력 종료·timeout·미종료 동작 정의

**산출물:** Host 규약, plugin 발견 계약, 종료 모드 표

**완료 기준:** SEQ-01/02의 미정 지점을 명시하고 timeout만으로 실행 중 원본을 재사용하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**개발 계약 보완:** GAP-05·06: 설정 schema/기본값/우선순위/잘못된 값 처리와 plugin 의존성·조회/저장 취소·종료 순서를 명시한다. [점검 목록](../12-engineering-readiness.md)

**담당자 결정:** 설정·lifecycle·plugin 로딩 규약 초안 작성

**협의 조건:** 초기화·종료·설정 schema·필수 plugin·자원 정리 순서를 정할 때

**협의 대상·관련 경계:** 해당 자원 소유 I/R/E/P/V; plugin R/A, Source 영향 외부 Source 담당 — DC-09 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.

<a id="h-02"></a>
## H-02. Host와 Plugin Loader 구현

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** H-01, R-05, E-01, P-02

**하위 작업**

- [ ] `H-02.1` Host 조립과 plugin 발견·등록 구현
- [ ] `H-02.2` 초기화 rollback·입력 준비 상태 구현
- [ ] `H-02.3` 정상 종료·drain·미종료 호출 검증

**산출물:** Host/Loader, lifecycle 테스트

**완료 기준:** 오류 접수를 plugin보다 먼저 준비하고 부적합 plugin을 처리한다. 준비 전 수신하지 않고 종료 시 참조를 확인한다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**담당자 결정:** 정해진 조립·로딩·rollback 흐름의 내부 구현

**협의 조건:** 계약상 시작/정지 시점·plugin 호환성·활성 자원 종료를 바꿀 때

**협의 대상·관련 경계:** 해당 모듈 I/R/E/P/V 및 plugin 담당 — DC-09 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.

<a id="h-03"></a>
## H-03. Docker 이미지와 실행 구성

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** H-02, I-03, P-03, V-03, R-06

**하위 작업**

- [ ] `H-03.1` 이미지 build 및 plugin/저장/전송 설정 연결
- [ ] `H-03.2` host Source와 container의 실행·종료·재시작 검증
- [ ] `H-03.3` 배포 설정 및 원격 View 절차 기록

**산출물:** Docker 이미지, 실행 설정, 배포 가이드

**완료 기준:** 지원 호스트에서 문서로 DSN을 실행하고 Source 입력·View 조회를 연결한다. requirement는 배포물에 포함하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**개발 계약 보완:** GAP-10: 재현 가능한 image·plugin·저장/전송 설정 및 환경별 검증 명령을 제공한다. [점검 목록](../12-engineering-readiness.md)

**담당자 결정:** 산출물 규격을 유지하는 이미지 build 단계와 설정 예제

**협의 조건:** image 기반·공유 의존성·mount·권한·endpoint·설정 기본값을 바꿀 때

**협의 대상·관련 경계:** 해당 산출물 I/P/V/R/외부 Source 담당 담당과 X — DC-10, DC-09 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.
