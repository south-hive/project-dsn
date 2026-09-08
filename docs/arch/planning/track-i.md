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
| [I-01: Transport Adapter 구현](#i-01) | [F-02](track-f.md#f-02) | Transport Adapter, session 테스트 |
| [I-02: Version Selector와 Decoder 구현](#i-02) | [F-02](track-f.md#f-02) | 공유 Protocol/Decoder, fixture 결과 |
| [I-03: Ingress → Lifetime → Queue 인계](#i-03) | [I-01](track-i.md#i-01), [I-02](track-i.md#i-02), [R-01](track-r.md#r-01), [R-02](track-r.md#r-02), [E-01](track-e.md#e-01) | Ingress pipeline, 소유권·포화 검증 |
| [I-04: Console Echo DSN Mock 구현](#i-04) | [I-01](track-i.md#i-01), [I-02](track-i.md#i-02), [R-01](track-r.md#r-01) | DSN Mock, 출력 fixture, 실행 가이드 |

<a id="i-01"></a>
## I-01. Transport Adapter 구현

**상태:** 미착수

**선행 조건:** F-02

**하위 작업**

- [ ] `I-01.1` 입력 endpoint·수신 자원·session 수명 구현
- [ ] `I-01.2` 수신과 연결 종료 경합 처리
- [ ] `I-01.3` 대역 sink로 frame 인계·session 식별 검증

**산출물:** Transport Adapter, session 테스트

**완료 기준:** frame 수명과 종료 계약을 지키고 다른 Source session을 잘못 종료하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="i-02"></a>
## I-02. Version Selector와 Decoder 구현

**상태:** 미착수

**선행 조건:** F-02

**하위 작업**

- [ ] `I-02.1` 최소 공통 헤더와 버전 선택 구현
- [ ] `I-02.2` v1 구조·범위 검증과 공통 모델 생성
- [ ] `I-02.3` 정상·손상·미지원 버전 fixture 실행

**산출물:** 공유 Protocol/Decoder, fixture 결과

**완료 기준:** payload 의미를 해석하지 않고 공통 fixture와 일치한다. 잘못된 길이로 범위를 벗어나 읽지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="i-03"></a>
## I-03. Ingress → Lifetime → Queue 인계

**상태:** 미착수

**선행 조건:** I-01, I-02, R-01, R-02, E-01

**하위 작업**

- [ ] `I-03.1` 원본/root 확보·검증·큐 인계 연결
- [ ] `I-03.2` 실패 시 단일 Checkin과 오류 보고
- [ ] `I-03.3` writer 종료와 진행 중 enqueue 정리

**산출물:** Ingress pipeline, 소유권·포화 검증

**완료 기준:** 성공 시 root를 한 번 인계하고 실패 시 회수한다. writer 종료 확정 후 enqueue하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="i-04"></a>
## I-04. Console Echo DSN Mock 구현

**상태:** 미착수

**선행 조건:** I-01, I-02, R-01

**하위 작업**

- [ ] `I-04.1` 공유 transport/decoder를 사용하는 console 수신기 구성
- [ ] `I-04.2` 바이너리 표시·길이 제한·출력 포화·원본 반납 구현
- [ ] `I-04.3` 실행 옵션·진단·종료 절차 작성

**산출물:** DSN Mock, 출력 fixture, 실행 가이드

**완료 기준:** Workspace·DB·View 없이 수신한다. 느린 console에서도 출력 메모리가 무한 증가하지 않고 원본 참조를 회수한다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.
