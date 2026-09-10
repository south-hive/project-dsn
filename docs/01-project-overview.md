# 1. Project Overview

## 목적

DSN은 다양한 앱 Source의 데이터를 로컬에서 수집·보존하고 Source → Filter → Sink로 처리하는 .NET 플랫폼이다. Workspace는 payload를 해석하는 첫 단계, 후속 Filter는 scalar 결과의 변환·선별, Sink는 저장/출력을 담당한다. 사무 PC 브라우저에서 시험 PC에 직접 접속할 수 있다.

분석 의미는 Source와 Workspace가 합의한다. 목적형 payload와 사후 원본 재처리를 모두 지원하며 DUT 불량 판단 알고리즘을 공통 계층에 고정하지 않는다. 드라이버 적용·검증은 담당자 영역이고 DSN 검증은 앱 Source만 사용한다.

## 범위와 산출물

| 포함 | 후속 범위 |
| --- | --- |
| C++·Python 앱 SDK, TCP notification 수신, 유한 큐 | 종단 간 ACK/outbox·중앙 자동 동기화 |
| Workspace plugin, 순서 있는 Filter, sqlite/console/discard Sink | 순환 그래프·시간창 집계·병렬 처리·plugin hot reload |
| SQLite 원본/결과/View 정의, NDJSON 초기 이전, 재처리 | 자동 보존 기간·DB 분할·운영 규모 부하 인수 |
| 웹 Presenter, 권한 있는 API, Docker 구성 | TCP 입력 인증/TLS·실제 OS별 운영 인수 |

코드는 src/, SDK는 sdk/, 예제는 samples/, 검증은 tests/다. prototype/은 이전 실험 구현이다. 중앙 환경도 같은 데이터/조회 계약으로 확장하되 현재 완료 범위는 로컬 파이프라인이다.

## 완료 판단

앱 입력이 원본으로 보존되고 설정 순서대로 처리되어 Sink에 도착해야 한다. 제외된 원본을 새 설정으로 재처리할 수 있고, 재시작 후 ID·값·View 정의가 유지되어야 한다. 잘못된 설정은 시작 전에 거부하고 실패한 처리 이후에도 원본 수명과 후속 입력을 보존한다.
