# 3. Architectural Drivers

## Use Case

| ID | 사용자·목적 | 흐름 |
| --- | --- | --- |
| UC1 | 앱 Source가 정보를 남긴다 | SDK 발행 → 접수 → 원본 보존 → Workspace → Filter → Sink |
| UC2 | 사무 PC에서 시험 데이터를 본다 | 시험 PC 웹 접속 → 권한 → field/표/현재 페이지 차트 |
| UC3 | 담당자가 사후 분석을 바꾼다 | 설정/plugin 변경·재시작 → 원본 선택 → 재처리 → 새 revision 결과 |
| UC4 | 담당자가 처리 경로를 구성한다 | Workspace별 순서 있는 Filter/Sink 선언 → 시작 시 검증 |
| UC5 | 운영자가 재시작·이전한다 | SQLite 복구 또는 legacy 초기 이전 → ID·View 유지 |

## Quality Attribute Scenario

| ID / 품질 | 자극·환경·대상 | 응답 | 판정 기준 |
| --- | --- | --- | --- |
| QA1 / 자원 | 앱이 느린 처리보다 빠르게 발행 | 유한 큐·메모리 상한, 초과 거부 | 포화/종료 시 참조 회수, 수신 성공과 보존 성공 구분 |
| QA2 / 수명 | Workspace가 lease 반납·예외·취소 | 완료 후 context 정리, 전체 dispatch 후 root 반환 | 중복 Checkin·반납 후 접근 거부·최종 참조 0 |
| QA3 / 변경 | 순서·조건·단위 변환 설정 변경 | 코드 재빌드 없이 연결 변경, 시작 시 옵션 검사 | 순서가 결과에 반영, unknown/reserved/중복 Sink 거부 |
| QA4 / 보존 | 프로세스 재시작·기존 파일 이전 | SQLite 트랜잭션, 원본/결과/View 복구 | BLOB 동일·ID 유지·이전 실패 전체 롤백·재이전 없음 |
| QA5 / 격리 | Filter/Sink 실패 또는 원본 보존 실패 | 진단·후속 메시지 진행, 참조 회수 | PIPELINE_FAILED/RAW_STORAGE_FAILED, 누수 없음 |
| QA6 / 접근 | 조회자가 다른 scope 원본·재처리 요청 | 모든 원래 대상 권한 + 재처리 권한 검사 | 401/403/404와 허용된 결과 확인 |
| QA7 / 재현성 | 제외된 원본을 새 조건으로 재처리 | message_id 유지, 새 실행/record ID·revision | 원본 수 불변·새 결과 추가·변경 revision 구분 |
| QA8 / 운영 | 시험 PC에서 장시간 발생·폭주 | 수집·조회·보존 동작 관측 | 처리량/p99·디스크/RSS 목표는 실측 후 설정; 아직 운영 인수 전 |

## Constraint

| ID | 제약 |
| --- | --- |
| C1 | C#/.NET 10, 독립 PC가 기본. 중앙 서버나 외부 DB 설치 불필요 |
| C2 | Source 업무 부담 최소화. 현재 전송은 best-effort notification |
| C3 | 공통 계층은 payload 의미를 모름. 업무 schema는 Source/Workspace 계약 |
| C4 | Checkout/Checkin 기반 원본 수명. detached 작업 금지 |
| C5 | 순차 실행, Filter 한 건 입력 → 한 건 또는 제외. 다중 Sink 원자성 없음 |
| C6 | 신뢰하는 시작 시 plugin 로딩. 구현 assembly는 Contracts만 참조 |
| C7 | SQLite 단일 프로세스 소유·로컬 디스크, 논리 데이터 상한, 자동 삭제 없음 |
| C8 | 실제 드라이버 연동·테스트 제외. 앱 Source로 검증 |
