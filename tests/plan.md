# DSN practice 테스트 계획

대상은 practice의 TypeScript prototype이다. Verification은 내부 계약, Validation은 실제 TCP/HTTP와 CLI의 요구 시나리오를 검증한다. 기존 V01–V20, A01–A12를 유지하고 아래 시험을 추가한다. 테스트 코드는 `prototype/test/`에 둔다.

| ID | 입력·시험 | 기대 결과 | 설계 / QA |
| --- | --- | --- | --- |
| B01 | payload 한도 -1/일치/+1, 잘못된 한도 설정 | 경계 수용·초과 거부, 잘못된 설정 거부 | T01 / QA-07 |
| B02 | 큐 byte 한도 일치/초과 | 실패 시 소유권 유지, 반환 후 재사용 | T09 / QA-02 |
| B03 | 서로 역순인 timestamp와 혼합 목적지 | 적재 FIFO, 유효 목적지 한 번 처리 | T05–08 / QA-02 |
| B04 | async 파싱 실패·Checkin 이후 저장 실패 | 다음 대상 처리, 최종 참조 0 | T08 / QA-02·03 |
| B05 | record byte 경계, export, offset/limit | 오래된 값 제거, 정확한 복사 값 | T16–17 / QA-06 |
| B06 | 오류 본문 필드 변경과 포화 | 다른 본문 분리, 기존 count 갱신 | T11–12 / QA-04 |
| B07 | Admin 저장 실패 후 재투영 | 실패를 완료로 표시하지 않고 재시도 가능 | T12 / QA-04 |
| N01 | frame 한도 -1/일치/+1 | 정상 입력 처리, 초과 session 종료 | T01 / QA-03 |
| N02 | UTF-8 중간 분할과 미완성 frame detach | bytes 정상 복원, 미완성 폐기 | T01·14 / QA-02 |
| N03 | session 한도 초과·해제 | 기존 연결 유지, 해제 후 재접속 | T13–14 / QA-03 |
| N04 | 원본 한도 포화와 회복 | 새 입력 폐기, 원본 반환 후 재수용 | T09 / QA-02 |
| N05 | 활성 작업 중 동시·반복 drain Stop | 동일 종료 작업, 대기 항목 처리, 참조 0 | T15 / QA-05 |
| N06 | View 포트 충돌·종료 후 포트 재사용 | 시작 rollback, 새 Host 정상 시작 | T04·15 / QA-05 |
| N07 | Mock 256 bytes 초과 binary | 앞 256 bytes hex, 전체 길이, truncated | T03 / QA-02 |

실행 순서: 빌드 → 기존·추가 Verification → 기존·추가 Validation 3회 → 데모. 단계별 60초 timeout, 실패 원문·종료 코드 보존. 빌드 실패 시 의존 단계는 미실행으로 기록한다. 시험 실패 후에도 독립적인 단계는 실행한다.

수정 전 결함 재현을 먼저 확보한다. 수정 후 관련 시험과 전체 시험을 실행한다. 최종 합격은 실패·skip·timeout 0, 정상 종료 후 참조 0, 예상 밖 예외·프로세스 종료 없음이다. 네트워크 반복은 성능 측정이 아니다.

C#·Docker·실제 Source/kernel/eBPF, 영속 저장·성능·운영 모니터링은 미검증으로 남긴다. 설계 변경이 필요한 사항은 결함 목록에서 별도 구분한다.
