# 결정 사항과 미정 사항

## 설계 기준

| ID | 결정 |
| --- | --- |
| D01 | DSN은 C#으로 구현하고 Docker로 배포한다. |
| D02 | C++ 앱(CentOS 9), Linux driver/eBPF는 외부 Source 담당자가 구현한다. DSN은 RPC 연동 인터페이스·Mock·검증 자료를 제공한다. |
| D03 | Source와 DSN은 현재 같은 호스트이며 Docker 밖과 안으로 나뉜다. 원격은 View를 기준으로 연결한다. |
| D04 | Source 발행은 내부 fire gun 탄창에 적재하는 것으로 끝낸다. 탄창은 넉넉해야 한다. |
| D05 | Source 호출은 no blocking / no error / no exception이며 손실을 허용한다. ACK/NACK을 요구하지 않는다. |
| D06 | DSN은 무한정 수신한다는 기본 전제로 설계하되 실제 수용 초과 시 폐기하고 IErrorSink로 Admin Space에 오류를 남긴다. |
| D07 | source_id별 단위 시간당 메시지 수를 관측하여 추후 경고와 필요 시 폐기에 활용한다. |
| D08 | 시스템 다운에 가까운 현상은 해당 Source의 연결만 끊는다. 신규 연결을 허용하고 재발 시 반복한다. 세부 운영 기준은 보류한다. |
| D09 | 동일 오류는 발생 시각을 제외한 전체 내용으로 비교하며 최초·최근 시각과 count를 저장한다. |
| D10 | Workspace가 topic과 유사한 분배 단위다. workspace[]로 전달하며 별도 topic을 두지 않는다. |
| D11 | Workspace는 DSN 시작 시 동적으로 로딩하고 Bulletin Board에 하나의 이름으로 등록한다. |
| D12 | 이름은 조직 내부에서 관리한다. 중복 이름의 후발 등록은 거부하고 IErrorSink에 기록한다. |
| D13 | payload는 공통 계층에서 블랙박스인 바이트이며 Source와 Workspace가 의미를 합의한다. |
| D14 | DSN 내부 메시지 원본은 하나다. Workspace는 읽기 전용 공유 참조를 사용하며 ref count 0에서 회수한다. 필요한 파싱 결과만 복사할 수 있다. |
| D15 | Workspace 간 처리 의존성은 없다. 초기 실행은 단일 FIFO 실행 루프에서 대상 Workspace를 순차 호출한다. 실행 방식은 교체 가능한 내부 인터페이스로 분리한다. |
| D16 | 기본 순서는 FIFO다. queue policy 인터페이스를 두고 no_policy를 기본으로 하며 reordering은 추후 확장한다. |
| D17 | Source 개발자를 위한 간단한 console echo DSN Mock을 제공한다. |
| D18 | 모듈 간 의존성은 공개 인터페이스로 제한하고 데이터 소유권, 수명, 순서 및 실패 동작을 계약으로 정의한다. |
| D19 | Message Lifetime이 원본 버퍼·ref count·회수를 소유하며 Bulletin Board에서 분리한다. |
| D20 | 참조 획득·반납은 명시적 Checkout·Checkin으로 수행한다. 파싱·필요 데이터 복사 직후 반납하여 보유 시간을 최소화한다. |
| D21 | 오류 접수·집계는 Admin Space와 분리한다. Admin Space는 관리용 record 변환을 담당하며 저장·View·cache 역할을 겸하지 않는다. |
| D22 | Transport Adapter의 연결·수신·종료와 버전별 Envelope Decoder의 검증을 분리한다. |
| D23 | Bulletin Board는 전달 대상을 결정한다. 실행 구성 요소는 호출·실패 처리·참조 반납 보장을 담당하며 queue policy와 분리한다. |
| D24 | Persistence는 record 저장·조회·export를 담당한다. View는 조회 인터페이스로 사용자별 field 선택·조합을 제공하고 Workspace를 직접 조회하지 않는다. |
| D25 | Workspace에는 등록·처리, 메시지 참조, record 저장 및 필요한 진단 계약만 공개한다. 큐·dispatcher·registry 변경·원본 할당과 강제 회수는 DSN 내부로 제한한다. |

| D26 | Source 내부 구현은 Source 담당자가 결정한다. DSN과 Source의 공통 설계 범위는 우선 RPC 계약이며 특정 framework는 미정이다. |

## 구현 전 우선 결정할 사항

| 항목 | 필요한 이유 |
| --- | --- |
| DSN host·.NET·RPC 검증 환경 | DSN 서버와 Mock의 build·실행 조건; Source kernel 환경은 외부 담당 |
| RPC 방식 및 컨테이너 endpoint | Source·DSN·Mock의 동일 RPC 입력 계약 수립 |
| Envelope 직렬화, 버전 선택용 최소 공통 헤더와 framing | 언어가 다른 Source와 C# DSN의 상호 운용 |
| 최대 메시지 크기, 빈 payload 및 잘못된 필드 처리 | decoder와 버퍼의 구조적 유효성 정의 |
| Source 전달 인터페이스·예제 | RPC 규격과 요구 동작 전달; 탄창·준비 API·용량은 Source 담당 |
| 버퍼 구조와 복수 큐 병합 | producer 동시성과 FIFO 범위 구체화 |
| Workspace 플러그인 계약·발견 경로·버전 호환성 | 동적 로딩과 안정적인 등록 구현 |
| 미등록 Workspace 이름 및 중복 대상 이름 처리 | 일부 대상 실패 시 전달 및 ref count 처리 정의 |
| 최소 record 및 View 계약 | field 나열과 record 간 결합의 구분, 상관관계 기준 정의 |
| 최초 저장소와 .NET 버전 | 빌드·배포 및 최소 수직 통합 구현 |

## 운영 경험을 바탕으로 결정할 사항

- Source별 관측 시간 단위, 경고 임계값, 선택적 폐기 조건.
- 오류 심각도 판정, 연결 해제 조건, 재접속 간격.
- 기본 순차 실행의 측정 결과에 따른 스케줄링 변경과 느린 소비자 격리.
- 부하에 맞는 버퍼 용량과 구체적 폐기 선택.
- 오류 집계의 보존 기간과 용량 제한, 기록 불가 시 fallback.
- 메시지 장기 참조 보유의 감지와 대응.
- 필요 시 queue policy 기반 reordering.

## 검토 중인 설계안

- `IQueuePolicy`와 실행 구성 요소 등 구체 인터페이스 이름 및 시그니처.
- Checkout·Checkin 처리 문맥, 호출 종료 시 미반납 참조 정리와 중복 반납 방지의 구체 API.
- 오류 본문의 구체 필드 및 접수·집계 구현.
- Mock의 hex 출력, 출력 길이 제한, 출력 큐와 C# console 패키징.
- 세부 패키징, 통합 단계 및 최소 예제의 세부 구현.
- 실행 중 Workspace hot reload는 초기 설계 범위에 포함하지 않는 방향. 시작 시 동적 로딩과는 별도 요구다.


## Admin Workspace 구현 시 결정할 사항

- 관리 이벤트의 envelope·payload 형식과 전달 경로.
- 원본 메시지를 관리 payload에 포함할지 여부.
- 원본 첨부 시 복사·참조 방식과 수명.
- 첨부 원본의 동일성 비교 범위 및 대표 사례 보존.
- 집계 결과 전달 주기·갱신 방식과 Admin 처리 실패 대응.

Admin 메시지의 중첩 envelope 구조와 원본 첨부는 현재 계약에 포함하지 않는다.

## 미정 사항의 구현 task 연결

| 결정 영역 | 결정 task |
| --- | --- |
| 환경·toolchain·패키지 배치 | [F-01](planning/track-f.md#f-01) |
| wire format·최소 헤더·transport·session 매핑 | [F-02](planning/track-f.md#f-02) |
| Source 전달용 RPC 규격·fixture·예제 | [F-03](planning/track-f.md#f-03) |
| 실제 공개 API·Checkout·Checkin·대상/실패 규칙 | [F-04](planning/track-f.md#f-04) |
| record·query·export·View 조합 의미 | [F-05](planning/track-f.md#f-05) |
| QA·위험·관측 아이디어 — 현재; 상세 계측은 1차 이후 | [F-06](planning/track-f.md#f-06) |
| Admin 전달·첨부·동일성·갱신 | [A-01](planning/track-a.md#a-01) |
| 실제 저장소 | [P-02](planning/track-p.md#p-02) |
| 사용자 View API·정의 보존·접근 범위 | [V-01](planning/track-v.md#v-01) |
| plugin 발견·호환성·시작·종료 정책 | [H-01](planning/track-h.md#h-01) |

운영 임계값과 재정렬·Workspace 격리는 구현 인터페이스를 마련한 뒤 측정 결과에 따라 결정한다. Admin 상세 결정은 해당 Workspace 구현 시 수행하며 초기 Source→Mock 경로의 선행 조건이 아니다.

## 1차 개발과 후속 품질 평가

- 현재는 Quality Attribute·위험 가설·관측 아이디어를 등록한다.
- 기본 기능과 소유권·실패·종료 계약의 정확성 검증은 1차 개발에 포함한다.
- 상세 monitoring 구현, 운영 지표·수집·알림·자동 조치와 부하 기반 개선은 1차 완료 이후로 보류한다.
- X-05가 1차 인수 지점이다. E-02·X-04는 X-05 이후이며 1차 완료의 선행 조건이 아니다.

개발 전 보완 계약은 [GAP-01–10](12-engineering-readiness.md), 품질 위험은 [QA·Risk 등록부](13-quality-attributes-and-risks.md)에 연결한다.
