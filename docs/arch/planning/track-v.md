# Track V: 사용자 View

[전체 순서 및 의존성](../10-implementation-plan.md) · [검증 기준](../11-verification.md)

## 입력 문서

- [07-types-and-interfaces.md](../07-types-and-interfaces.md)
- [08-sequences.md](../08-sequences.md)
- [09-deployment.md](../09-deployment.md)

## 작업 순서

선행 task가 모두 완료되면 착수한다. 같은 track에서도 의존 관계가 없으면 병렬 진행할 수 있다. 하위 작업은 해당 task 안의 구현 순서다.

| Task | 선행 task | 산출물 |
| --- | --- | --- |
| [V-01: 사용자 View 정의와 원격 API](#v-01) | [F-05](track-f.md#f-05) | View schema, API 규격, 조회 예제 |
| [V-02: View 조회와 데이터 조합 구현](#v-02) | [V-01](track-v.md#v-01), [P-01](track-p.md#p-01) | View service, query 계약 테스트 |
| [V-03: 실제 저장소와 원격 View 연결](#v-03) | [V-02](track-v.md#v-02), [P-02](track-p.md#p-02) | 원격 실행 예제, 실제 저장 통합 결과 |

<a id="v-01"></a>
## V-01. 사용자 View 정의와 원격 API

**상태:** 미착수

**선행 조건:** F-05

**하위 작업**

- [ ] `V-01.1` 사용자별 field 선택·조합·정의 보존 설계
- [ ] `V-01.2` query 결과·사용자 구분·접근 범위 정의
- [ ] `V-01.3` 서로 다른 View의 입력/출력 fixture 작성

**산출물:** View schema, API 규격, 조회 예제

**완료 기준:** 같은 record에 서로 다른 사용자 정의 결과를 제시하고 Workspace 직접 조회 없이 표현한다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="v-02"></a>
## V-02. View 조회와 데이터 조합 구현

**상태:** 미착수

**선행 조건:** V-01, P-01

**하위 작업**

- [ ] `V-02.1` Record 조회 계약을 사용하는 service 구현
- [ ] `V-02.2` field 선택·조합·페이지·빈 결과 처리
- [ ] `V-02.3` 저장 대역으로 사용자별 기대 결과 검증

**산출물:** View service, query 계약 테스트

**완료 기준:** Workspace 인스턴스를 참조하지 않고 View/query fixture에 일치한다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="v-03"></a>
## V-03. 실제 저장소와 원격 View 연결

**상태:** 미착수

**선행 조건:** V-02, P-02

**하위 작업**

- [ ] `V-03.1` 실제 Persistence와 endpoint 연결
- [ ] `V-03.2` 원격 조회·잘못된 field·조회 실패 검증
- [ ] `V-03.3` 사용자 View 구성과 조회 예제 기록

**산출물:** 원격 실행 예제, 실제 저장 통합 결과

**완료 기준:** 원격 사용자가 선택한 field 결과를 얻고 사용자 구분·접근 동작이 V-01 계약에 맞는다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.
