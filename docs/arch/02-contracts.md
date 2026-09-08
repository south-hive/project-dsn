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

RPC Server Adapter는 RPC 요청 수신·session·호출 종료를 담당한다. Source 내부 구현은 외부 책임이다. 버전 선택기는 최소 공통 헤더에서 version을 읽어 해당 Decoder를 선택한다. 최소 공통 헤더의 바이너리 규약은 미정이다. 버전별 Decoder는 공통 메시지 표현을 제공하며 envelope 구조, payload 경계 및 길이 같은 구조적 조건을 확인한다. payload 내용의 스키마 검증이나 의미 해석은 하지 않는다. 빈 payload 허용 여부, 크기 제한, 문자열 인코딩, 시간 표현, framing은 별도 확정이 필요하다.

Source의 시계가 서로 다를 수 있다. 생성 시각으로 전역 순서를 보장하거나, 시계 기준 확인 없이 Source 생성 시각과 DSN 처리 시각의 차이를 정확한 전송 지연으로 간주하지 않는다.

## 인터페이스 책임

아래는 언어 중립적인 역할 계약이다. Source에 C# 인터페이스를 강제하지 않는다. 신규 이름은 제안이며, 실제 메서드 시그니처와 ABI는 미정이다.

| 계약 | 소유 영역 | 범위와 책임 |
| --- | --- | --- |
| Source 내부 API — 외부 | Source 담당 | IMessageBuilder/IEventPublisher는 개념적 역할이며 DSN 제공 SDK 계약이 아님 |
| RPC 연동 계약 — 이름 미정 | Source / Ingress | 외부 공개 서비스·요청·호환성 계약; 세부 RPC 방식 미정 |
| `ISink`, `IMessageDecoder` | Ingress | 내부 입력 및 버전별 decoder 확장점; Workspace에는 노출하지 않음 |
| `ISignalBufferWriter`, `ISignalBufferReader` | Queue | DSN 내부 보유권 인계와 FIFO 소비; 외부 플러그인의 직접 접근 없음 |
| `IQueuePolicy` — 제안 이름 | Queue | 내부 정책 확장점; 기본 `no_policy` |
| `IBulletinBoard`, `IDispatcher` | Bulletin Board | 내부 registry·라우팅 계약; 등록 변경은 시작 시 로딩 경로로 제한 |
| Workspace 실행 계약 — 이름 미정 | 실행 구성 요소 | 내부 실행 방식 교체 지점; 순차 호출 및 실패 처리 |
| Message Lifetime 계약 — 이름 미정 | Message Lifetime | 원본 할당·회수는 내부 전용; 메시지별 Checkout·Checkin은 제한된 참조 API로 제공 |
| `IPayloadLease` | Message Lifetime / 플러그인 공통 계약 | Checkout으로 획득하는 읽기 전용 참조; Checkin 후 사용 금지 |
| `IWorkspace` | Workspace | 공개 플러그인 계약; 등록 이름, payload 처리, record 생성 |
| `IErrorSink` | 오류 접수·집계 | 공통 오류 접수 계약; 집계 구현은 Workspace 외부에 위치 |
| Admin 전달 계약 — 미정 | 오류 접수·집계 / Admin Space | 관리용 데이터 전달; 상세 envelope·payload 형식 미정 |
| `IRecordStore` | Persistence | Workspace에 공개하는 record 저장 계약 |
| `IRecordExporter` | Persistence | export 소비자용 계약; Workspace 처리 API와 분리 |
| Record 조회 계약 — 이름 미정 | Persistence | View가 사용하는 저장 record 조회 계약 |

Workspace 공개 표면은 등록·처리 계약, 읽기 전용 메시지와 Checkout·Checkin, record 저장 및 필요한 진단 계약으로 제한한다. 버퍼, dispatcher, 다른 Workspace의 인스턴스, 원본 할당·강제 회수 API는 공개하지 않는다. View는 Record 조회 계약을 사용하며 Workspace를 직접 조회하지 않는다. 구체적인 언어 접근 제한자와 패키지 배치는 미정이다.

## 순서와 Queue Policy — 확정

정책 인터페이스를 두고 `no_policy`를 기본으로 사용한다. `no_policy`는 큐가 제공하는 FIFO를 그대로 사용하며 timestamp 정렬이나 우선순위 재배치를 하지 않는다. 재정렬이 필요해지면 queue policy로 추가한다.

FIFO는 해당 큐에 적재된 순서를 뜻한다. 여러 Source 또는 여러 큐 사이의 실제 발행 시각 순서를 보장하지 않는다. 복수 큐의 병합 순서는 미정이다. FIFO 전달이 Workspace의 record 저장 완료 순서까지 자동으로 보장하는 것도 아니다. 실제 병렬 실행을 채택한다면 완료 순서 요구를 별도로 확인한다.

## 공유 메시지와 ref count — 확정

DSN 내부에서 메시지 원본 하나를 여러 Workspace가 읽기 전용으로 참조한다. Workspace 수에 비례하여 메시지 본문을 복사하지 않는다. ref count가 0이 되면 원본을 해제하거나 풀에 반환한다. Workspace는 파싱 후 필요한 데이터를 복사하여 record 등으로 보관할 수 있다.

이 no-copy 계약은 DSN 내부의 Workspace 분배에 관한 것이다. 전송, decoding, Source 탄창 적재까지 모두 zero-copy라고 가정하지 않는다.

### Checkout / Checkin 계약 — 확정

Message Lifetime은 원본 버퍼와 ref count를 소유한다. Checkout은 읽기 전용 참조를 획득하고 count를 증가시킨다. Checkin은 해당 참조를 반납하고 count를 감소시킨다. 0이 되면 원본을 회수한다. Workspace는 필요한 데이터를 복사한 직후 Checkin하며, 저장 대기 동안 원본을 보유하지 않는 것을 기본으로 한다. record는 반납한 원본이나 그 slice에 의존하지 않아야 한다.

### 참조 인계 절차 — 구현 제안

1. Ingress는 Message Lifetime에서 수신 원본에 대한 소유 참조를 확보한다.
2. 큐 적재 성공 시 참조를 인계한다. 인계는 소유자 변경이며 새로운 Checkout이 아니다. 적재 실패 시 Checkin한다.
3. 실행 루프는 큐 참조를 인계받아 전체 대상 호출이 끝날 때까지 유지한다. 순차 호출 중 첫 Workspace가 반납해도 뒤의 대상이 읽을 원본이 회수되지 않게 한다.
4. 각 Workspace 호출에는 해당 메시지만 Checkout할 수 있는 처리 문맥을 제공한다. Workspace는 Checkout하고 파싱·복사 후 명시적으로 Checkin한다.
5. 실행 구성 요소는 호출 종료·실패 시 해당 호출에 남은 참조의 반납을 보장한다. Workspace의 정상 Checkin과 종료 시 정리가 같은 참조를 중복 감소시키지 않도록 소유권 상태를 추적한다. 구체 API는 미정이다.
6. 전체 대상 호출 종료 시 실행 루프가 보유한 참조를 Checkin한다. 마지막 참조 반납 시 Message Lifetime이 원본을 회수한다.

기본 순차 실행에서 호출 종료 후 메시지를 사용하는 백그라운드 작업은 허용하지 않는다. 별도 작업에는 복사된 record를 전달한다. 향후 메시지 참조의 비동기 인계가 필요하면 소유권 인계 계약을 별도로 정의한다.

실제 동시 접근 가능성에 맞게 카운트 연산을 동기화한다. 중복 반환으로 카운트가 감소하거나 원본이 두 번 회수되지 않도록 구현한다. Workspace가 원본을 계속 사용하는 동안 timeout만으로 메모리를 강제 재사용해서는 안 된다. 장기 보유 감지 및 대응은 운영 정책으로 남긴다.

## 오류와 관측

### 확정된 동작

- 버전 오류, 잘못된 envelope, 수용 한계 초과 등 DSN에서 관측한 오류는 IErrorSink를 통해 Admin Space에 남긴다.
- 시스템 다운에 가까운 심각한 현상이 특정 Source에 연관되면 **해당 Source의 기존 연결만 끊는다**.
- 신규 연결은 허용하고, 연결 후 같은 현상이 발생하면 다시 해당 연결을 끊는다. 영구 차단 목록을 기본으로 두지 않는다.
- 동일 Error Event는 발생 시각을 제외한 모든 내용이 같을 때 합친다. 최초 발생 시각, 최근 발생 시각, count를 유지한다.
- Source 호출자에게 실패를 전파하거나 수신 확인을 기다리게 하지 않는다.

### 오류 접수·집계와 Admin Space 경계 — 확정

IErrorSink는 별도 오류 접수·집계 구성 요소가 구현한다. Admin Space는 집계·운영 정보를 관리용 record로 변환한다. 집계 상태는 오류 구성 요소가 소유하며 Admin Space의 cache가 아니다. Persistence는 record 저장·조회·export를 제공하고 View는 사용자별 field 선택·조합을 정의한다.

### 오류 집계 표현 제안

오류의 본문과 집계 메타데이터를 분리한다. 동일성은 오류 본문의 전체 필드 값으로 판정한다. `first_seen`, `last_seen`, `count`는 집계 결과이므로 동일성 비교에 포함하지 않는다. 본문에 source_id가 있다면 다른 source_id는 다른 이벤트로 취급한다. 본문에 발생 시각 외의 값이 달라지면 별도 이벤트다.

오류 접수·집계 경로는 Workspace 실행 경로와 분리한다. 오류 처리의 재귀적 실패를 방지해야 하며, 구체 fallback은 미정이다. 다만 완전한 자원 고갈이나 프로세스 중단 시 오류 기록까지 반드시 성공한다는 보장은 없다. 집계 저장 한도, 보존 기간, 최종 기록 실패 시 fallback은 미정이다.

### 운영으로 미룬 사항

심각도 판정, Source별 수신량 경고 임계값, 시간 단위, 선택적 폐기, 연결 해제 후 재접속 간격은 추후 결정한다. DSN 수신 건수는 Source의 실제 발행 건수와 다를 수 있으므로 초기 지표에서는 측정 지점을 명시한다. Source의 전송 전 손실까지 알아야 한다면 별도 계측 계약이 필요하다.

### Admin Workspace 구현 시 결정할 사항

- 관리 이벤트의 envelope·payload 형식과 Bulletin Board 경유 여부.
- 원본 envelope와 payload의 첨부 여부 및 표현 방식.
- 원본 첨부 시 복사 또는 참조 보유 방식과 수명.
- 첨부 원본의 동일 오류 비교 포함 여부와 대표 사례 보존 방식.
- 집계 결과의 전달 시점, 갱신 방식 및 Admin 처리 실패 시 동작.

위 항목은 현재 메시지 계약에 포함하지 않는다. 기존 오류 본문의 시각 제외 동일성 규칙은 유지한다.
