# practice RPC 계약

계약 식별자: `dsn-practice-rpc/1`. Production RPC/IDL 결정과 독립적인 실험 규약이다.

## 전송과 호출

TCP byte stream에 UTF-8 JSON 한 개와 LF(`0x0a`)를 보낸다. JSON 문자열 내부의 개행은 escape한다. 분할 수신과 한 번에 여러 frame 수신을 지원한다. 최대 frame은 LF 제외 65,536 bytes다.

```json
{"jsonrpc":"2.0","method":"dsn.publish","params":{"version":1,"source_id":"sample","time":"2026-09-08T00:00:00.000Z","event_type":"normal","workspace":["echo","hex"],"payload":"aGVsbG8="}}
```

`id`가 없는 notification 형식을 사용하고 application 응답을 쓰지 않는다. 적재·처리·저장 ACK/NACK, 재전송, 전달 보장은 없다. 종료·전송 실패는 socket 상태로 관찰될 수 있다. Host의 `Drain()`은 테스트용이며 RPC 메서드가 아니다.

[JSON-RPC 2.0 명세](https://www.jsonrpc.org/specification)의 notification 표현을 이용하지만 **완전한 JSON-RPC 2.0 서버는 아니다**. 일반 request, batch, parse error 응답 규약은 제공하지 않는다. 허용하지 않는 RPC 입력은 오류를 집계하고 해당 연결을 닫는다. 정식 연동에는 이 제한을 명시한 계약이 필요하다.

## Envelope v1

| 필드 | practice 제약 |
| --- | --- |
| version | 등록된 정수 버전, 기본 decoder는 1 |
| source_id | 비어 있지 않은 string, 최대 128 UTF-16 code units |
| source_description | 선택 string, 최대 512 code units; production의 자유 형식을 확정하지 않음 |
| time | 최대 40 code units; 날짜·T 접두사와 Date.parse 가능한 문자열 검사; 엄격한 달력/시각 동기화 검증 없음 |
| event_type | 비어 있지 않은 string, 최대 64 code units; 우선순위 의미 없음 |
| workspace | 이름 1~32개, 각 이름 `[a-z][a-z0-9-]{0,63}` |
| payload | canonical base64, 빈 값 허용, decode 후 최대 16,384 bytes |

추가 필드는 무시한다. Core는 payload 업무 스키마를 검사하지 않는다. 중복 목적지는 첫 등장 한 번만 처리하고 없는 목적지는 오류를 남기며 유효한 목적지는 계속 호출한다. 여러 Source 사이에서는 서버 큐 적재 순서를 사용한다. Source time으로 정렬하지 않는다.

`maxPayloadBytes` 설정은 0 이상의 안전한 정수만 허용한다. 0은 빈 payload만 허용한다. NaN·Infinity·음수·소수는 decoder 생성 시 거부하며 수신 경로에 진입하지 않는다.

## Session과 오류

TCP 연결이 attach이며 첫 정상 envelope가 source_id를 연결에 묶는다. 연결당 하나의 source_id, source_id당 하나의 활성 연결을 허용하는 practice 선택이다. 별도 attach RPC는 없다. FIN/reset/서버 종료가 detach다. 기존 연결이 끊어지면 같은 source_id의 새 연결을 허용한다.

| 상황 | 수신 동작 |
| --- | --- |
| 정상 notification | 수용 가능한 경우 FIFO로 인계, 응답 없음 |
| 미지원 version / envelope 오류 | 오류 접수·메시지 폐기, 연결 유지 |
| 잘못된 JSON/UTF-8, id 포함, batch, 다른 method | INVALID_RPC, 해당 연결 종료 |
| frame 초과 | FRAME_TOO_LARGE, 해당 연결 종료 |
| 연결 중 source_id 변경 | SOURCE_CHANGED, 해당 연결 종료 |
| 이미 연결된 source_id로 새 session 발행 | 새 연결 종료, 기존 연결 유지 |
| 큐·원본 한도 초과 | QUEUE_FULL/MEMORY_FULL, 새 메시지 폐기, 연결 유지 |
| session 한도 초과 | 새 연결 종료 |
| Workspace 실패 | 오류 접수, 종료된 호출의 lease 정리, 다음 목적지 계속 실행 |

연결 종료 조건은 시험 가능한 제한 정책이며 production의 “시스템 다운에 가까운 오류” 분류를 확정하지 않는다. source_id를 검증하기 전 오류는 source_id 없이 기록될 수 있다. ErrorSink도 포화되면 새로운 종류의 오류가 유실될 수 있다.

Node의 TCP socket은 byte stream이고 write backpressure가 존재한다. 서버가 업무 ACK를 보내지 않는 것과 Source 호출의 비대기 보장은 구분해야 한다. [Node net API](https://nodejs.org/api/net.html)를 참고한다.

## Fixture와 Mock

[client.ts](../src/client.ts)의 `notification()`이 정상 fixture를 생성한다. [verification.test.ts](../test/verification.test.ts)는 버전·형식·길이 오류를, [validation.test.ts](../test/validation.test.ts)는 실제 TCP 분할/병합·포화·detach/reconnect를 재현한다.

Mock은 같은 RpcServer와 Decoder를 사용한다. envelope, 원시 payload의 앞 256 bytes hex, 전체 byte 수, truncated 여부를 console JSON으로 출력한다. 업무 payload 해석은 하지 않는다. DSN의 echo/hex Workspace는 변환 예제이며 Core의 payload 계약이 아니다.
