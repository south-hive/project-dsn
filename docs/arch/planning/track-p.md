# Track P: Persistence

[전체 순서 및 의존성](../10-implementation-plan.md) · [검증 기준](../11-verification.md)

## 입력 문서

- [07-types-and-interfaces.md](../07-types-and-interfaces.md)
- [09-deployment.md](../09-deployment.md)
- [11-verification.md](../11-verification.md)

## 작업 순서

선행 task가 모두 완료되면 착수한다. 같은 track에서도 의존 관계가 없으면 병렬 진행할 수 있다. 하위 작업은 해당 task 안의 구현 순서다.

| Task | 선행 task | 산출물 |
| --- | --- | --- |
| [P-01: Record 계약 및 저장·조회 대역](#p-01) | [F-05](track-f.md#f-05) | Record contracts, 대역, 공통 검증 |
| [P-02: 실제 Persistence Adapter](#p-02) | [P-01](track-p.md#p-01) | 저장소 선택 기록, adapter, 영속성 검증 |
| [P-03: Export와 저장 배포 설정](#p-03) | [P-02](track-p.md#p-02) | export, 저장 배포 가이드 |

<a id="p-01"></a>
## P-01. Record 계약 및 저장·조회 대역

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** F-05

**하위 작업**

- [ ] `P-01.1` Record/Query/Export 계약 프로젝트 구현
- [ ] `P-01.2` 영속 보장이 없는 테스트 대역 작성
- [ ] `P-01.3` 공통 fixture로 adapter 검증 suite 작성

**산출물:** Record contracts, 대역, 공통 검증

**완료 기준:** DB 없이 Workspace와 View를 검증하며 대역 성공을 영속 저장 성공으로 간주하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**담당자 결정:** 계약을 구현하는 대역 저장소와 검증 suite 구성

**협의 조건:** 공통 타입·Append/Query/Export 의미·fixture 결과를 바꿀 때

**협의 대상·관련 경계:** R·A·V 중 해당 소비자 — DC-06 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.

<a id="p-02"></a>
## P-02. 실제 Persistence Adapter

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** P-01

**하위 작업**

- [ ] `P-02.1` 저장소 선택과 record mapping 정의
- [ ] `P-02.2` Append·Query·flush/종료 구현
- [ ] `P-02.3` 재시작·실패·원본 반납 이후 데이터 검증

**산출물:** 저장소 선택 기록, adapter, 영속성 검증

**완료 기준:** 선택 저장 보장을 재시작 테스트로 확인하고 Query가 Workspace/lease에 접근하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**개발 계약 보완:** GAP-07: 선택 저장소에서 중복 쓰기·조회 가능 시점·재시작 보장·취소 동작을 검증한다. [점검 목록](../12-engineering-readiness.md)

**담당자 결정:** 정해진 보장을 유지하는 mapping·index·query 코드

**협의 조건:** 초기 저장소·배포 의존성·저장 보장·조회 가시성·취소를 정하거나 바꿀 때

**협의 대상·관련 경계:** H와 해당 계약 소비자 R·A·V — DC-06, DC-09, DC-10 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.

<a id="p-03"></a>
## P-03. Export와 저장 배포 설정

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** P-02

**하위 작업**

- [ ] `P-03.1` 정해진 범위·형식 export 구현
- [ ] `P-03.2` 부분 실패·취소·조회 부하 검증
- [ ] `P-03.3` volume/저장 서비스 설정과 데이터 수명 기록

**산출물:** export, 저장 배포 가이드

**완료 기준:** fixture field·값을 export하고 container 재시작 시 유지/소멸 데이터를 명시한다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**담당자 결정:** 규격 내 export 구현·오류 테스트·가이드 구성

**협의 조건:** export 범위/형식·부분 실패·저장 배치·데이터 수명을 바꿀 때

**협의 대상·관련 경계:** export 소비자 및 H; query 의미 영향 시 V — DC-06, DC-10 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.
