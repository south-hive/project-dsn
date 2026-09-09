# 3. Architectural Drivers

## Use Case

| ID | 사용자·목적 | 정상 흐름 | 예외·종료 조건 |
| --- | --- | --- | --- |
| UC1 | Source가 시험 이벤트를 남긴다 | notification 수신 → 봉투 검사 → 대상 Workspace 해석 → record 저장 | 잘못된 입력·수용 초과는 폐기/진단. 저장 성공 응답 없음 |
| UC2 | 평가자가 진행·이상을 확인한다 | 인증/scope 확인 → field 선택 또는 저장된 View 조회 | 없는 field 400, 인증 실패 401, scope 위반 403, 없는 정의 404 |
| UC3 | 관리자가 처리 오류를 확인한다 | 오류 본문 집계 → Admin snapshot record → View 조회 | 집계/저장 한도 도달 시 진단 일부 유실 가능 |
| UC4 | 연동 시 입력 형식을 확인한다 | 테스트 Source → 독립 Mock → envelope·hex 확인 | 잘못된 RPC 연결 종료, 출력 포화 시 출력 폐기 |
| UC5 | 서버를 실행·종료·재시작한다 | 설정·plugin 준비 → 수신/조회 → 입력 중단·drain → 저장 복구 | 필수 plugin 실패/포트 충돌은 시작 실패, 비협력 호출은 종료 지연 |

## Quality Attribute Scenario

각 시나리오는 자극의 출처, 자극, 환경, 대상, 응답, 측정 기준으로 기술한다. 아래 수치는 기능 판정 기준과 구현 기본값이다. 부하에 따른 성능 목표로 해석하지 않는다.

| ID / 품질 | 출처·자극 | 환경·대상 | 기대 응답 | 측정·판정 기준 |
| --- | --- | --- | --- | --- |
| QA1 / 성능·자원 | Source가 처리를 초과하는 입력을 발행 | 느린 Workspace/저장, Runtime | 수용한 메시지는 FIFO 처리, 한도 초과 신규 입력은 폐기·집계 | 기본 queue 1,024건/16 MiB 초과 거부. p99 지연·CPU·처리량 목표 및 부하 판정은 미정 |
| QA2 / 메모리 정확성 | Workspace가 중복 반납하거나 예외 발생 | 여러 대상이 원본 공유, Lifetime | lease별 유효 반환 한 번, 마지막 참조에서 회수 | 동시 Checkin 32회 중 성공 1회; 처리 종료 뒤 references=0 |
| QA3 / 실패 격리 | Source A의 잘못된 RPC 또는 대상 연결 종료 | A/B 동시 연결, Ingress | A만 종료, B 계속 수신, A의 신규 연결 허용 | B 후속 메시지와 A 재접속 입력 수신 확인 |
| QA4 / 복구·일관성 | 재시작 또는 미완성 마지막 파일 쓰기 | Append 완료 record가 있는 Persistence | 완료 record 복구, 마지막 불완전 frame만 제거 | 재시작 전후 id·값·순서 동일, 후속 id 연속 |
| QA5 / 종료 안전성 | 종료 요청 중 Workspace가 반환하지 않음 | active 참조와 queue가 있는 Host/Runtime | 접수 중단, timeout 후 대기분 폐기, active 원본 유지 | timeout 후 active 읽기 유효, 실제 반환 후 references=0. 총 종료시간 상한은 보장 안 함 |
| QA6 / 접근 통제 | 사용자가 다른 scope/정의를 조회 | 토큰 설정된 HTTP View | 허용 Workspace와 자기 정의만 제공 | 무토큰 401, scope 밖 403, 타 사용자만 가진 정의 404 |
| QA7 / 변경 용이성 | 다른 payload 처리 plugin을 배치 | 시작 시 plugin 로딩, Workspace 계약 | Contracts 기반 plugin 등록·처리, 부적합 plugin 거부 | 예제 DLL 동적 로딩/처리 확인. 추가 plugin·버전 호환 matrix는 확대 필요 |
| QA8 / 관측 가능성 | 동일/서로 다른 오류가 반복 | 오류 집계와 Admin 저장 | 본문 동일성 집계, 시각·count 보존, snapshot 투영 | 동일 본문 2회 → count 2; source_id 차이는 별도 집계; 변경 없는 revision 재출력 없음 |

## Constraint

| ID | 제약 | 설계에 주는 영향 |
| --- | --- | --- |
| C1 | C#/.NET 10, Docker 배포 대상 | Host/Mock을 .NET 산출물로 제공. Termux 실행과 Linux 배포 검증 구분 |
| C2 | Source 부담 최소화, 손실 허용, 업무 ACK/NACK 없음 | notification 입력과 유한 수용량, 전송 성공을 저장 성공으로 취급하지 않음 |
| C3 | 공통 계층은 payload 의미를 모름 | 버전별 봉투 검사와 Workspace 해석 분리 |
| C4 | DSN 내부 공유 원본 하나, 명시적 Checkout/Checkin | root·lease 수명 관리, 저장 전 필요한 값 복사 |
| C5 | 초기 FIFO·순차 실행, Workspace 간 처리 의존성 없음 | queue policy와 executor 분리, 기본 재정렬/병렬화 없음 |
| C6 | 시작 시 신뢰하는 로컬 plugin 로딩 | 동적 DLL 계약, 중복 이름 거부, hot reload/sandbox 없음 |
| C7 | View는 저장된 record만 조회 | Workspace 직접 조회 금지, 기록·조회 계약 분리 |

Source 내부 성능과 상세 monitoring/운영 임계값은 별도 평가 범위다. 미정인 수치 목표를 임의로 채워 설계 충족을 선언하지 않는다.
