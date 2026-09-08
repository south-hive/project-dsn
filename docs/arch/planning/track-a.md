# Track A: Admin Workspace

[전체 순서 및 의존성](../10-implementation-plan.md) · [검증 기준](../11-verification.md)

## 입력 문서

- [02-contracts.md](../02-contracts.md)
- [05-decisions.md](../05-decisions.md)
- [06-structural-views.md](../06-structural-views.md)

## 작업 순서

선행 task가 모두 완료되면 착수한다. 같은 track에서도 의존 관계가 없으면 병렬 진행할 수 있다. 하위 작업은 해당 task 안의 구현 순서다.

| Task | 선행 task | 산출물 |
| --- | --- | --- |
| [A-01: Admin 전달·record 계약 정의](#a-01) | [E-01](track-e.md#e-01), [F-05](track-f.md#f-05) | Admin 규격, record 모델, 입력/출력 예제 |
| [A-02: Admin Workspace 구현](#a-02) | [A-01](track-a.md#a-01), [R-04](track-r.md#r-04), [P-01](track-p.md#p-01) | Admin plugin, 전달 adapter, 통합 fixture |

<a id="a-01"></a>
## A-01. Admin 전달·record 계약 정의

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** E-01, F-05

**하위 작업**

- [ ] `A-01.1` 관리 envelope/payload·전달 경로 결정
- [ ] `A-01.2` 원본 첨부·수명·동일성 비교 범위 결정
- [ ] `A-01.3` 집계 갱신의 record 반영과 실패 계약 정의

**산출물:** Admin 규격, record 모델, 입력/출력 예제

**완료 기준:** 원본 wrapping을 기본으로 가정하지 않는다. 오류 집계·Admin·저장·View 책임이 겹치지 않는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**개발 계약 보완:** GAP-09: 집계 갱신의 Admin record 반영 규칙을 정의한다. 원본 첨부와 동일성 확장은 이 task 전까지 미정이다. [점검 목록](../12-engineering-readiness.md)

**담당자 결정:** 관리 입력/record 규격과 fixture 초안 작성

**협의 조건:** Admin 전달·원본 첨부·동일성·집계 갱신 표현을 정할 때

**협의 대상·관련 경계:** E·P; 공유 메시지 사용 시 R, View 의미 영향 시 V — DC-08, DC-06 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.

<a id="a-02"></a>
## A-02. Admin Workspace 구현

**상태:** 미착수

**단계:** 1차 개발

**선행 조건:** A-01, R-04, P-01

**하위 작업**

- [ ] `A-02.1` 관리 데이터를 record로 변환하는 plugin 구현
- [ ] `A-02.2` 집계 결과 전달 adapter 연결
- [ ] `A-02.3` Admin 부재·재가동·저장 실패와 record fixture 검증

**산출물:** Admin plugin, 전달 adapter, 통합 fixture

**완료 기준:** 일반 Workspace 등록·저장 계약을 사용한다. 자체 cache·View·집계 저장소 역할을 맡지 않고 Admin 부재에도 오류 접수는 유지된다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

**담당자 결정:** 확정 형식의 관리용 record 변환과 plugin 내부 코드

**협의 조건:** 관리 전달 규약·record 타입·원본 보유·저장 의미를 바꿀 때

**협의 대상·관련 경계:** E·P; lease R, View field V — DC-08, DC-06, DC-04 ([결정 범위](../14-decision-boundaries.md)). 기존 계약 준수 시 재협의 없이 진행한다.
