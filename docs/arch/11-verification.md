# 검증·계측·요구사항 추적

## 검증 구분

| 단계 | 범위 | 대표 task |
| --- | --- | --- |
| 계약 검증 | byte fixture, 타입·소유권·순서·실패 규칙 | F-02–05 |
| 모듈 검증 | 실제 구현과 대역 소비자/제공자 | S/I/R/E/P/V 각 구현 task |
| 경계 통합 | 실제 Source→Mock, Ingress→Runtime, Workspace→저장소 | S-04, K-04, R-06, X-01–02 |
| 배포·lifecycle | Docker와 대상 kernel, 시작·종료·attach/detach | H-03, X-03 |
| 부하·자원 | 발행 지연, CPU, 원본 수명, 포화·손실·조회 영향 | F-06, E-02, X-04 |

문서의 예상 동작과 실제 검증 결과는 구분한다. 현재 문서는 검증 계획이며 DSN 구현의 테스트 통과나 성능 달성을 보고하지 않는다.

## 검증 시나리오

| ID | 입력·상황 | 관측 및 합격 조건 | 담당 task |
| --- | --- | --- | --- |
| T01 | 정상 및 손상 envelope, 미지원 version | fixture 일치; payload 미해석; 범위 밖 접근 없음 | I-02 |
| T02 | DSN 부재·전송 실패·탄창 포화 | Publish가 연결·전송·큐 여유를 기다리지 않고 오류·예외를 전파하지 않음 | S-01–03, K-02–03 |
| T03 | C++·driver·eBPF → Mock | 동일 규약으로 echo; console 포화 시 출력 한도 및 자원 회수 | S-04, K-04, X-01 |
| T04 | 시작 시 정상·중복·부적합 plugin | 이름 하나 등록, 후발 중복 거부, 준비 전 수신 없음, 오류 접수 가능 | R-03, H-02 |
| T05 | 단일 큐에 번호를 부여한 메시지 적재 | dequeue·호출 순서가 FIFO; timestamp가 달라도 no_policy로 재정렬 안 함 | R-02, R-05 |
| T06 | 대상 Workspace 둘 | 원본 버퍼 하나, 두 파싱 결과 독립; 호출 동시성 1 | R-06, X-02 |
| T07 | Checkin 후 다음 Workspace 호출 | executor root가 남아 다음 Checkout 가능; 마지막 root 반납에서 회수 | R-01, R-06 |
| T08 | 중복 Checkin·파싱 실패·미반납 후 함수 반환 | count 음수·조기 회수·누수 없음; 실행 종료 정리와 정상 반납 중복 감소 없음 | R-01, R-05 |
| T09 | 검증 실패·큐 적재 실패 | root 인계 유무에 맞게 현재 소유자가 한 번 반납 | I-03 |
| T10 | Workspace가 반환하지 않는 처리 | timeout만으로 원본을 회수하지 않음; 정의된 종료 상태 보고 | H-02, X-03 |
| T11 | 같은 오류 본문, 다른 시각 또는 다른 필드 | 시각만 다르면 first/last/count 갱신; 다른 본문은 별도 집계 | E-01 |
| T12 | Admin 미가동·오류 접수 포화 | Admin 처리와 접수 분리; 재귀 보고 없음; 명시된 한도·fallback | E-01, A-02 |
| T13 | 여러 Source 중 하나의 심각 오류 | 해당 Source만 종료; 다른 Source 유지; 신규 attach 및 재발 반복 | E-03, X-03 |
| T14 | Source detach 중 수신·처리 | 진행 중 전송 자원 정리; 인계된 DSN root 수명 독립 | S-03, K-04, X-03 |
| T15 | DSN 종료·잔여 큐·진행 중 Workspace | writer 종료 뒤 drain/폐기, 미실행 root 반납, 활성 메모리 보호 | H-02, X-03 |
| T16 | Checkin 이후 저장·재시작·export | 원본 의존 없는 record, 선택 저장 보장 및 export 값 일치 | P-02–03 |
| T17 | 사용자별 서로 다른 View 정의 | Record 조회 계약만 사용; field 선택·조합이 fixture와 일치 | V-02–03 |
| T18 | 정상·burst·느린 처리·느린 저장·조회 부하 | 지연·CPU·메모리·손실과 root/lease 수명을 같은 workload에서 기록 | X-04 |

## MET-01. 계측 정의

| 지표 | 시작·종료 또는 계수 지점 | 단위와 보고 방식 |
| --- | --- | --- |
| Publish 호출 지연 | 동일 Source 안에서 Publish 진입→반환 | 시간 단위 명시; p50/p95/p99·관측 최대·표본 수 |
| Source 적재 결과 | 탄창 적재 시도·성공·폐기 | 건수; SDK 계측 비용과 포함 범위 명시 |
| DSN 수신량 | 구조 검증 전 frame 수와 검증 후 message 수를 구분 | source_id 식별 가능 여부, 구간별 건수 |
| 큐 적재·소비·폐기 | root 인계 성공/실패 및 dequeue/종료 폐기 | 건수, 현재·최대 깊이, 보유 bytes |
| Workspace lease 보유 시간 | Checkout→해당 Checkin | Workspace별 분포; 실행 종료 정리 건수 별도 |
| 원본 전체 수명 | Message Lifetime 원본 확보→ref count 0 | root 포함 수명, bytes, 최대 동시 원본 수 |
| Workspace 처리 시간 | Process 호출→반환 | Workspace별 분포; 파싱/저장 비용 분리 가능 시 별도 |
| Record 저장 지연 | Append 진입→계약상 반환 | 접수와 영속 완료 의미를 보고서에 명시 |
| DSN CPU·메모리 | 일정 workload 구간 | 측정 도구·범위 명시; runtime/container와 relay 구분 |
| 오류 집계 | Report 접수, 집계 항목 수, count, 접수 폐기 | 실제 이벤트 수와 고유 본문 수 구분 |
| View 조회 지연 | View 요청→응답 | 조회량·record 수·field 수·동시 요청 수와 함께 보고 |

모든 시점 차이는 같은 시계 기준에서 계산한다. Source의 envelope time과 DSN 시각을 단순히 빼서 전송 지연으로 보고하지 않는다. 계측이 없는 Source 경로의 전송 전 손실을 DSN 수신량만으로 계산하지 않는다. 관측 최대 지연은 hard real-time 상한 증명이 아니다.

## MET-02. 통제된 검증에서의 계수 보존

아래 식은 테스트 경로의 입력·출력과 초기·종료 상태를 모두 관측할 수 있을 때만 사용한다. 임의 전송 구간 전체에 무손실을 가정하는 식이 아니다.

```text
탄창 적재 시도 = 적재 성공 + 적재 시점 폐기

큐 초기 보유 + 적재 성공
    = dequeue 성공 + 미실행 폐기 + 큐 종료 보유

생성 원본 수 = 회수 원본 수 + 현재 살아 있는 원본 수

참조 현재 수 = 초기 참조 수 + 생성 root + Checkout 횟수 - 유효 Checkin 횟수
```

root 인계는 생성·Checkout에 포함하지 않는다. 중복 Checkin 요청은 유효 Checkin 횟수를 증가시키지 않는다. dequeue 후 처리 중인 root는 큐 보유량에서 빠지지만 살아 있는 원본 수에는 남는다. 참조 종류별 ledger로 검증한다.

## 계측 실행 계획

1. F-06에서 환경·메시지 크기·Source 수·발행률·Workspace 수·실행 시간과 반복 수를 정의한다.
2. 계측 최소 구성으로 baseline을 측정하고 상세 계측 구성의 overhead를 비교한다.
3. 정상 부하, burst, 수신 한계 초과, 느린 Workspace, 느린 저장소, 원격 View 부하를 각각 실행한다.
4. detach·재접속·종료 후 잔여 root/lease·탄창 슬롯·session 자원을 확인한다.
5. 수치 목표가 확정된 항목만 합격 여부를 판정한다. 미정 항목은 baseline·병목·후속 결정으로 보고한다.

| 결과 필드 | 필수 내용 |
| --- | --- |
| 코드·설정 | commit, toolchain, image, kernel, plugin 목록, 용량·정책 |
| workload | 메시지 크기 분포, Source/Workspace 수, 발행 패턴, 조회 부하 |
| 측정 | 도구·시계·표본·반복·단위·원시 결과 위치 |
| 해석 | 입력/수신/처리/저장의 차이, 손실 관측 범위, 계측 overhead |
| 판정 | 목표와 비교 또는 목표 미정, 미검증 환경, 알려진 제한 |

## 요구사항·다이어그램·task 추적

| 설계 기준 | 구조·시퀀스 | 구현 task | 검증 |
| --- | --- | --- | --- |
| D01·03: C# Docker, 동일 호스트, 원격 View | SYS-01, DEP-01, SEQ-08 | F-01, H-03, V-03 | T17–18, X-03 |
| D02·04·05: 다양한 Source, 탄창, 실패 비전파 | CMP-01, SEQ-03–06 | F-02–03, S-01–04, K-01–04 | T02–03, T14 |
| D06·07: 수용 초과와 Source 수신 계측 | CMP-02, SEQ-07, MET-01 | I-03, E-01–02 | T09, T18 |
| D08: 해당 Source 종료와 신규 attach | SEQ-05 | F-02, E-03 | T13 |
| D09·21: 오류 집계와 Admin 분리 | CMP-04, CLS-03 | E-01, A-01–02 | T11–12 |
| D10–12: Workspace 이름 기반 등록·분배 | CMP-03, CLS-02, SEQ-01 | R-03, H-02 | T04 |
| D13·22: payload 블랙박스, 버전별 검증 | CMP-02, IF-D01 | F-02, I-02 | T01 |
| D14·19·20: 공유 원본, Lifetime, 명시적 반납 | CLS-01, SEQ-06–07 | R-01, R-06 | T06–10 |
| D15·16·23: FIFO, no_policy, 순차 실행 분리 | CMP-03, CLS-02, SEQ-06 | R-02, R-05 | T05–07 |
| D17: DSN Mock | CMP-05, DEP-02 | I-04, X-01 | T03 |
| D18·25: 인터페이스 경계·공개 범위 | PKG-01, IF-01 | F-04, R-03–04 | 프로젝트 참조 검증, T04 |
| D24: Persistence·사용자 View 분리 | CMP-04, CLS-03, SEQ-08 | F-05, P-01–03, V-01–03 | T16–17 |
| 수명주기 구현안: 시작·종료 | SEQ-01–02, DEP-01 | H-01–03 | T10, T15 |

## 문서 검증과 구현 검증

문서 검증은 링크·task 의존성·ID·Mermaid 문법 및 도식 간 계약 일치를 확인한다. 실제 구현의 기능·성능 검증과 별도다. 구현 검증 결과는 각 task의 완료 기록에 남기고 X-05에서 Release manifest와 연결한다.
