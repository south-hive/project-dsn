# C# 구현 계약 v1

2026-09-08. 기존 설계 D01–26을 구현하는 초기 결정이다. 설계 문서에서 미정으로 남겼던 API/설정의 현재 구현 기준은 이 문서와 컴파일 가능한 코드다. Source는 테스트 클라이언트만 제공한다.

## RPC — dsn-rpc/1

TCP UTF-8 JSON + LF framing. JSON-RPC 2.0의 `dsn.publish` notification 부분집합을 사용한다. application 응답은 없고 batch/request/id는 거부한다. 업무 처리·저장 ACK/NACK, 자동 재전송, 전달 보장은 없다. complete JSON-RPC 서버라고 주장하지 않는다. RPC 입력 예제:

```json
{"jsonrpc":"2.0","method":"dsn.publish","params":{"version":1,"source_id":"sample","time":"2026-09-08T00:00:00Z","event_type":"normal","workspace":["echo","hex"],"payload":"aGVsbG8="}}
```

| 항목 | 규격 |
| --- | --- |
| 공통 헤더 | `params.version` 정수, 등록 decoder 선택. v1 기본 |
| frame | 기본 LF 제외 65,536 bytes, JSON depth 32 |
| source_id | 공백만 아닌 string, 최대 128 UTF-16 units |
| source_description | 임의 JSON 값, 생략 가능; 전체 frame 한도 적용 |
| time | timezone 있는 ISO 8601 날짜시각, 소수 초 최대 7자리, 최대 40 units, 실제 달력 검증 |
| event_type | 공백만 아닌 string, 최대 64 units. 우선순위 없음 |
| workspace | 1–32개, 이름 `[a-z][a-z0-9-]{0,63}`. 중복은 첫 한 번 |
| payload | canonical base64, 기본 decode 후 최대 16,384 bytes, 빈 값 허용 |
| 추가 필드 | 무시 |

첫 정상 envelope로 TCP session에 source_id를 묶는다. 한 session에 한 Source, Source당 활성 session 하나다. 중복 접속은 신규 session만 닫고 기존 session을 유지한다. Source가 detach되면 동일 ID로 재접속할 수 있다. version/envelope 오류는 폐기·집계하고 연결 유지. 잘못된 RPC/framing·Source 변경은 해당 연결 종료. 영구 ban은 없다. `DisconnectSource`가 특정 Source의 현재 연결만 닫는 제어 경계다. 자동 심각도 판정·운영 임계값은 후속 범위다.

기존 `dsn-practice-rpc/1` 정상 fixture와 호환되지만 C# v1은 엄격한 ISO 시각, 임의 JSON description을 선택했다. 프로토타입의 느슨한 날짜 처리까지 호환한다고 주장하지 않는다. Source 설명을 payload 스키마로 해석하지 않는다.

## Runtime 및 공개 SDK

`Dsn.Contracts`가 Workspace 유일 공통 SDK다. `IMessageContext.Checkout()`으로 lease를 얻고 필요한 값을 복사한 뒤 `Checkin(lease)`한다. 중복 Checkin은 false이고 count를 다시 줄이지 않는다. 다른 context의 lease는 ArgumentException. 반납한 lease/facade 또는 종료된 context의 Checkout은 ObjectDisposedException이다. 호출 반환·실패 시 미반납 lease를 자동 정리한다. 처리 후 background task에는 원본 대신 복사한 값만 전달한다.

Ingress가 디코딩한 byte[]를 내부 소유권으로 인계한다. Submit 이후 호출자는 이를 수정하지 않는다. queue/root는 전체 대상 처리까지 원본을 유지한다. Workspace 분배 시 payload 복제 없음. 공개 payload façade는 원본 Span/Memory를 반환하지 않으며 읽기 호출과 Checkin을 동기화한다. Copy/문자열 변환은 명시적 복사다. 참조 0이면 내부 byte[] 참조를 해제하며 실제 GC 반환 시각은 보장하지 않는다.

FIFO는 단일 queue에 수용된 순서다. `IQueuePolicy`/`NoPolicy`와 `IWorkspaceExecutor`/`SequentialExecutor`를 별도 내부 교체 지점으로 둔다. Source timestamp 재정렬 없음. 같은 메시지 내 순차 대상 처리, Workspace 실패 후 다음 대상 계속. 큐/원본 수용 초과 시 새 메시지 폐기와 오류 집계.

## 저장과 View

`MemoryRecordStore`는 동일 query/scalar 계약을 가진 비영속 테스트 대역이고, `JournalStore`는 Host의 실제 영속 adapter다.

Record field는 string/JSON number/bool/null. field 1–128개, 이름 `[a-z][a-z0-9_]{0,63}`. `id`, `workspace`는 저장소 예약 column. record당 최대 1 MiB. 값은 저장 시 복사되고 lease와 무관하다. 저장소가 증가하는 long id를 부여한다. 동일 input의 반복 Append는 별도 record이며 exactly-once나 dedup 의미가 없다. Append는 시작 전 취소만 검사하며 flush 중간 취소는 적용하지 않는다.

저장 성공 후 Query에 즉시 보이고 정상 종료/재시작 및 완료된 flush를 지원한다. 미완성 마지막 frame을 복구한다. storage I/O 실패는 해당 실행 호출 실패로 집계하며 모호한 쓰기를 재시도하지 않는다. 내부 I/O 실패 후 journal은 재시작 전까지 쓰기를 거부한다. 용량 초과는 I/O fault와 달리 단순 거부다. journal 전체를 메모리에 인덱싱하므로 대규모 장기 보관 DB 대체로 평가하지 않았다. 최대 100,000 records / 256 MiB 기본값은 RSS 상한이나 운영 권장값이 아니다.

View는 `IRecordQuery`만 소비한다. 여러 Workspace record를 하나의 선택 column 집합으로 투영하는 union 형식이다. cross-record join은 하지 않는다. `message_id`는 하나의 수신 원본에서 파생된 record 상관관계 키다. 누락 field는 null, 알려지지 않은 field는 오류, `id` ascending / `afterId` exclusive / limit 1–1000. field 목록은 현재 저장된 record에서 얻으므로 최초 record가 없으면 payload field 정의를 아직 만들 수 없다. 정의당 Workspace/field 최대 32, 사용자별 최대 128 definitions, 정의 파일 최대 4 MiB.

## 오류와 Admin

`ErrorBody(Code, Component, SourceId?, Workspace?, Detail?)` 전체 record 값 동등성이 집계 키다. 발생시각은 키가 아니다. 첫/최근 UTC시각 및 long count. 최대 1,024종 기본, 기존 종류는 계속 갱신하고 신규 종류는 dropped를 증가시킨다. payload 원본을 오류 본문에 첨부하지 않는다.

Admin은 독립 집계기의 snapshot을 1초마다 revision이 바뀐 경우 `admin` record로 투영한다. 원본 snapshot의 first_seen/last_seen/count와 snapshot_revision을 저장한다. 과거 snapshot도 append 이력으로 남는다. 저장 실패는 고정 오류 본문으로 집계하여 재귀 기록을 피한다. 실시간 보장이나 상세 monitoring 서비스는 아니다.

## 설정·plugin·lifecycle

JSON 설정 파일 하나 또는 코드 기본값을 사용한다. 환경변수/여러 파일 병합 우선순위는 없다. 알 수 없는 키, 잘못된 타입/범위는 시작 전 실패. 샘플에 모든 키/기본값을 명시했다. config/plugin/data 상대 경로는 cwd 기준이다. maxFrame 4 MiB 이하, PayloadBytes는 0–FrameBytes. 주요 count/byte 한도는 양수. 모든 plugin은 필수, 버전 1 factory가 필요하고 시작 실패 시 포트/저장소 rollback. AssemblyDependencyResolver로 plugin 의존성 해석, SDK 계약 assembly 공유. hot reload/security sandbox 없음.

Source 수신 no-blocking 보장은 Source 내부 구현 책임이다. 서버의 notification 무응답과 테스트 Source의 await write는 각각 다른 계약이다. Termux Mono와 Linux CoreCLR 성능, Docker 실행, 실제 C++/kernel 연동 및 후속 QA는 별도 평가 대상으로 남긴다.
