# 결정 사항과 미정 사항

## 설계 기준

| ID | 결정 |
| --- | --- |
| D01 | DSN은 C#으로 구현하고 Docker로 배포한다. |
| D02 | Source는 원 프로젝트 환경별 라이브러리로 제공한다. 현재 C++ 앱(CentOS 9), Linux 커널 드라이버, eBPF 발행 경로를 대상으로 한다. |
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
| D15 | Workspace는 서로 의존하지 않는다. 독립 스레드나 병렬 실행은 필수 조건이 아니다. CPU 부하를 줄이는 실행 방식을 선택한다. |
| D16 | 기본 순서는 FIFO다. queue policy 인터페이스를 두고 no_policy를 기본으로 하며 reordering은 추후 확장한다. |
| D17 | Source 개발자를 위한 간단한 console echo DSN Mock을 제공한다. |
| D18 | 모듈 간 의존성은 공개 인터페이스로 제한하고 데이터 소유권, 수명, 순서 및 실패 동작을 계약으로 정의한다. |

## 구현 전 우선 결정할 사항

| 항목 | 필요한 이유 |
| --- | --- |
| 정확한 OS·커널·eBPF 실행 환경 | Source API와 중계 경로의 실현 가능성 확인 |
| Source–DSN 전송 기술 및 컨테이너 연결 방식 | SDK·실제 Ingress·Mock의 동일 입력 계약 수립 |
| Envelope 직렬화와 framing | 언어가 다른 Source와 C# DSN의 상호 운용 |
| 최대 메시지 크기, 빈 payload 및 잘못된 필드 처리 | decoder와 버퍼의 구조적 유효성 정의 |
| Source 탄창 소유권, 준비 API, 용량 | 호출 반환 후 데이터 수명과 메모리 사용량 정의 |
| 버퍼 구조와 복수 큐 병합 | producer 동시성과 FIFO 범위 구체화 |
| Workspace 플러그인 계약·발견 경로·버전 호환성 | 동적 로딩과 안정적인 등록 구현 |
| 미등록 Workspace 이름 및 중복 대상 이름 처리 | 일부 대상 실패 시 전달 및 ref count 처리 정의 |
| 최소 record 및 View 계약 | field 나열과 record 간 결합의 구분, 상관관계 기준 정의 |
| 최초 저장소와 .NET 버전 | 빌드·배포 및 최소 수직 통합 구현 |

## 운영 경험을 바탕으로 결정할 사항

- Source별 관측 시간 단위, 경고 임계값, 선택적 폐기 조건.
- 오류 심각도 판정, 연결 해제 조건, 재접속 간격.
- Workspace 스케줄링과 느린 소비자의 격리 방식.
- 부하에 맞는 버퍼 용량과 구체적 폐기 선택.
- 오류 집계의 보존 기간과 용량 제한, 기록 불가 시 fallback.
- 메시지 장기 참조 보유의 감지와 대응.
- 필요 시 queue policy 기반 reordering.

## 검토 중인 설계안

- `IQueuePolicy`라는 구체 인터페이스 이름, lease의 단계별 인계 방식.
- ErrorSink의 독립 접수·집계 경로와 내부 오류 표현.
- Mock의 hex 출력, 출력 길이 제한, 출력 큐와 C# console 패키징.
- 세부 패키징, 통합 단계 및 최소 예제의 세부 구현.
- 실행 중 Workspace hot reload는 초기 설계 범위에 포함하지 않는 방향. 시작 시 동적 로딩과는 별도 요구다.

