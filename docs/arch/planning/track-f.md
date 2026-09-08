# Track F: 공통 계약과 기준

[전체 순서 및 의존성](../10-implementation-plan.md) · [검증 기준](../11-verification.md)

## 입력 문서

- [06-structural-views.md](../06-structural-views.md)
- [07-types-and-interfaces.md](../07-types-and-interfaces.md)
- [05-decisions.md](../05-decisions.md)

## 작업 순서

선행 task가 모두 완료되면 착수한다. 같은 track에서도 의존 관계가 없으면 병렬 진행할 수 있다. 하위 작업은 해당 task 안의 구현 순서다.

| Task | 선행 task | 산출물 |
| --- | --- | --- |
| [F-01: 지원 환경과 프로젝트 경계 고정](#f-01) | 없음 | 환경 matrix, 프로젝트 배치안, build 절차 |
| [F-02: Envelope v1과 전송 경계 정의](#f-02) | [F-01](track-f.md#f-01) | wire v1, transport/session 규격, 공통 fixture |
| [F-03: Source 발행·탄창·attach 수명 계약](#f-03) | [F-01](track-f.md#f-01) | Source API, magazine 상태 전이, teardown 계약 |
| [F-04: DSN 실행·메모리·플러그인 API 고정](#f-04) | [F-01](track-f.md#f-01) | 계약 프로젝트, 소유권 표, 대역 Workspace, 기본 오류 모델 |
| [F-05: Record·조회·export·View 경계 정의](#f-05) | [F-01](track-f.md#f-01) | Record/저장/조회/export 규격, 예제 record·query fixture |
| [F-06: 성능·손실 계측 계획과 목표 정의](#f-06) | [F-01](track-f.md#f-01) | 계측 계획, workload, 결과 schema 및 목표 표 |

<a id="f-01"></a>
## F-01. 지원 환경과 프로젝트 경계 고정

**상태:** 미착수

**선행 조건:** 없음

**하위 작업**

- [ ] `F-01.1` 배포판·CPU·커널·eBPF 실행 문맥 및 검증 장비 기록
- [ ] `F-01.2` .NET·native toolchain과 프로젝트 참조 경계 선택
- [ ] `F-01.3` 공통 build·검증 명령과 버전 고정 목록 작성

**산출물:** 환경 matrix, 프로젝트 배치안, build 절차

**완료 기준:** 각 대상의 지원/미검증 구분과 재현 환경이 명시되고 빈 프로젝트를 빌드할 수 있다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="f-02"></a>
## F-02. Envelope v1과 전송 경계 정의

**상태:** 미착수

**선행 조건:** F-01

**하위 작업**

- [ ] `F-02.1` 최소 헤더·version·framing·길이·encoding·time 표현 및 오류 규칙 정의
- [ ] `F-02.2` 동일 호스트 전송·container 접근·session/source_id 매핑 선택
- [ ] `F-02.3` 정상·손상·미지원 버전의 언어 중립 byte fixture 작성

**산출물:** wire v1, transport/session 규격, 공통 fixture

**완료 기준:** C++ encoder와 C# decoder가 동일 bytes와 기대 결과를 공유하며 frame 및 연결 종료 시 소유권을 구분한다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="f-03"></a>
## F-03. Source 발행·탄창·attach 수명 계약

**상태:** 미착수

**선행 조건:** F-01

**하위 작업**

- [ ] `F-03.1` 준비 API와 Publish 비용 경계 및 producer 동시성 정의
- [ ] `F-03.2` 탄창 용량·슬롯 소유권·포화 폐기·미연결 동작 정의
- [ ] `F-03.3` attach/detach·재접속·SDK 종료와 활성 접근 정리 규칙 정의

**산출물:** Source API, magazine 상태 전이, teardown 계약

**완료 기준:** 발행 비대기·오류 전파 금지·반환 후 데이터 수명·detach 중 발행 및 해제 조건이 검증 가능하게 정의된다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="f-04"></a>
## F-04. DSN 실행·메모리·플러그인 API 고정

**상태:** 미착수

**선행 조건:** F-01

**하위 작업**

- [ ] `F-04.1` 공개 Workspace 계약과 내부 Lifetime·Queue·Executor 계약 분리
- [ ] `F-04.2` Checkout·Checkin·root 인계·종료 정리 및 기본 ErrorEvent 모델 정의
- [ ] `F-04.3` 대상 배열 순서·미등록/중복 대상·처리 실패 후 다음 대상 동작 정의

**산출물:** 계약 프로젝트, 소유권 표, 대역 Workspace, 기본 오류 모델

**완료 기준:** 플러그인은 공개 계약만으로 빌드하며 count 0·중복 반납·실패 경로 및 큐 인계의 기대 결과가 명확하다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="f-05"></a>
## F-05. Record·조회·export·View 경계 정의

**상태:** 미착수

**선행 조건:** F-01

**하위 작업**

- [ ] `F-05.1` record 소유 데이터·Workspace별 field 식별과 타입 정의
- [ ] `F-05.2` Append 반환·실패·Query pagination/일관성·Export 부분 실패 정의
- [ ] `F-05.3` View 선택·조합·결합 키·사용자 구분·정의 보존 범위 결정

**산출물:** Record/저장/조회/export 규격, 예제 record·query fixture

**완료 기준:** 서로 다른 Workspace record로 사용자별 View 기대 결과를 기술하고 Workspace 직접 조회가 필요하지 않다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.

<a id="f-06"></a>
## F-06. 성능·손실 계측 계획과 목표 정의

**상태:** 미착수

**선행 조건:** F-01

**하위 작업**

- [ ] `F-06.1` 발행 지연·CPU·원본 보유·수신량·손실 측정 지점 정의
- [ ] `F-06.2` 정상·burst·미연결·느린 저장·다중 Source workload 작성
- [ ] `F-06.3` 환경별 수치 목표 또는 목표 미정 상태와 baseline 형식 정의

**산출물:** 계측 계획, workload, 결과 schema 및 목표 표

**완료 기준:** 발행과 수신 건수 및 clock 기준을 구분하며 수치 목표가 없는 항목을 미정으로 표시한다.

**인계 증거:** 변경 위치·build/검증 명령·환경·실제 결과·남은 제약을 완료 기록에 첨부한다.
