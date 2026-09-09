# 4. Top Level Design Description

## Structure View

DSN은 한 Host 프로세스 안에 수신, 실행, 해석, 저장, 조회 기능을 조립한다. 화살표는 호출/의존 관계다. record 결과는 조회 요청의 반대 방향으로 반환된다.

```mermaid
flowchart LR
    I["Ingress"] --> R["Runtime<br/>FIFO·대상·원본 수명"]
    R --> W["Workspace"]
    W -->|"IRecordStore"| P["Persistence"]
    V["View / HTTP"] -->|"IRecordQuery·Exporter"| P
    I -->|"IErrorSink"| D["Diagnostics / Admin"]
    R -->|"IErrorSink"| D
    D -->|"관리 record"| P
    H["Host"] -. "생성·연결·종료" .-> I
    H -. "생성·연결·종료" .-> R
    H -. "생성·연결·종료" .-> V
```

`Dsn.Contracts`에 공개 Workspace·lease·record·진단 계약을 둔다. Ingress/Runtime/Diagnostics/Persistence/View와 plugin loader는 현재 `Dsn.Core`에 있고, 조립과 HTTP endpoint는 `Dsn.Host`, 예제 plugin은 `Dsn.Workspaces`에 있다. Mock은 수신 코드를 재사용하는 별도 실행 파일이다. 상세 내부 구조는 [Component Design](05-component-design.md)에 한 번만 기술한다.

## Behavior View

### 메시지 처리와 원본 수명

```mermaid
sequenceDiagram
    participant S as Source
    participant I as Ingress
    participant R as Runtime
    participant L as Lifetime
    participant W as Workspace
    participant P as Persistence
    S->>I: notification
    I->>I: frame·version·Envelope 검사
    I->>R: Submit(DecodedMessage)
    R->>L: 원본 소유권 인계 / root 확보
    R->>R: FIFO 대기열 → 대상 목록
    loop 중복을 제거한 대상 순서
        R->>W: ProcessAsync(context)
        W->>L: Checkout
        W->>W: payload 해석·값 복사
        W->>L: Checkin
        W->>P: AppendAsync(record)
        P-->>W: flush 완료
        R->>L: 반환·실패 시 미반납 lease 정리
    end
    R->>L: root 반환 / 마지막 참조이면 회수
```

없는 대상과 실패한 Workspace는 진단하고 다음 대상으로 진행한다. 앞 Workspace가 Checkin해도 root가 뒤 대상을 위해 원본을 유지한다. Source에 처리 결과를 응답하지 않는다. queue/memory 초과는 Submit 단계에서 신규 입력을 거부한다.

### 조회와 종료

조회는 사용자 식별 → Workspace scope 확인 → View 정의/field 검증 → Persistence Query → field 투영 순서다. 인증 없는 로컬 모드 외에는 `/health`를 포함한 HTTP 요청에 토큰이 필요하다.

```mermaid
flowchart LR
    A["RPC 접수·session 종료"] --> B["HTTP 중단<br/>Admin timer 중단"]
    B --> C["Runtime drain"]
    C --> Q{"제한 시간 내 완료?"}
    Q -->|"예"| F["최종 Admin 저장<br/>저장소·Host 닫기"]
    Q -->|"아니오"| D["대기분 폐기<br/>active에 취소 요청"]
    D --> E["active 실제 반환 대기"]
    E --> F
```

원본 안전성을 위해 active 호출의 강제 회수는 하지 않는다. 시작은 설정 검증·저장 복구·plugin 등록 후 Runtime/HTTP/RPC를 열며, 준비 중 실패하면 확보 자원을 정리한다.

## Deployment View

아래는 목표 Linux 배포다. 실선은 네트워크 또는 파일 접근이다. Termux 검증에서는 컨테이너 없이 같은 DLL을 직접 실행했다.

```mermaid
flowchart LR
    U["원격 사용자"] -->|"HTTPS"| T["TLS reverse proxy<br/>배치 시 별도 구성"]
    subgraph H["Linux host"]
        S["Source / 테스트 CLI"] -->|"host 7070"| D["Docker: Dsn.Host<br/>RPC 7070 / HTTP 7071"]
        T -->|"host 7071"| D
        D --> F["dsn-data volume<br/>records.ndjson / views.json"]
        C["설정·plugin 파일"] --> D
    end
```

[Dockerfile](../Dockerfile)은 .NET SDK로 Host/Mock을 publish하고 ASP.NET runtime 이미지에 배치한다. [Compose](../compose.yaml)는 호스트 loopback에만 포트를 공개한다. 컨테이너 설정은 `bind=0.0.0.0`, `dataDirectory=/data`, 사용자 토큰을 지정한다. 추가 plugin은 경로 설정과 파일 배치가 필요하다. reverse proxy는 Compose에 포함돼 있지 않다.

## Design Decision

| ID | 결정과 근거 | 대안·trade-off | 관련 driver |
| --- | --- | --- | --- |
| D1 | TCP JSON notification: 언어 독립 fixture와 Mock을 단순하게 공유 | gRPC 등 대신 framing/session을 직접 관리. 완전한 JSON-RPC 아님 | UC1/4, C2 |
| D2 | 공통 계층의 opaque payload, 버전 decoder와 plugin 분리 | 업무 스키마를 중앙에서 검증하지 못함 | QA7, C3/6 |
| D3 | 단일 FIFO·순차 실행: 순서와 수명 추적을 단순화 | 병렬 실행 대비 느린 plugin/flush의 지연 전파 | QA1/2, C5 |
| D4 | root와 lease, 유효성 검사 façade: 원본 공유와 반납 후 접근 통제 | Workspace별 복사 대비 수명 관리 비용. 실제 GC 시점은 별개 | QA2/5, C4 |
| D5 | BCL 기반 append journal: 추가 DB 의존성 없이 flush·재시작 검증 | DB 대비 조회/보존 기능 제한, 동기 flush·메모리 index 비용 | QA4, C1/7 |
| D6 | ErrorSink와 Admin 변환 분리: 오류 접수가 저장 완료에 의존하지 않음 | snapshot 이력 중복·저장 quota 소모, 완전한 오류 보존 불가 | QA8, UC3 |
| D7 | record 투영과 사용자별 정의: 저장 모델에서 조회를 분리 | join·실시간 Workspace 상태·Source별 ACL 없음 | QA6, C7 |
| D8 | 협력 취소 후 active 반환 대기: 사용 중 원본의 조기 회수 방지 | 강제 종료 대비 종료시간 상한 없음 | QA5, C4 |

선택의 타당성과 잔여 위험은 [Architecture Evaluation](06-architecture-evaluation.md)에서 위 driver에 연결해 평가한다.
