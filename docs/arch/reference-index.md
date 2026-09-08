# DSN 상세 설계 참조 목록

설계 기준일: 2026-09-08.

C# 구현은 [루트 실행 안내](../../README.md)와 [구현 계약 v1](../implementation/contracts.md)을 참고한다. 아래 미정 항목과 task 상태는 최초 설계 기록이며, 현재 구현·검증 상태는 [검증 보고서](../implementation/verification.md)에서 별도로 관리한다.

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
| [Task·Track·진행 순서](10-implementation-plan.md) | 9개 DSN track, 35개 task, 105개 하위 작업, 의존성 및 통합 지점 |
| [검증·계측·추적](11-verification.md) | 1차 기능 검증, 후속 관측 아이디어, 요구사항 추적 |
| [개발 준비도](12-engineering-readiness.md) | 개발 전 보완 계약 GAP-01–10과 착수 조건 |
| [Quality Attribute·위험](13-quality-attributes-and-risks.md) | QA 7개, 위험 9개, 관측 아이디어 및 후속 평가 범위 |
| [담당자 결정·경계 협의](14-decision-boundaries.md) | 개별 구현 선택과 공동 계약의 구분, 협의 대상·완료 기준 |

## 이 참조 자료를 읽는 시점

부서원 공통 개발 안내는 [전체 → 기능 → 작업 → 담당 지시서](../development/README.md) 순서로 읽는다. 이 목록은 설계 근거·과거 task 추적·세부 계약을 찾아볼 때 사용한다. 현재 구현의 API/값은 [구현 계약](../implementation/contracts.md), 실행 증거는 [검증 보고서](../implementation/verification.md)를 기준으로 확인한다.

## 설계 상태와 도식 표기

- **확정**: 현재 설계의 필수 동작과 제약. [D01–D26](05-decisions.md)가 기준이다.
- **제안 / 구현안**: 패키지·클래스 배치, API 이름·시그니처, lifecycle 등 구체화 중인 설계.
- **미정**: 전송·저장 기술, Admin 상세 형식, 운영 값 등 후속 task에서 결정할 항목.

새로운 다이어그램의 구체 타입·연산 이름은 구현안이며 확정된 runtime API를 뜻하지 않는다. 기존 책임 계약을 변경하는 내용은 미정·제안으로 명시한다. Task 상태는 구현 작업 상태이며 문서 작성으로 완료 처리하지 않는다.

다이어그램은 Markdown 안의 Mermaid 원본으로 제공한다. GitHub 문서 보기에서 렌더링할 수 있다. 패키지 그림은 코드 의존성, 시스템·컴포넌트 그림은 데이터·참조 흐름, 시퀀스는 실행 순서를 나타내며 각 그림의 범례를 따른다.

## 1차 범위

35개 DSN task 중 33개는 1차 개발, E-02·X-04의 2개는 후속 평가로 보류한다. X-05가 1차 산출물 인수다. 현재 QA·위험과 관측 아이디어를 문서로 등록하며, 상세 계측·monitoring·개선 조치는 1차 완료 이후에 진행한다.

## Source 범위

Source 내부 구현은 각 Source 담당자가 결정한다. DSN은 RPC 연동 인터페이스·Envelope·서버 수신부·Mock·검증 자료를 제공한다. 최초 설계에서는 RPC framework를 미정으로 두었으며 현재 선택은 [C# 구현 계약](../implementation/contracts.md)에 있다. 기존 S/K 참조 문서는 외부 책임을 표시하며 DSN 개발의 선행 조건이 아니다. [연동 계약과 책임](03-source-and-mock.md)을 기준으로 한다.
