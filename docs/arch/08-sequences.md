# 주요 실행 시퀀스

아래는 확정된 책임을 연결한 **수명주기 구현안**이다. DSN 시작·종료 정책, Source 재접속·잔여 탄창 정책은 F-03·H-01에서 구체화한다. 메시지 ACK/NACK은 추가하지 않는다. 시간 제한의 수치는 미정이다.

## SEQ-01. DSN 실행

```mermaid
sequenceDiagram
    actor Operator as 운영자
    participant Host as DSN Host
    participant Error as Error Intake
    participant Core as Lifetime / Buffer / Protocol
    participant Store as Persistence
    participant Loader as Plugin Loader
    participant Registry as Bulletin Board Registry
    participant Run as Sequential Executor
    participant Transport as Transport Adapter
    participant View as View Service
    Operator->>Host: DSN 시작
    Host->>Error: 초기화 및 오류 접수 활성화
    Host->>Core: 용량과 버전 설정으로 초기화
    Host->>Store: 저장·조회 adapter 초기화
    Host->>Loader: 시작 시 플러그인 탐색 및 로딩
    loop 발견한 Workspace
        Loader->>Loader: 계약 버전 및 이름 검사
        Loader->>Registry: Register(workspace)
        alt 이름 중복 또는 플러그인 부적합
            Registry->>Error: 등록 실패 기록
            Note over Loader,Registry: 부적합 플러그인 거부, 필수 플러그인 실패 정책은 H-01
        else 정상 등록
            Registry-->>Loader: 등록 완료
        end
    end
    Host->>Registry: 등록 완료 상태로 전환
    Host->>Run: 실행 루프 시작
    Host->>Transport: 입력 endpoint 열기
    Host->>View: 조회 endpoint 열기
    Host-->>Operator: Running 상태
```

오류 접수는 플러그인 로딩 전에 준비한다. 실행 루프와 registry가 준비되기 전에 입력을 수락하지 않는 순서를 제안한다. 중간 초기화 실패 시 이미 열린 자원을 역순으로 정리하고 Running으로 표시하지 않는다. Admin 로딩 실패가 오류 접수 자체를 막아서는 안 된다.

## SEQ-02. DSN 정상 종료 및 제한 시간 초과

```mermaid
sequenceDiagram
    actor Operator as 운영자
    participant Host as DSN Host
    participant View as View Service
    participant Transport as Transport Adapter
    participant Queue as Signal Buffer
    participant Run as Executor / Workspace
    participant Memory as Message Lifetime
    participant Store as Persistence
    participant Error as Diagnostics
    Operator->>Host: 종료 요청
    Host->>View: 신규 조회 접수 중지
    Host->>Transport: 신규 attach 및 입력 접수 중지
    Transport->>Transport: 수신 작업 종료 및 session 정리
    Host->>Queue: writer 종료 확정
    Host->>Run: 정해진 종료 모드로 처리 종료 요청
    alt drain 시간 내 종료
        Run->>Queue: 잔여 메시지 FIFO 소비
        Run->>Memory: 각 처리 참조 Checkin
        Run-->>Host: 실행 중 호출 없음
    else drain 제한 초과
        Host->>Queue: 미실행 항목 폐기
        Queue->>Memory: 미실행 root Checkin
        Host->>Run: 협력적 중단 요청
        Note over Run,Memory: 실행 중인 호출의 원본은 강제 회수하지 않음
        Run-->>Host: 종료 확인 또는 미종료 상태 보고
    end
    alt 실행 중 호출과 조회가 모두 종료됨
        Host->>Store: 허용된 범위에서 record flush 및 종료
        Host->>Memory: 미반납 참조 점검
        alt 활성 참조 없음
            Host->>Memory: 메모리 자원 해제
            Host->>Error: 최종 진단 집계 및 종료
            Host-->>Operator: Stopped
        else 미반납 참조 존재
            Host->>Error: 잔여 참조 기록
            Host-->>Operator: 종료 정리 실패 상태
        end
    else 호출 또는 조회가 종료되지 않음
        Host->>Error: 미종료 작업 기록
        Host-->>Operator: 종료 미완료 상태
        Note over Host,Memory: 사용 중 저장소와 버퍼 유지, 프로세스 종료 정책은 H-01
    end
```

writer 종료는 진행 중인 enqueue까지 정리된 시점을 뜻한다. 정상 drain과 즉시 폐기 중 기본 모드, flush 보장, 제한 시간 및 프로세스 종료 정책은 H-01에서 결정한다. 종료 중 Source는 수신 측 완료를 기다리지 않는다. 큐 폐기와 오류 기록도 항상 완료된다고 가정하지 않는다. Source 연결 재허용 정책은 Running 상태의 오류 대응에 적용하며, DSN 종료 중 신규 연결 수락을 요구하지 않는다.

## SEQ-03. Source attach

```mermaid
sequenceDiagram
    participant App as 원 프로젝트
    participant SDK as Fire Gun / Magazine
    participant Sender as Source Sender
    participant Transport as DSN Transport
    participant Session as Session Context
    App->>SDK: 초기화 및 발행 준비
    SDK->>Sender: 전송 실행 흐름 시작
    Sender->>Transport: attach 시도
    alt endpoint 사용 가능
        Transport->>Session: 연결 또는 논리 session 생성
        Transport-->>Sender: 전송 경로 사용 가능
    else DSN 미실행 또는 접속 실패
        Sender->>Sender: Detached 유지, 별도 실행 흐름에서 후속 시도
    end
    App->>SDK: Publish(message)
    SDK->>SDK: 적재 시도 또는 폐기
    SDK-->>App: 즉시 반환
    Note over App,Session: attach 결과는 개별 메시지 ACK가 아니며 Publish는 접속 완료를 기다리지 않음
```

attach는 전송 기술에 따라 연결 생성 또는 논리 송신 경로 활성화를 뜻한다. 별도 DSN 등록 handshake를 요구하지 않는다. 연결과 `source_id` 매핑은 F-02에서 정하며, envelope을 보기 전에는 source_id가 알려지지 않을 수 있다. 초기화 자체의 실패 보고 방식은 발행 API와 분리하여 F-03에서 정의한다.

## SEQ-04. Source 정상 detach

```mermaid
sequenceDiagram
    participant App as 원 프로젝트
    participant SDK as Fire Gun / Magazine
    participant Sender as Source Sender
    participant Transport as DSN Transport
    participant Queue as DSN Queue / Executor
    App->>SDK: detach 또는 SDK 종료 요청
    SDK->>SDK: 신규 적재 종료 상태 전환
    SDK->>Sender: 전송 중지 요청
    Sender->>Sender: 정해진 잔여 탄창 정책 적용
    Sender->>Transport: 연결 또는 송신 경로 해제
    Transport->>Transport: session 종료 및 수신 자원 정리
    Note over Transport,Queue: 이미 DSN에 인계된 메시지는 독립 수명으로 처리
    Sender-->>SDK: 전송 자원 사용 종료
    SDK->>SDK: 활성 접근 종료 확인 후 탄창 해제
    Note over App,SDK: 전환 중 Publish는 즉시 폐기, 해제된 SDK 객체 호출은 지원 계약 밖
```

detach 시 미송신 메시지를 폐기하는 것을 초기안으로 두되 F-03에서 고정한다. 종료 호출의 동기화와 unload 순서는 발행 호출과 구분한다. DSN이 인계받은 메시지를 Source detach만으로 강제 해제하지 않는다.

## SEQ-05. Source 오류에 따른 연결 종료 및 재attach

```mermaid
sequenceDiagram
    participant Sender as Source Sender
    participant TA as Transport Adapter
    participant Core as Ingress / 내부 처리
    participant Policy as 오류 분류 및 연결 제어
    participant Error as Error Intake
    Sender->>TA: 메시지 입력
    TA->>Core: frame 및 session 문맥
    Core->>Error: 내부 오류 보고
    Core->>Policy: 오류와 session 문맥 전달
    alt 해당 Source의 심각한 오류로 판정
        Policy->>TA: 해당 연결 또는 논리 Source 종료
        TA->>TA: 해당 자원만 종료
        Note over TA,Policy: 다른 Source 유지, 영구 차단 목록 기본 없음
        Sender->>Sender: 전송 실패 정리 / Publish 경로로 전파하지 않음
        Sender->>TA: 후속 attach 시도
        TA->>TA: 신규 session 수락 가능
        Note over Core,Policy: 같은 현상 재발 시 동일 절차 반복
    else 일반 오류
        Core->>Core: 해당 메시지 폐기 등 오류별 처리
    end
```

심각도와 임계값은 운영 항목이다. 연결 제어 계약의 실제 구현 위치와 다중 Source 중계의 선택적 차단 방식은 F-02·E-03에서 정한다. 테스트에서는 주입된 판정으로 해당 Source만 종료되는지를 검증한다.

## SEQ-06. 메시지 발행·수신·다중 Workspace 처리

```mermaid
sequenceDiagram
    participant App as 원 프로젝트
    participant Gun as Fire Gun
    participant Sender as Sender
    participant RX as Transport / Decoder
    participant Memory as Message Lifetime
    participant Queue as Signal Buffer
    participant Run as Sequential Executor
    participant Board as Bulletin Board
    participant WS as 대상 Workspace
    participant Store as Persistence
    App->>Gun: Publish(prepared)
    alt 탄창 적재 가능
        Gun->>Gun: message 적재
    else 탄창 적재 불가
        Gun->>Gun: 폐기 및 가능한 로컬 계측
    end
    Gun-->>App: 즉시 반환
    Note over App,Sender: 이하 작업은 발행 호출 밖에서 수행
    Sender->>Gun: 적재 메시지 소비
    Sender->>RX: 전송 시도
    RX->>Memory: 원본 확보 및 root 생성
    Note over RX,Memory: root ref count = 1
    RX->>RX: version 선택, envelope 구조 검증
    RX->>Queue: TryEnqueueOwned(root)
    Note over RX,Queue: 성공 시 소유권 인계, ref count 변화 없음
    Run->>Queue: TryDequeueOwned()
    Queue-->>Run: root 소유권 인계
    Run->>Board: Resolve(workspace names)
    Board-->>Run: 수신 대상 목록
    loop 각 대상 순차 호출
        Run->>WS: Process(message context)
        WS->>Memory: Checkout(context)
        Memory-->>WS: 읽기 전용 lease
        Note over Memory,WS: 단일 lease 예시: ref count 1 → 2
        WS->>WS: payload 파싱 및 필요한 데이터 복사
        WS->>Memory: Checkin(lease)
        Note over Memory,WS: ref count 2 → 1
        WS->>Store: Append(독립 record)
        Store-->>WS: 저장 계약에 따른 접수 또는 완료
        WS-->>Run: Process 반환
        Run->>Memory: 해당 호출의 미반납 참조 정리
    end
    Run->>Memory: Checkin(root)
    Note over Run,Memory: ref count 1 → 0, 원본 회수
```

그림의 정상 후반 경로는 적재·전송·수신에 성공한 메시지에만 적용한다. Workspace가 자신의 참조를 반납해도 Executor의 root는 마지막 호출까지 유지된다. 따라서 저장이 느리면 root 보유 시간은 여전히 늘어날 수 있다. 별도 record 쓰기 큐를 현재 계약에 암묵적으로 추가하지 않으며 P-01·P-02에서 저장 반환 의미를 정하고 X-04에서 측정한다.

## SEQ-07. 수신 실패와 Workspace 실패

```mermaid
sequenceDiagram
    participant RX as Ingress
    participant Memory as Message Lifetime
    participant Queue as Signal Buffer
    participant Run as Executor
    participant WS as Workspace
    participant Error as Error Intake
    alt envelope 검증 실패
        RX->>Error: InvalidEnvelope 또는 UnsupportedVersion
        RX->>Memory: 보유한 root Checkin
    else 큐 적재 실패
        RX->>Queue: TryEnqueueOwned(root)
        Queue-->>RX: 거부, 소유권 유지
        RX->>Memory: root Checkin
        RX->>Error: 수용 초과 오류
    else Workspace 파싱 실패
        Run->>WS: Process(context)
        WS->>Memory: Checkout
        WS-->>Run: 처리 실패
        Run->>Memory: 종료된 호출의 미반납 lease Checkin
        Run->>Error: Workspace 처리 오류
        Note over Run,WS: 다음 대상 계속 처리 여부는 F-04의 실패 계약
        Run->>Memory: 해당 메시지 처리 종료 시 root Checkin
    end
```

메모리 확보 전에 실패한 경우 존재하지 않는 root를 반납하지 않는다. 오류 보고는 원본 메모리 수명을 암묵적으로 늘리지 않는다. 원본 첨부는 Admin 후속 결정 항목이다.

## SEQ-08. 사용자 View 조회

```mermaid
sequenceDiagram
    actor User as 사용자
    participant View as View Service
    participant Query as IRecordQuery
    participant Store as Persistence Adapter
    User->>View: 자신의 View 정의와 조회 조건
    View->>View: field 선택과 조합 규칙 확인
    View->>Query: 저장 record 조회 요청
    Query->>Store: 저장소 조회
    Store-->>Query: record 및 조회 메타데이터
    Query-->>View: 조회 결과
    View->>View: 사용자별 결과 구성
    View-->>User: View 데이터
    Note over View,Store: Workspace 실행이나 message lease에 접근하지 않음
```

View 정의의 저장, 사용자 식별, 조회 범위 및 결합 의미는 F-05·V-01에서 고정한다. 원본 이벤트가 저장되기 전 상태는 기본 조회 대상이 아니다.
