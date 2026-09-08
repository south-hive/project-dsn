# 시스템·패키지·컴포넌트 구조

> 상세 설계 참조입니다. 처음 읽는 부서원은 [전체 → 기능 → 담당 작업 안내](../development/README.md)에서 시작하세요. 아래 초기 설계의 미정/제안과 현재 구현 선택은 [구현 계약](../implementation/contracts.md)을 함께 확인합니다.

## 표기와 설계 상태

필수 동작은 [설계 기준 D01–D26](05-decisions.md)를 따른다. 아래 패키지·클래스 이름과 내부 배치는 **구현 설계안**이다. Source 내부는 외부 담당 범위이며 DSN 공통 경계는 RPC다. 패키지는 논리 모듈이며 각각 별도 프로세스나 배포 단위를 뜻하지 않는다. 화살표 의미는 각 그림에서 정의한다.

## SYS-01. 전체 시스템

실선은 메시지 또는 record 흐름, 점선은 참조·조회 또는 상세 규약이 미정인 연결이다.

```mermaid
flowchart TB
    App["C++ Application"] --> SDK["외부 Source 구현"]
    Kernel["Kernel Driver / eBPF"] --> KA["Kernel 발행 경로"]
    SDK --> Sender["외부 RPC 호출 경로"]
    KA --> Bridge["외부 RPC 호출 경로"]
    Sender --> TA["DSN RPC Server Adapter"]
    Bridge --> TA
    TA --> DEC["Version Selector / Envelope Decoder"]
    DEC --> Q["Signal Buffer / no_policy"]
    Q --> RUN["Sequential Workspace Executor"]
    RUN -. "대상 조회" .-> BB["Bulletin Board / Registry"]
    RUN --> WS["Workspace Plugins"]
    Q -. "소유 참조" .-> ML["Message Lifetime"]
    RUN -. "실행 참조" .-> ML
    WS -. "Checkout / Checkin" .-> ML
    WS --> PS["Persistence"]
    TA --> ER["Error Intake / Aggregator"]
    DEC --> ER
    RUN --> ER
    BB --> ER
    ER -. "관리 전달 규약 미정" .-> ADMIN["Admin Workspace"]
    ADMIN --> PS
    User["사용자"] --> VIEW["View / 사용자별 Field 선택과 조합"]
    VIEW -. "IRecordQuery" .-> PS
    Sender --> MOCK["DSN Mock / Console Echo"]
    Bridge --> MOCK
```

Mock은 Source의 대체 목적지다. 그림의 두 목적지 화살표가 동일 메시지의 동시 이중 전송을 요구하지 않는다. Admin은 Workspace의 특별한 구현이며, 일반 메시지 경로를 통한 Admin 전달 여부는 미정이다.

## PKG-01. 패키지 의존성

화살표 `A → B`는 **A가 B의 타입 또는 계약에 의존**함을 뜻한다. 데이터 흐름과 다르다. 각 구현은 필요한 계약 부분만 참조하며 Host만 실제 구현을 조립한다.

```mermaid
flowchart LR
    Host["Dsn.Host"] --> Ingress["Dsn.Ingress"]
    Host --> Routing["Dsn.Routing"]
    Host --> Execution["Dsn.Execution"]
    Host --> Memory["Dsn.MessageLifetime"]
    Host --> Queue["Dsn.Buffer"]
    Host --> Diagnostics["Dsn.Diagnostics"]
    Host --> Store["Dsn.Persistence"]
    Host --> View["Dsn.View"]
    Host --> Loader["Dsn.PluginLoading"]
    Ingress --> Protocol["Dsn.Protocol"]
    Protocol --> Models["Dsn.MessageModel"]
    Ingress --> Runtime["Dsn.Runtime.Contracts"]
    Routing --> Runtime
    Execution --> Runtime
    Memory --> Runtime
    Queue --> Runtime
    Runtime --> SDK["Dsn.Workspace.Abstractions"]
    Runtime --> Models
    Runtime --> DiagAPI["Dsn.Diagnostics.Abstractions"]
    SDK --> Models
    SDK --> Records["Dsn.Record.Contracts"]
    SDK --> DiagAPI
    Diagnostics --> DiagAPI
    Store --> Records
    View --> Records
    Loader --> SDK
    Workspace["Workspace Plugins / Admin"] --> SDK
    Mock["Dsn.Mock"] --> Ingress
    Mock --> Protocol
    Mock --> Runtime
    Mock --> Memory
```

`Dsn.Runtime.Contracts`는 DSN 내부 모듈 사이의 계약 패키지다. C#에서 다른 assembly가 참조할 수 있다는 사실과 외부 플러그인에 지원하는 API인지는 구분한다. Workspace 프로젝트의 참조 목록에 Runtime·Ingress·Execution 구현을 포함하지 않는 검증을 둔다. 인터페이스별 제공자·소비자는 [IF-01](07-types-and-interfaces.md)에 정의한다.

외부 Source는 RPC 규격과 공통 입력 fixture로 상호 운용한다. DSN은 Source SDK 내부 구현을 제공하지 않는다. 실제 프로젝트 디렉터리와 assembly 분할은 작업 F-01에서 고정한다.

## CMP-01. 외부 Source와 RPC 경계

```mermaid
flowchart LR
    Caller["원 프로젝트 / Source 담당 구현"] --> Client["외부 RPC 호출 구현"]
    Contract["DSN 제공 RPC 계약 / Envelope"] -.-> Client
    Client --> Server["DSN RPC Server Adapter"]
    Client --> Mock["RPC Console Mock"]
```

Source의 언어별 API·탄창·sender·kernel/eBPF·중계·메모리 수명은 Source 담당자가 결정한다. DSN은 RPC 계약과 Mock을 제공한다. 두 endpoint는 대체 목적지다. RPC framework와 호출 형태는 F-02에서 정한다.

## CMP-02. Ingress와 Queue

```mermaid
flowchart LR
    TA["RPC Server Adapter"] --> RX["수신 Frame / Session Context"]
    RX --> VS["Minimum Header / Version Selector"]
    VS --> D1["Envelope Decoder v1"]
    VS -. "향후" .-> DN["Envelope Decoder vN"]
    D1 --> CM["공통 Envelope / Opaque Payload Slice"]
    DN --> CM
    CM --> SQ["Signal Buffer Writer"]
    SQ --> Q["FIFO Queue"]
    POL["IQueuePolicy / no_policy"] --> Q
    RX -. "원본 확보" .-> LM["Message Lifetime"]
    Q -. "소유 참조 인계" .-> LM
    VS --> ERR["IErrorSink"]
    D1 --> ERR
    SQ --> ERR
```

소유 참조는 Decoder 성공 이후에도 동일 원본을 가리킨다. framing을 위해 필요한 조립·복사는 전송 기술 결정에 따른다. 검증 실패나 큐 적재 실패에서는 현재 소유자가 참조를 반납한다. `source_id`가 아직 검증되지 않은 입력 오류는 수신 session 문맥으로 기록할 수 있어야 한다. session과 Source의 매핑 규칙은 F-02에서 결정한다.

## CMP-03. Routing·Execution·Memory

```mermaid
flowchart TB
    PL["Plugin Loader"] --> REG["Registry / 시작 시 등록"]
    REG --> SNAP["등록 완료 목록"]
    R["Signal Buffer Reader"] --> LOOP["Sequential Execution Loop"]
    LOOP --> ROUTE["Bulletin Board / Resolve Targets"]
    ROUTE --> SNAP
    LOOP --> CTX["Workspace Invocation Context"]
    CTX --> WA["Workspace A"]
    CTX --> WB["Workspace B"]
    WA -->|"Checkout / Checkin"| ML["Message Lifetime / Lease Ledger"]
    WB -->|"Checkout / Checkin"| ML
    CTX -->|"호출 종료 시 미반납 정리"| ML
    LOOP -->|"전체 호출 동안 root 참조 유지"| ML
    WA --> RS["IRecordStore"]
    WB --> RS
```

A와 B는 병렬 실행을 나타내지 않는다. 한 메시지에 대한 대상 목록을 얻은 뒤 순차 호출한다. 실행 루프의 root 참조는 마지막 대상 호출까지 유지한다. Workspace가 자신의 lease를 일찍 반납해도 뒤의 대상이 사용할 원본이 남아 있어야 한다. 대상 배열 내 호출 순서와 중복 목적지 처리는 F-04에서 확정한다.

## CMP-04. 오류·Admin·Persistence·View

```mermaid
flowchart LR
    Producers["DSN 내부 오류 발생 지점"] --> Intake["Error Intake / IErrorSink"]
    Intake --> Agg["Error Aggregator"]
    Agg --> State["집계 상태 / First / Last / Count"]
    State -. "규약 미정" .-> Admin["Admin Workspace / Record 변환"]
    Regular["업무 Workspace"] --> Write["IRecordStore"]
    Admin --> Write
    Write --> Adapter["Persistence Adapter"]
    Adapter --> Storage["Record Storage"]
    View["View Definition / Query Service"] --> Query["IRecordQuery"]
    Query --> Adapter
    Export["Export Consumer"] --> ExportAPI["IRecordExporter"]
    ExportAPI --> Adapter
    User["사용자"] --> View
```

View는 field 선택·조합 정의를 소유한다. 실제 query 실행을 저장소에 위임할 수 있다. 조회용 cache와 실시간 상태 조회는 기본 구성에 포함하지 않는다. Admin 원본 메시지 wrapping, 첨부 및 집계 결과 갱신 규약은 A-01에서 결정한다.

## CMP-05. DSN Mock

```mermaid
flowchart LR
    Source["Source Sender"] --> Transport["실제 DSN과 같은 RPC Server Adapter"]
    Transport --> Decoder["공유 Version Selector / Decoder"]
    Decoder --> Echo["Echo Formatter"]
    Echo --> Output["제한된 Console Output Buffer"]
    Output --> Console["stdout / stderr"]
    Decoder --> Diag["Mock 진단 / Drop Count"]
    Output --> Diag
```

Mock은 Bulletin Board, Workspace, record 저장소, View 없이 실행한다. Echo 데이터가 원본 참조를 보유한다면 출력 완료·폐기 시 반납한다. 초기 구현은 짧은 출력 표현으로 복사한 뒤 원본을 반납하는 안을 우선 검증한다. 출력 용량과 표시 형식은 I-04에서 명시한다.
