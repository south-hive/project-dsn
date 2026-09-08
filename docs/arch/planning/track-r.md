# Track R: 메시지 수명·Queue·Routing·Execution

[전체 순서 및 의존성](../10-implementation-plan.md) · [검증 기준](../11-verification.md)

## 입력 문서

- [02-contracts.md](../02-contracts.md)
- [07-types-and-interfaces.md](../07-types-and-interfaces.md)
- [08-sequences.md](../08-sequences.md)

## 작업 순서

선행 task가 모두 완료되면 착수한다. 같은 track에서도 의존 관계가 없으면 병렬 진행할 수 있다. 하위 작업은 해당 task 안의 구현 순서다.

| Task | 선행 task | 산출물 |
| --- | --- | --- |
| [R-01: Message Lifetime과 Checkout·Checkin 구현](#r-01) | [F-04](track-f.md#f-04) | Lifetime 모듈, ref count·수명 검증 |
| [R-02: Signal Buffer와 no_policy 구현](#r-02) | [R-01](track-r.md#r-01) | Buffer 모듈, FIFO·폐기 테스트 |
| [R-03: Registry와 Bulletin Board 구현](#r-03) | [F-04](track-f.md#f-04) | Routing 모듈, registry 검증 |
| [R-04: Workspace SDK와 예제 플러그인](#r-04) | [F-04](track-f.md#f-04), [P-01](track-p.md#p-01) | Workspace 예제, SDK 가이드, 검증용 plugins |
| [R-05: 단일 FIFO Executor 구현](#r-05) | [R-01](track-r.md#r-01), [R-02](track-r.md#r-02), [R-03](track-r.md#r-03) | Sequential Executor, 대역 검증 |
| [R-06: 다중 Workspace 수명 통합](#r-06) | [R-04](track-r.md#r-04), [R-05](track-r.md#r-05) | Runtime 통합 fixture, 수명 결과 |

<a id="r-01"></a>
## R-01. Message Lifetime과 Checkout·Checkin 구현

**상태:** 미착수

**선행 조건:** F-04

**하위 작업**

- [ ] `R-01.1` 원본·root 인계·lease 상태 구현
- [ ] `R-01.2` count 연산·마지막 회수·중복 감소 방지
- [ ] `R-01.3` 호출 scope의 제한된 접근과 종료 정리 구현

**산출물:** Lifetime 모듈, ref count·수명 검증

**완료 기준:** 소비자가 동일 원본을 공유하고 마지막 반납에서 한 번만 회수한다. 실패·인계 경로에 누수·조기 회수가 없다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="r-02"></a>
## R-02. Signal Buffer와 no_policy 구현

**상태:** 미착수

**선행 조건:** R-01

**하위 작업**

- [ ] `R-02.1` 용량과 소유권 인계 API 구현
- [ ] `R-02.2` FIFO와 정책 교체 경계 구현
- [ ] `R-02.3` 빈 큐·포화·writer 종료·미실행 폐기 검증

**산출물:** Buffer 모듈, FIFO·폐기 테스트

**완료 기준:** 성공/실패 소유자를 구분하고 FIFO를 유지한다. timestamp/event_type으로 암묵적으로 재정렬하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="r-03"></a>
## R-03. Registry와 Bulletin Board 구현

**상태:** 미착수

**선행 조건:** F-04

**하위 작업**

- [ ] `R-03.1` 단일 이름 등록과 registry 완료 상태 구현
- [ ] `R-03.2` 이름 기반 대상 조회·중복 등록 오류 구현
- [ ] `R-03.3` 미등록·중복 목적지 fixture 검증

**산출물:** Routing 모듈, registry 검증

**완료 기준:** 후발 중복 등록이 거부되고 공개 plugin API로 registry나 큐를 변경할 수 없다. payload는 라우팅에 사용하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="r-04"></a>
## R-04. Workspace SDK와 예제 플러그인

**상태:** 미착수

**선행 조건:** F-04, P-01

**하위 작업**

- [ ] `R-04.1` 공개 계약만 참조하는 예제 작성
- [ ] `R-04.2` Checkout·파싱·복사·Checkin·저장 순서 구현
- [ ] `R-04.3` 성공·파싱 실패·미반납 대역과 build 예제 제공

**산출물:** Workspace 예제, SDK 가이드, 검증용 plugins

**완료 기준:** 내부 구현 참조 없이 빌드하고 record가 반납한 원본에 의존하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="r-05"></a>
## R-05. 단일 FIFO Executor 구현

**상태:** 미착수

**선행 조건:** R-01, R-02, R-03

**하위 작업**

- [ ] `R-05.1` 큐 root 소비·대상 순차 호출 구현
- [ ] `R-05.2` context 생성·종료 정리·실패 계약 구현
- [ ] `R-05.3` 정지·drain을 위한 내부 실행 계약 제공

**산출물:** Sequential Executor, 대역 검증

**완료 기준:** 동시에 두 Workspace를 호출하지 않고 마지막 대상까지 root를 유지한다. 정상/실패 정리가 count를 중복 감소시키지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="r-06"></a>
## R-06. 다중 Workspace 수명 통합

**상태:** 미착수

**선행 조건:** R-04, R-05

**하위 작업**

- [ ] `R-06.1` 두 Workspace의 서로 다른 record 결과 검증
- [ ] `R-06.2` 느린 소비자·파싱/저장 실패 대역 실행
- [ ] `R-06.3` Workspace 반납과 root 회수 시점 계측

**산출물:** Runtime 통합 fixture, 수명 결과

**완료 기준:** 원본 하나와 독립 record를 확인한다. 느린 저장의 root 보유 영향 및 FIFO 범위를 기록한다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.
