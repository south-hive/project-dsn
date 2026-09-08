# Track S: C++ Source SDK

[전체 순서 및 의존성](../10-implementation-plan.md) · [검증 기준](../11-verification.md)

## 입력 문서

- [03-source-and-mock.md](../03-source-and-mock.md)
- [08-sequences.md](../08-sequences.md)
- [07-types-and-interfaces.md](../07-types-and-interfaces.md)

## 작업 순서

선행 task가 모두 완료되면 착수한다. 같은 track에서도 의존 관계가 없으면 병렬 진행할 수 있다. 하위 작업은 해당 task 안의 구현 순서다.

| Task | 선행 task | 산출물 |
| --- | --- | --- |
| [S-01: C++ Fire Gun과 Magazine 구현](#s-01) | [F-03](track-f.md#f-03) | C++ 발행·magazine, 계약 테스트 |
| [S-02: C++ Encoder와 Sender 구현](#s-02) | [S-01](track-s.md#s-01), [F-02](track-f.md#f-02) | encoder, sender, fixture 결과 |
| [S-03: C++ attach/detach와 재접속 구현](#s-03) | [S-02](track-s.md#s-02) | SDK lifecycle 구현, 장애·종료 결과 |
| [S-04: C++ SDK 패키지와 Mock 예제](#s-04) | [S-03](track-s.md#s-03), [I-04](track-i.md#i-04) | SDK 배포물, 개발 가이드, 실행 예제 |

<a id="s-01"></a>
## S-01. C++ Fire Gun과 Magazine 구현

**상태:** 미착수

**선행 조건:** F-03

**하위 작업**

- [ ] `S-01.1` 준비 데이터 소유권과 슬롯 수명 구현
- [ ] `S-01.2` Publish 적재·포화 폐기 구현
- [ ] `S-01.3` 동시 producer 및 초기화/종료 전환 검증

**산출물:** C++ 발행·magazine, 계약 테스트

**완료 기준:** 호출자 버퍼 변경에 메시지가 손상되지 않고 포화 시 기다리지 않는다. detach 경합에서 해제된 자원에 접근하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="s-02"></a>
## S-02. C++ Encoder와 Sender 구현

**상태:** 미착수

**선행 조건:** S-01, F-02

**하위 작업**

- [ ] `S-02.1` v1 encoder와 fixture 대조
- [ ] `S-02.2` 탄창 소비와 별도 전송 흐름 연결
- [ ] `S-02.3` 미연결·송신 실패 시 메시지 및 자원 정리

**산출물:** encoder, sender, fixture 결과

**완료 기준:** 공통 bytes를 생성하고 Publish가 전송 완료를 기다리지 않는다. 전송 오류·예외를 호출자로 전파하지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="s-03"></a>
## S-03. C++ attach/detach와 재접속 구현

**상태:** 미착수

**선행 조건:** S-02

**하위 작업**

- [ ] `S-03.1` attach/detach 상태 전이 구현
- [ ] `S-03.2` 수신기 종료와 후속 attach 처리
- [ ] `S-03.3` 잔여 슬롯·진행 중 호출 종료 순서 검증

**산출물:** SDK lifecycle 구현, 장애·종료 결과

**완료 기준:** 메시지 ACK/NACK 없이 재접속하며 반복 attach/detach에서 slot/session 누수가 없다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="s-04"></a>
## S-04. C++ SDK 패키지와 Mock 예제

**상태:** 미착수

**선행 조건:** S-03, I-04

**하위 작업**

- [ ] `S-04.1` 헤더·라이브러리·버전·build 예제 구성
- [ ] `S-04.2` DSN/Mock 목적지 전환 예제 작성
- [ ] `S-04.3` 정상·미연결·포화 결과와 제약 기록

**산출물:** SDK 배포물, 개발 가이드, 실행 예제

**완료 기준:** 지원 환경에서 패키지만으로 빌드하고 Mock에 envelope 및 바이너리 payload가 표시된다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.
