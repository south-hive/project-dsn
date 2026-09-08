# 모듈 경계와 검증

상세 구조는 [구조 다이어그램](06-structural-views.md), 구현 작업의 선행 조건과 산출물은 [Task·Track 계획](10-implementation-plan.md)을 기준으로 한다. 아래 통합 순서는 요약이며 track별 병렬 진행을 제한하지 않는다.

## 모듈별 인터페이스

각 모듈은 제공 인터페이스와 소비 인터페이스를 통해 연결된다. 내부 구현은 모듈 경계를 넘어 직접 참조하지 않는다.

| 모듈 | 책임 및 산출물 | 제공 계약 | 사용하는 계약 |
| --- | --- | --- | --- |
| Source SDK / 환경별 adapter | C++·커널·eBPF 발행 경로와 예제 | IMessageBuilder, IEventPublisher에 해당하는 환경별 API | 공통 envelope 및 전송 규약 |
| Transport Adapter | 연결·수신·연결 종료 | 내부 전송 계약 | 입력 계약, IErrorSink |
| Envelope Decoder | 버전 선택 및 구조 검증 | 내부 IMessageDecoder 확장점 | 최소 공통 헤더, Message Lifetime, IErrorSink |
| Signal Buffer | FIFO 보유 및 인계 | 내부 ISignalBufferWriter, ISignalBufferReader | Message Lifetime, queue policy |
| Bulletin Board | registry, 전달 대상 결정 | 내부 IBulletinBoard, IDispatcher | Workspace 등록 정보, IErrorSink |
| Workspace 실행 구성 요소 | FIFO 단일 루프, 대상 순차 호출, 종료 시 참조 정리 | 내부 실행 방식 교체 계약 | buffer reader, Bulletin Board, IWorkspace, Message Lifetime, IErrorSink |
| Message Lifetime | 원본 버퍼, ref count, 회수 | 제한된 Checkout·Checkin 및 IPayloadLease; 내부 할당 API | 메모리 관리 구현 |
| Queue Policy | 기본 no_policy와 추후 정렬 정책 | queue policy 인터페이스 | 큐 항목 및 순서 계약 |
| Workspace 플러그인 | payload 해석 및 record 생성 | IWorkspace | IPayloadLease, IRecordStore |
| 오류 접수·집계 | 동일 오류와 최초·최근 시각, count 관리 | IErrorSink, Admin 전달 계약 — 미정 | 오류 모델 |
| Admin Space | 오류·운영 정보의 record 변환 | 관리용 IWorkspace | Admin 전달 계약, IRecordStore |
| Persistence | record 저장·조회·export | IRecordStore, IRecordExporter, Record 조회 계약 | 공통 record 모델 |
| View | 여러 Workspace field의 조합과 원격 조회 | 원격 View API — 미정 | Record 조회 계약 |
| DSN Mock | console echo와 Source 입력 진단 | 실제 DSN과 동일한 Source 측 입력 규약 | 공통 envelope decoder 계약 |
| DSN 실행 호스트 | 시작 시 플러그인 로딩, 모듈 조립, Docker 실행 | 시작 설정과 플러그인 발견 규약 — 미정 | 각 모듈의 공개 인터페이스 |

위 표의 세부 패키징은 제안이다. Source adapter와 Workspace 플러그인은 각 공개 계약을 구현한다. 계약 변경 시 제공자와 소비자의 호환성을 검증한다.

## 공개 범위와 의존성 규칙

- 공통 계약에는 envelope 모델, 읽기 전용 메시지와 lease, 오류 모델 및 공개 인터페이스를 둔다. 전송 구현, 저장소 SDK, 업무 payload 스키마를 넣지 않는다.
- Workspace에는 등록·처리, 제한된 메시지 Checkout·Checkin, 저장·진단 계약만 공개한다. 큐와 dispatcher, registry 변경, 원본 할당·강제 회수는 DSN 내부로 제한한다. 내부 인터페이스가 모든 플러그인에 공개되는 것은 아니다.
- View는 Persistence의 Record 조회 계약으로 접근하고 Workspace를 직접 조회하지 않는다.
- 각 모듈은 다른 모듈의 구체 클래스 대신 공개 인터페이스에 의존한다. 실행 호스트가 실제 구현을 선택하고 연결한다.
- Ingress와 Bulletin Board는 Admin Space의 구체 구현 대신 IErrorSink를 사용한다. Workspace는 특정 저장소 대신 IRecordStore를 사용한다.
- Workspace의 payload 모델은 해당 플러그인이 소유한다. 다른 Workspace의 내부 모델이나 실행 결과에 직접 의존하지 않는다.
- 인터페이스 변경 시 데이터 형식뿐 아니라 소유권, 반환 시점, 순서, 실패 및 포화 동작, 동시 호출 가능 여부를 함께 명시한다.
- C# 공통 계약과 native Source 간에는 wire format 및 전송 계약으로 호환성을 유지한다. 동일 런타임이나 바이너리 인터페이스를 강제하지 않는다.

| 개발 대상 | 전체 시스템 없이 검증하는 경계 |
| --- | --- |
| Source | DSN Mock에 실제 입력 전송 |
| Ingress | 고정 입력 바이트와 대역 ErrorSink로 검증·큐 적재 확인 |
| Bulletin Board | 등록 정보와 목적지 배열로 전달 대상 결정 확인 |
| 실행 구성 요소 / Message Lifetime | 대역 큐·Workspace로 FIFO 순차 호출, Checkout·Checkin, 실패 시 단일 반납 확인 |
| Workspace | 고정 payload와 대역 record store로 파싱 결과 확인 |
| 오류 접수·집계 | 고정 Error Event로 동일성·시각·count 확인; Admin 미가동 시 접수 경로 확인 |
| Admin Space | 전달 규약 확정 후 관리용 record 변환 확인 |
| Persistence / View | 공통 record 예제와 조회 대역으로 저장·field 조합 확인 |

이 경계 검증은 실제 Source, Docker, DSN을 연결하는 통합 검증을 보완한다.

## 통합 순서 — 제안

### 1. 최소 공통 계약

Envelope wire format, 최대 입력 크기, 첫 전송 경로, Source 탄창 소유권, Workspace 및 lease 인터페이스를 먼저 정한다. DSN 공통 계약과 환경별 Source API의 책임을 구분한다. payload의 업무 스키마를 공통 라이브러리에 넣지 않는다.

### 2. C++ Source → Mock

console echo로 실제 입력 규약을 검증한다. 수신기 미실행·종료·혼잡 상태에서 원 프로젝트가 대기하지 않는지 확인한다. Source 발행 비용과 탄창 동작을 함께 측정한다.

### 3. 실제 DSN의 최소 수직 경로

Source → Ingress → FIFO/no_policy → Bulletin Board → 실행 구성 요소 → 시작 시 로드된 예제 Workspace → record 저장 → 최소 View 조회를 연결한다. 두 Workspace가 동일 원본을 참조하는 경로와 오류 집계를 함께 검증한다.

### 4. 커널·eBPF 및 Docker 통합

지원 커널에서 발행 경로를 검증하고, 컨테이너 밖 Source와 컨테이너 안 DSN 사이의 자원 접근 및 연결 설정을 문서화한다. 정확한 mount·권한·네트워크 옵션은 전송 방식 결정 후 필요한 범위로 정한다. 원격 View 연결도 검증한다.

### 5. 부하 측정과 운영 정책 후속 결정

측정 결과를 바탕으로 용량, 스케줄러, 경고 및 심각도 기준을 정한다. 임시 기본값은 적용 범위와 선정 근거를 명시한다.

## 핵심 검증 기준

| 영역 | 검증할 동작 |
| --- | --- |
| Source 호출 | 미연결, 혼잡, 수신기 종료 시에도 연결·큐 여유·응답을 기다리지 않고 실패를 호출자에게 전파하지 않음 |
| 성능 | 발행 호출 지연 분포, CPU 부하, 처리량, 메모리 사용량, 관측 지점별 폐기 건수 기록; 수치 목표는 미정 |
| Payload 블랙박스 | 서로 다른 임의 바이너리를 공통 계층에서 의미 해석 없이 수신·분배 |
| Registry | Workspace 하나당 등록 이름 하나; 중복 이름의 후발 등록 거부와 ErrorSink 기록 |
| Queue / 실행 | 기본 no_policy에서 FIFO 소비; 단일 실행 루프에서 대상 Workspace 순차 호출 |
| 공개 경계 | Workspace의 큐·dispatcher 직접 접근 없음; View는 Record 조회 계약만 사용 |
| 공유 수명 | 두 소비자가 원본 하나를 공유; 먼저 반납한 소비자가 있어도 남은 참조 유효; 마지막 반납 때 한 번만 회수 |
| 실패 시 수명 | 적재 실패, 전달 실패, 파싱 실패 시 Checkin; 정상 반납과 종료 정리가 중복 감소하지 않음 |
| 최소 보유 | 필요한 데이터 복사 후 저장 전에 Checkin; 후속 Workspace 호출까지 실행 루프의 원본 참조 유효 |
| 오류 집계 | 시각만 다른 동일 본문은 count 증가; 본문 필드가 다르면 별도 이벤트; 최초·최근 시각 유지 |
| 심각한 오류 | 해당 Source 연결만 종료; 다른 Source 연결 유지; 신규 연결 후 재발 시 동일 처리 |
| 수용 초과 | 메시지 폐기와 관측 가능한 ErrorSink 기록; 오류 처리의 재귀적 포화 방지 |
| Mock | 실제 Source와 동일 입력 규약으로 echo; payload 스키마 불필요; console 혼잡 처리 |
| Docker / View | 같은 호스트의 Source 입력과 원격 View 조회가 배포 경계에서 동작 |

## 초기 산출물 범위

C# DSN Docker 이미지, 환경별 Source 라이브러리와 예제, DSN Mock, Workspace 공통 계약과 예제 플러그인, 기본 no_policy, 독립 오류 집계와 Admin Space, 최소 record 저장 및 View 조회 경로를 목표로 한다. 전송 방식·저장소·View API의 구체 규격은 미정이다.
