# 개발 준비도와 보완 계약

> 상세 설계 참조입니다. 처음 읽는 부서원은 [전체 → 기능 → 담당 작업 안내](../development/README.md)에서 시작하세요. 아래 초기 설계의 미정/제안과 현재 구현 선택은 [구현 계약](../implementation/contracts.md)을 함께 확인합니다.

## 점검 결과

Source 구체 구현은 외부 담당 범위다. 아래 개발 준비도는 DSN·RPC 인터페이스·Mock을 기준으로 판단하며 Source 내부 API·탄창·kernel/eBPF의 결정을 DSN 착수 조건으로 두지 않는다.

구성 요소, 공개 범위, 참조 수명, 기본 실행 방식과 task 의존성은 정의되어 있다. **구체 API·wire format·설정·저장 규격은 아직 고정되지 않았으므로 모든 모듈을 곧바로 구현할 수 있는 상태는 아니다.** 일부는 기존 결정 task에 등록되어 있고, 아래 일부 세부 계약은 추가 명시가 필요하다.

이 목록은 새로운 제품 기능 목록이 아니다. 기존 기능을 서로 호환되게 구현하기 위한 결정 항목이다. 전체 항목을 한꺼번에 닫을 필요는 없으며 각 소비 task에 들어가기 전에 해당 계약을 완료한다.

## GAP-01–10. 개발 계약 점검

| ID | 분류 | 필요한 결정·보완 | 담당 결정 task | 완료 증거 |
| --- | --- | --- | --- | --- |
| GAP-01 | 기존 미정 | RPC framework·호출 형태·서비스/메서드·요청·상태/응답 의미, envelope version·최대 크기·직렬화 | [F-02](planning/track-f.md#f-02) | RPC 계약/IDL과 정상·오류 fixture |
| GAP-02 | 외부 책임 구분 | Source 내부 API·탄창·thread·메모리·kernel/eBPF는 해당 담당 결정; DSN은 연동 요구만 전달 | [F-03](planning/track-f.md#f-03) | Source 책임 경계를 포함한 RPC 전달 문서 |
| GAP-03 | RPC 경계 상세 | source_id 중복·재시작 재사용·RPC session 다중 Source·검증 전 오류 식별 | [F-02](planning/track-f.md#f-02) | Source↔RPC session 매핑과 선택적 종료 규칙 |
| GAP-04 | 기존 미정 | 실제 Checkout·Checkin API, 유효하지 않은 lease 처리, Process 반환 의미, 호출 종료 정리, 미등록·중복 대상 및 실패 후 다음 대상 처리 | [F-04](planning/track-f.md#f-04) | 컴파일 가능한 계약과 수명·실패 fixture; plugin이 임의 해제 API에 접근하지 않음 |
| GAP-05 | 상세 명세 보완 | DSN 설정 키·타입·기본값·우선순위·오류 처리, DSN Queue/Lifetime/오류 집계 한도 | [F-04](planning/track-f.md#f-04), [E-01](planning/track-e.md#e-01), [H-01](planning/track-h.md#h-01) | DSN 설정 schema·예제와 초과 처리 표; Source 내부 용량 제외 |
| GAP-06 | 기존 미정의 상세 보완 | plugin 계약 버전·발견 규칙·의존 assembly 해석·필수 plugin 실패, 종료 중 새 호출 차단, 실행 중 저장/조회 취소 및 자원 종료 순서 | [F-04](planning/track-f.md#f-04), [F-05](planning/track-f.md#f-05), [H-01](planning/track-h.md#h-01) | 지원 plugin 예제와 부적합 예제, 시작·정상 종료·미종료 상태의 기대 결과 |
| GAP-07 | 기존 미정의 상세 보완 | record 식별·field 이름과 타입·null·schema 변화, Append 접수/영속 완료 의미, 중복 쓰기 허용 여부, 저장 후 조회 가능 시점 | [F-05](planning/track-f.md#f-05), [P-02](planning/track-p.md#p-02) | record/query fixture 및 저장 계약; exactly-once나 자동 재시도를 암묵적으로 요구하지 않음 |
| GAP-08 | 기존 미정의 상세 보완 | View에서 선택 가능한 field의 목록 제공, 조합·결합 키, 정의 보존, 사용자 구분·조회 범위, 빈 결과·잘못된 field·페이지 처리 | [F-05](planning/track-f.md#f-05), [V-01](planning/track-v.md#v-01) | Workspace 직접 조회 없이 서로 다른 View를 구성하는 API·결과 예제 |
| GAP-09 | 기존 미정의 상세 보완 | ErrorEvent 본문의 필수 필드·코드, source_id 미확인 오류, first/last/count 갱신 의미, Admin record의 집계 갱신 반영 방식 | [F-04](planning/track-f.md#f-04), [E-01](planning/track-e.md#e-01), [A-01](planning/track-a.md#a-01) | 오류 fixture와 기본 집계 결과; Admin 원본 첨부는 A-01까지 미정 유지 |
| GAP-10 | 상세 명세 보완 | DSN·Mock의 build/test·자동 검증·RPC fixture·배포 버전·산출물 경로 | [F-01](planning/track-f.md#f-01), [H-03](planning/track-h.md#h-03), [X-05](planning/track-x.md#x-05) | DSN 재현 절차와 검증 목록; Source toolchain은 외부 담당 |

유한 초기 용량과 실패 동작은 1차 구현에 필요하다. workload별 최적 용량, 운영 임계값, 자동 경고·차단 조정은 1차 이후의 Quality Attribute 평가 범위다. 초기값을 성능 목표 또는 운영 권장값으로 표현하지 않는다.

## 경계별 개발 착수 조건

| 구현 경계 | 먼저 필요한 계약 | 대역으로 진행 가능한 범위 |
| --- | --- | --- |
| 외부 Source ↔ DSN/Mock RPC | GAP-01·03, GAP-02 책임 전달 | RPC fixture·검증 client·Mock |
| Ingress ↔ Queue ↔ Executor ↔ Workspace | GAP-04–06 | root/lease ledger, buffer 대역, 실패 Workspace |
| Workspace ↔ Persistence | GAP-07 | IRecordStore 대역; 영속 완료와 구분 |
| View ↔ Persistence | GAP-07–08 | 공통 record/query fixture; 실제 DB 없이 결과 조합 |
| Diagnostics ↔ Admin | GAP-09 | 기본 오류 집계는 먼저 개발; Admin 전달 형식은 A-01에서 결정 |
| Host ↔ 배포 환경 | GAP-05–06·10 | 설정 검증, lifecycle 대역; 최종 Docker·kernel 통합은 별도 수행 |

## 1차 개발과 후속 평가의 경계

| 1차 개발에 포함 | 1차 개발 완료 이후 |
| --- | --- |
| 위 계약과 기본 설정을 구현 가능한 수준으로 정의 | QA별 수치 목표·workload·표본·측정 도구 구체화 |
| FIFO, no_policy, ref count, 실패 시 반납 등 기능 정확성 검증 | 상세 계측 코드·지표 수집·대시보드·알림 구현 |
| IErrorSink 기본 집계, Source 선택적 연결 종료 경로의 기능 검증 | 운영 상태를 이용한 심각도 판정·자동 조치·임계값 조정 |
| RPC 정상·실패·session 종료 계약 확인; Source 내부는 외부 담당 | 부하별 지연 상위 분위수·CPU·메모리·관측 overhead 분석 |
| 위험과 관측 아이디어를 문서에 등록 | 실행 방식·저장 큐·메모리 정책 등의 개선안 선택·적용 |

1차 기능 검증에서 실제 결함이 발견되면 해당 기능 task에서 수정한다. 이 원칙은 위험 평가용 계측·운영 개선을 뒤로 미루는 것과 별개다. 운영 모니터링이 없다는 이유로 Checkin의 정확성이나 종료 시 메모리 수명 검증을 생략하지 않는다.

## 작업 계획 반영

GAP-01–10은 해당 task의 계약 완료 기준에 연결한다. 별도 기능 track을 추가하지 않는다. [Quality Attribute 및 위험 등록부](13-quality-attributes-and-risks.md)에 미평가 위험과 관측 아이디어를 모으고, [작업 계획](10-implementation-plan.md)에서 후속 task가 1차 완료를 막지 않도록 단계와 의존성을 구분한다.

## 보완 항목의 결정 범위

| 보완 항목 | 협의할 경계 | 담당자에게 남는 구현 선택 |
| --- | --- | --- |
| GAP-01·03 | DC-02: Source/Ingress의 wire·전송·session 규약 | 규격 내 Encoder·Decoder·session 자료구조 |
| GAP-02 | DC-03: RPC 연동 요구 전달 | Source 내부 API·슬롯·발행 코드는 외부 담당 결정 |
| GAP-04 | DC-04·05: 참조 수명·처리 결과 | ref count 상태 표현·이름 조회·순차 loop 구현 |
| GAP-05·06 | DC-09 및 각 자원 계약: 설정·종료·호환성 | 확정된 수명주기를 구현하는 내부 코드 |
| GAP-07 | DC-06: Record·저장·조회 의미 | 내부 mapping·index·query 실행 |
| GAP-08 | DC-07: View 정의·공개 API | 계약 내 데이터 조합·응답 구성 |
| GAP-09 | DC-08: 오류·Admin 데이터 규약 | 집계 자료구조·record 변환 |
| GAP-10 | DC-01·10: 공통 build·배포·인수 경계 | 모듈 내부 테스트·예제·문서 구성 |

각 DC의 제공자·소비자와 완료 기준은 [결정 범위 문서](14-decision-boundaries.md)에 정의한다. 개별 task에도 동일한 판단 기준을 표시한다.
