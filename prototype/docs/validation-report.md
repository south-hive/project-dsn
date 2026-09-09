# practice Verification / Validation 결과

이 문서는 최초 32건 검증의 과거 기록이다. 추가 경계·복구 시험 및 결함 수정 이후 결과는 [tests 최신 보고서](../../tests/report.md)를 기준으로 확인한다.

실행일: 2026-09-08. 대상: `practice` 브랜치의 `prototype/`. 기능 경계 참조(현재 문서): [계약](../../docs/05-component-design.md), [Source 경계](../../docs/02-system-overview.md), [현재 평가](../../docs/06-architecture-evaluation.md). 이 문서의 상대 경로는 저장소 내 실제 파일을 가리킨다.

## 판정

**인터페이스·echo stub의 기능 검증은 통과했다.** Production 구현의 인수나 저지연 성능 적합성 판정은 아니다. Verification은 코드가 명시한 계약을 지키는지 확인하고, Validation은 사용자 요구를 대표하는 실제 연결 시나리오가 가능한지 확인했다. 실제 Source 담당자의 인수 검증은 아직 수행하지 않았다.

| 실행 | 결과 |
| --- | --- |
| `npm run build` | strict 타입 검사·컴파일 성공; 공개 API의 금지 접근 compile fixture 포함 |
| `npm run verify` | 20 / 20 통과, 실패·skip 0 |
| `npm run validate` | 12 / 12 통과, 실패·skip 0 |
| `npm run demo` | 실제 RPC 입력, 2개 Workspace record, HTTP View, 정상 종료 |
| CLI lifecycle | DSN·Mock 별도 프로세스 시작, RPC echo, SIGTERM 정상 exit 0 |

환경은 Android arm64 Termux, Node 26.4.0, npm 11.19.1, TypeScript 5.9.3이다. 테스트는 [Node test runner](https://nodejs.org/api/test.html)를 사용한다. TypeScript native compiler의 Android 실행 제약으로 JavaScript 기반 5.9.3을 고정했다.

데모 ledger의 실제 결과:

```json
{"responseBytes":0,"lifetime":{"created":1,"reclaimed":1,"liveMessages":0,"liveBytes":0,"checkouts":2,"checkins":3,"references":0}}
```

echo/hex record의 `message_id`는 모두 1이었다. Checkin 3회는 두 lease와 executor root의 반환이다. 이 결과는 해당 시나리오의 논리적 소유권을 검증하며 GC 회수·동시성·모든 입력의 무누수를 증명하지 않는다.

## Verification 추적

실행 파일: [verification.test.ts](../test/verification.test.ts), 타입 fixture: [api-surface.ts](../test/api-surface.ts).

| 시험 | 검증 내용 | 설계 검증 항목 |
| --- | --- | --- |
| V01–V04 | opaque bytes, frozen envelope, 버전 분리, base64·길이·필수 필드 | T01 |
| V05–V09 | root 인계, 입력 변경 방어, 공유 원본, 반환 후 접근 차단, 중복·외부 lease, 자원 한도 | T06–T09 |
| V10–V11 | FIFO, 적재 실패 소유권, writer 종료, 정책 오류의 원본 보존 | T05, T09, T15 |
| V12–V14 | 이름 registry, 중복·누락 대상, 순차 async 처리, 실패·미반납 정리 | T04–T08 |
| V15–V16 | 복사 record, 보존 한도, View의 조회 계약 의존·field 선택 | T16–T17 일부 |
| V17–V19 | 오류 본문 동일성, 집계 포화, 출력 포화·실패, Admin 투영 분리 | T11–T12 |
| V20 | 실제 module import, API 버전 불일치·중복 플러그인 거부 | T04 |

## Validation 추적

실행 파일: [validation.test.ts](../test/validation.test.ts). 네트워크 시나리오는 loopback 실제 TCP/HTTP를 사용한다. Source는 검증용 클라이언트다.

| 시험 | 요구 시나리오와 확인 결과 | 설계 검증 항목 |
| --- | --- | --- |
| A01 | RPC → 동적 echo/hex → record → HTTP View/export, 동일 메시지 ID, 응답 bytes 0 | T06–T07, T16–T17 일부 |
| A02 | 독립 Mock의 임의 binary echo와 원본 반환 | T03 |
| A03 | frame 분할·병합에도 큐 입력 FIFO 유지 | T01, T05 |
| A04 | 미지원 버전 두 건 집계 후 같은 연결의 정상 요청 처리, Admin record 투영 | T01, T11 |
| A05 | frame 초과 Source만 종료, 다른 Source 유지, 같은 source_id 재접속 | T13 일부 |
| A06 | 중복 session 거부, 기존 연결 유지, detach 후 재접속 | T14 |
| A07 | 느린 Workspace 중 큐 포화: 새 2건 폐기, 연결 유지, 최종 참조 0 | T09 |
| A08 | 종료 timeout: 대기 root 폐기, 활성 lease 접근 유지, 처리 종료 뒤 최종 회수 | T10, T15 |
| A09 | RPC 포트 충돌 시 시작 rollback, 기존 DSN 정상 유지 | T04, T15 일부 |
| A10 | 잘못된 View/export 요청 400 및 잘못된 경로 404 | T17 일부 |
| A11 | malformed RPC와 연결 중 source_id 변경의 session 종료 | T01, T14 |
| A12 | 실제 DSN/Mock CLI 실행·echo·SIGTERM·참조 0·exit 0 | T03, T15 |

T02의 Source 호출 경로, T16의 영속 저장·재시작 복구, T17의 실제 사용자별 권한/저장된 정의, T18의 성능 평가는 미검증이다. T13은 명시적인 frame 초과 정책만 검증했으며 일반적인 심각도 정책은 구현하지 않았다. Docker 배포 검증도 포함하지 않는다.

## 설계상 확인한 사항과 남은 위험

| 위험 | 이번 확인 | 후속 관측 아이디어 / 한계 |
| --- | --- | --- |
| RISK-02 / QA-02 | root와 lease 분리·반환 이후 접근 거부·종료 정리 통과 | C#에서는 실제 동시 접근, pool 재사용, GC와 allocation을 별도 검증 |
| RISK-03 / QA-01·02 | 느린 Workspace 중 큐 포화·폐기를 재현 | root 보유 시간과 큐 대기 측정; 순차 실행에서 느린 저장도 다음 호출을 지연시킬 수 있음 |
| RISK-04 / QA-03 | 1 source_id / 1 TCP 연결에서 해당 연결만 종료·재접속 | 실제 중계가 여러 Source를 한 연결에 실을 경우 매핑 계약 재협의 |
| RISK-05 / QA-04 | 오류 종류 한도·count 집계·비재귀 폐기 검증 | 고유 오류 수, 폐기 수 관측; Admin snapshot 투영은 실시간 전달이 아님 |
| RISK-06 / QA-05 | 비동기 미완료 호출의 원본을 timeout만으로 반환하지 않음 | 동기 무한 실행은 event loop를 막음; 프로세스 격리·종료 정책은 후속 결정 |
| RISK-07 / QA-06 | View는 record만 조회하고 선택 field를 반환 | 메모리 수용만 제공; durable commit·재시작 일관성은 미검증 |
| RISK-08 / QA-07 | decoder 교체 지점과 plugin 버전 검증 | practice wire와 production IDL/ABI는 별도; queue policy는 현재 index 선택 hook만 제공 |
| RISK-01·09 / QA-01·04 | 이번 성능 평가 없음 | Source 호출 지연·CPU·계측 비용은 1차 이후 실제 환경에서 평가 |

관측 항목은 아이디어이며 운영 수집기·대시보드·경고·자동 조치는 추가하지 않았다. 현재 C# 구현의 품질 판정은 [Architecture Evaluation](../../docs/06-architecture-evaluation.md)을 따른다. 이 프로토타입 결과만으로 production 품질을 판정하지 않는다.

## 재현

```bash
cd prototype
npm ci
npm run check
npm run demo
```

실행 파일과 test fixture는 소스에 포함된다. `node_modules/`, 컴파일 산출물 `dist/`, 원시 테스트 로그는 Git 대상에서 제외한다. `requirement/` 원본도 계속 제외하며 수정하지 않는다.
