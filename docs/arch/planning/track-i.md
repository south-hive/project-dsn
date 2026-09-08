# Track I: Ingress와 DSN Mock

[전체 순서 및 의존성](../10-implementation-plan.md) · [검증 기준](../11-verification.md)

## 입력 문서

- [06-structural-views.md](../06-structural-views.md)
- [07-types-and-interfaces.md](../07-types-and-interfaces.md)
- [03-source-and-mock.md](../03-source-and-mock.md)

## 작업 순서

선행 task가 모두 완료되면 착수한다. 같은 track에서도 의존 관계가 없으면 병렬 진행할 수 있다. 하위 작업은 해당 task 안의 구현 순서다.

| Task | 선행 task | 산출물 |
| --- | --- | --- |
| [I-01: RPC Server Adapter 구현](#i-01) | [F-02](track-f.md#f-02) | RPC Server Adapter, 요청·session 테스트 |
| [I-02: Version Selector와 Decoder 구현](#i-02) | [F-02](track-f.md#f-02) | 공유 Protocol/Decoder, fixture 결과 |
| [I-03: Ingress → Lifetime → Queue 인계](#i-03) | [I-01](track-i.md#i-01), [I-02](track-i.md#i-02), [R-01](track-r.md#r-01), [R-02](track-r.md#r-02), [E-01](track-e.md#e-01) | Ingress pipeline, 소유권·포화 검증 |
| [I-04: Console Echo DSN Mock 구현](#i-04) | [I-01](track-i.md#i-01), [I-02](track-i.md#i-02), [R-01](track-r.md#r-01) | DSN Mock, 출력 fixture, 실행 가이드 |

<a id="i-01"></a>
## I-01. RPC Server Adapter 구현

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** F-02

**하위 작업**

- [ ] `I-01.1` 합의한 RPC endpoint·호출 수신·session 수명 구현
- [ ] `I-01.2` RPC 취소/종료와 진행 중 입력의 경합 처리
- [ ] `I-01.3` 대역 sink로 요청 인계 및 Source/session 식별 검증

**산출물:** RPC Server Adapter, 요청·session 테스트

**완료 기준:** 합의한 RPC 입력·종료 계약을 지키며 개별 메시지 처리 ACK/NACK을 새로 제공하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**담당자 결정:** 확정된 RPC·모듈 계약 안의 구현 및 문서·검증 구성

**협의 조건:** RPC 입력/응답 의미·Source 식별·소유권·DSN 배포 또는 공개 계약을 새로 정하거나 변경할 때

**협의 대상·관련 경계:** I·H·해당 DSN 계약 소비자; RPC 외부 호환성은 Source 담당자 — DC-02, DC-10 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.

<a id="i-02"></a>
## I-02. Version Selector와 Decoder 구현

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** F-02

**하위 작업**

- [ ] `I-02.1` 최소 공통 헤더와 버전 선택 구현
- [ ] `I-02.2` v1 구조·범위 검증과 공통 모델 생성
- [ ] `I-02.3` 정상·손상·미지원 버전 fixture 실행

**산출물:** 공유 Protocol/Decoder, fixture 결과

**완료 기준:** payload 의미를 해석하지 않고 공통 fixture와 일치한다. 잘못된 길이로 범위를 벗어나 읽지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**담당자 결정:** 동일 규약을 해석하는 decoder 코드·내부 검증 순서

**협의 조건:** version·bytes·길이 한도·오류 결과·공통 fixture를 바꿀 때

**협의 대상·관련 경계:** 외부 Source 담당; 공통 오류 의미 E — DC-02, DC-08 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.

<a id="i-03"></a>
## I-03. Ingress → Lifetime → Queue 인계

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** I-01, I-02, R-01, R-02, E-01

**하위 작업**

- [ ] `I-03.1` 원본/root 확보·검증·큐 인계 연결
- [ ] `I-03.2` 실패 시 단일 Checkin과 오류 보고
- [ ] `I-03.3` writer 종료와 진행 중 enqueue 정리

**산출물:** Ingress pipeline, 소유권·포화 검증

**완료 기준:** 성공 시 root를 한 번 인계하고 실패 시 회수한다. writer 종료 확정 후 enqueue하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**담당자 결정:** 규정된 성공/실패 인계 흐름과 내부 테스트

**협의 조건:** root 인계·적재 실패 반납·writer 종료 시점을 바꿀 때

**협의 대상·관련 경계:** R의 Lifetime/Queue/Executor 담당; 오류 E, 종료 H — DC-04, DC-09 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.

<a id="i-04"></a>
## I-04. Console Echo DSN Mock 구현

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** I-01, I-02, R-01

**하위 작업**

- [ ] `I-04.1` 공유 transport/decoder를 사용하는 console 수신기 구성
- [ ] `I-04.2` 바이너리 표시·길이 제한·출력 포화·원본 반납 구현
- [ ] `I-04.3` 실행 옵션·진단·종료 절차 작성

**산출물:** DSN Mock, 출력 fixture, 실행 가이드

**완료 기준:** Workspace·DB·View 없이 수신한다. 느린 console에서도 출력 메모리가 무한 증가하지 않고 원본 참조를 회수한다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**담당자 결정:** echo formatter·출력 버퍼 구현과 내부 테스트

**협의 조건:** Source 입력 규약·공개 CLI/endpoint·출력 규격·lease 수명을 바꿀 때

**협의 대상·관련 경계:** 외부 Source 담당, 참조 계약 R, 배포/인수 H·X — DC-02, DC-04, DC-10 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.
