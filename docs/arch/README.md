# DSN 설계 및 구현 문서

설계 기준일: 2026-09-08.

DSN은 다양한 Source의 메시지를 낮은 부하로 수집하고, Workspace가 해석한 record를 저장하여 사용자별 View로 제공하는 플랫폼이다. 특정 테스트 시나리오나 payload 형식에 종속되지 않는다.

## 설계 문서

| 문서 | 내용 |
| --- | --- |
| [아키텍처](01-architecture.md) | 시스템 범위, 책임, 기본 실행 및 손실 계약 |
| [계약과 메시지 수명](02-contracts.md) | 버전별 검증, 공개 범위, FIFO, Checkout·Checkin, 오류 처리 |
| [Source와 DSN Mock](03-source-and-mock.md) | 환경별 발행 경로와 console echo |
| [모듈 경계와 검증](04-delivery.md) | 제공·소비 인터페이스와 검증 경계 요약 |
| [결정 사항과 미정 사항](05-decisions.md) | 필수 설계 기준과 후속 결정 항목 |
| [전체·패키지·컴포넌트 구조](06-structural-views.md) | SYS-01, PKG-01, CMP-01–05 |
| [클래스·인터페이스](07-types-and-interfaces.md) | CLS-01–03, IF-01–02, 연산·소유권 계약 |
| [주요 시퀀스](08-sequences.md) | DSN 실행·종료, Source attach·detach, 발행·오류·View 조회 |
| [배포 View](09-deployment.md) | host/kernel/container/원격 경계, 배포 산출물, 자원 수명 |
| [Task·Track·진행 순서](10-implementation-plan.md) | 11개 track, 43개 task, 129개 하위 작업, 의존성 및 통합 지점 |
| [검증·계측·추적](11-verification.md) | 시나리오, 측정 지점, 요구사항→구조→task 매핑 |

## 문서 사용 순서

1. [전체 구조](06-structural-views.md)와 [설계 기준](05-decisions.md)으로 시스템 경계를 확인한다.
2. [클래스·인터페이스](07-types-and-interfaces.md)와 [시퀀스](08-sequences.md)로 소유권·실패·종료 계약을 확인한다.
3. [Task 목록](10-implementation-plan.md)에서 해당 track 문서를 열고 선행 조건·산출물·완료 기준을 확인한다.
4. [검증 계획](11-verification.md)에 따라 실제 결과와 미검증 범위를 기록한다.

## 설계 상태와 도식 표기

- **확정**: 현재 설계의 필수 동작과 제약. [D01–D25](05-decisions.md)가 기준이다.
- **제안 / 구현안**: 패키지·클래스 배치, API 이름·시그니처, lifecycle 등 구체화 중인 설계.
- **미정**: 전송·저장 기술, Admin 상세 형식, 운영 값 등 후속 task에서 결정할 항목.

새로운 다이어그램의 구체 타입·연산 이름은 구현안이며 확정된 runtime API를 뜻하지 않는다. 기존 책임 계약을 변경하는 내용은 미정·제안으로 명시한다. Task 상태는 구현 작업 상태이며 문서 작성으로 완료 처리하지 않는다.

다이어그램은 Markdown 안의 Mermaid 원본으로 제공한다. GitHub 문서 보기에서 렌더링할 수 있다. 패키지 그림은 코드 의존성, 시스템·컴포넌트 그림은 데이터·참조 흐름, 시퀀스는 실행 순서를 나타내며 각 그림의 범례를 따른다.
