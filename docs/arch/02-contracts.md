# 계약과 메시지 수명

## Envelope — 논리 필드 확정, wire format 미정

| 필드 | 의미 |
| --- | --- |
| `version` | 숫자형 envelope 버전 |
| `source_id` | Source가 정하는 식별자 |
| `source_description` | 선택적 설명, 내용 형식은 지정하지 않음 |
| `time` | Source의 이벤트 생성 시각 |
| `event_type` | normal, urgent 등의 구분; 현재 우선순위 등 특별한 처리 의미 없음 |
| `workspace[]` | 수신할 Workspace의 등록 이름 배열 |
| `payload` | Source와 Workspace 사이에서만 형식과 의미를 아는 불투명 바이트 |

Ingress는 버전과 envelope 구조, payload 경계 및 길이 같은 구조적 조건을 확인한다. payload 내용의 스키마 검증이나 의미 해석은 하지 않는다. 빈 payload 허용 여부, 크기 제한, 문자열 인코딩, 시간 표현, framing은 별도 확정이 필요하다.

Source의 시계가 서로 다를 수 있다. 생성 시각으로 전역 순서를 보장하거나, 시계 기준 확인 없이 Source 생성 시각과 DSN 처리 시각의 차이를 정확한 전송 지연으로 간주하지 않는다.

## 인터페이스 책임

아래는 언어 중립적인 역할 계약이다. Source에 C# 인터페이스를 강제하지 않는다. 신규 이름은 제안이며, 실제 메서드 시그니처와 ABI는 미정이다.

| 인터페이스 | 소유 영역 | 역할과 제약 |
| --- | --- | --- |
| `IMessageBuilder` | Source | 메시지 구성; 데이터 준비와 발행 호출의 비용 경계는 미정 |
| `IEventPublisher` | Source | 탄창 적재로 발행 종료; no blocking / no error / no exception |
| `ISink` | Ingress | 전송 경계의 메시지 입력 |
| `IMessageDecoder` | Ingress | envelope 버전별 구조 해석과 검증 |
| `ISignalBufferWriter` | Ingress | 메시지 보유권을 버퍼에 인계; 수용 실패 처리 |
| `ISignalBufferReader` | Ingress / Bulletin Board | 버퍼에서 메시지를 가져오는 읽기 계약 |
| `IQueuePolicy` — 신규 제안 이름 | Queue | 정책 교체 지점; 기본 구현 이름은 `no_policy` |
| `IBulletinBoard` | Bulletin Board | 등록 및 분배 기능의 Facade |
| `IDispatcher` | Bulletin Board | 이름으로 대상을 찾고 공유 메시지 참조 전달 |
| `IWorkspace` | Workspace | 등록 이름 제공, payload 해석, record 생성 |
| `IPayloadLease` | 공통 계약 / Bulletin Board | 읽기 전용 메시지 참조의 유효 수명과 반환 |
| `IErrorSink` | 공통 계약 / Admin Space | 내부 오류 접수와 동일 이벤트 집계 경로 |
| `IAdminSpace` | Admin Space | 오류 및 운영 정보 제공 |
| `IRecordStore` | Persistence | Workspace가 생성한 record 저장 |
| `IRecordExporter` | Persistence | 저장된 record export |
| View 조회 계약 — 이름 미정 | Persistence / View | 여러 Workspace의 record field 조회 및 조합 |

## 순서와 Queue Policy — 확정

정책 인터페이스를 두고 `no_policy`를 기본으로 사용한다. `no_policy`는 큐가 제공하는 FIFO를 그대로 사용하며 timestamp 정렬이나 우선순위 재배치를 하지 않는다. 재정렬이 필요해지면 queue policy로 추가한다.

FIFO는 해당 큐에 적재된 순서를 뜻한다. 여러 Source 또는 여러 큐 사이의 실제 발행 시각 순서를 보장하지 않는다. 복수 큐의 병합 순서는 미정이다. FIFO 전달이 Workspace의 record 저장 완료 순서까지 자동으로 보장하는 것도 아니다. 실제 병렬 실행을 채택한다면 완료 순서 요구를 별도로 확인한다.

## 공유 메시지와 ref count — 확정

DSN 내부에서 메시지 원본 하나를 여러 Workspace가 읽기 전용으로 참조한다. Workspace 수에 비례하여 메시지 본문을 복사하지 않는다. ref count가 0이 되면 원본을 해제하거나 풀에 반환한다. Workspace는 파싱 후 필요한 데이터를 복사하여 record 등으로 보관할 수 있다.

이 no-copy 계약은 DSN 내부의 Workspace 분배에 관한 것이다. 전송, decoding, Source 탄창 적재까지 모두 zero-copy라고 가정하지 않는다.

### 수명 관리 제안

1. Ingress가 수신 원본을 소유하며 초기 참조 하나를 가진다.
2. 버퍼 적재에 성공하면 그 참조의 소유권을 버퍼에 이전한다. 실패하면 참조를 반환한다.
3. Dispatcher는 버퍼에서 인계받은 참조를 유지한 채 수신 대상별 lease를 획득한다. Workspace에 노출하기 전에 ref count를 증가시킨다.
4. 대상이 메시지를 받아들이지 못하면 그 대상의 lease를 즉시 반환한다. 전달을 마치면 dispatcher 자신의 참조도 반환한다.
5. 각 Workspace는 메시지 사용이 끝났을 때 자신의 lease를 반환한다. 파싱 실패 등 종료 경로에서도 반환을 보장한다.
6. 마지막 참조 반환 시 원본을 회수한다. lease 반환 후 원본이나 원본을 가리키는 slice를 사용하지 않는다.

실제 동시 접근 가능성에 맞게 카운트 연산을 동기화한다. 중복 반환으로 카운트가 감소하거나 원본이 두 번 회수되지 않도록 구현한다. Workspace가 원본을 계속 사용하는 동안 timeout만으로 메모리를 강제 재사용해서는 안 된다. 장기 보유 감지 및 대응은 운영 정책으로 남긴다.

## 오류와 관측

### 확정된 동작

- 버전 오류, 잘못된 envelope, 수용 한계 초과 등 DSN에서 관측한 오류는 IErrorSink를 통해 Admin Space에 남긴다.
- 시스템 다운에 가까운 심각한 현상이 특정 Source에 연관되면 **해당 Source의 기존 연결만 끊는다**.
- 신규 연결은 허용하고, 연결 후 같은 현상이 발생하면 다시 해당 연결을 끊는다. 영구 차단 목록을 기본으로 두지 않는다.
- 동일 Error Event는 발생 시각을 제외한 모든 내용이 같을 때 합친다. 최초 발생 시각, 최근 발생 시각, count를 유지한다.
- Source 호출자에게 실패를 전파하거나 수신 확인을 기다리게 하지 않는다.

### 오류 집계 표현 제안

오류의 본문과 집계 메타데이터를 분리한다. 동일성은 오류 본문의 전체 필드 값으로 판정한다. `first_seen`, `last_seen`, `count`는 집계 결과이므로 동일성 비교에 포함하지 않는다. 본문에 source_id가 있다면 다른 source_id는 다른 이벤트로 취급한다. 본문에 발생 시각 외의 값이 달라지면 별도 이벤트다.

IErrorSink가 포화된 데이터 큐로 오류를 다시 넣어 재귀적으로 실패하지 않도록 독립적인 접수·집계 경로를 두는 것을 제안한다. 다만 완전한 자원 고갈이나 프로세스 중단 시 오류 기록까지 반드시 성공한다는 보장은 없다. 집계 저장 한도, 보존 기간, 최종 기록 실패 시 fallback은 미정이다.

### 운영으로 미룬 사항

심각도 판정, Source별 수신량 경고 임계값, 시간 단위, 선택적 폐기, 연결 해제 후 재접속 간격은 추후 결정한다. DSN 수신 건수는 Source의 실제 발행 건수와 다를 수 있으므로 초기 지표에서는 측정 지점을 명시한다. Source의 전송 전 손실까지 알아야 한다면 별도 계측 계약이 필요하다.
