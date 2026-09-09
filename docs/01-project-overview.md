# 1. Project Overview

## 목적

DSN은 시험 프로그램과 장치가 발생시키는 메시지를 수집하여, 필요한 데이터로 해석·저장하고 조회하는 플랫폼이다. 평가자는 시험 진행 상태와 이상 발생 이력을 확인하고, 관리자는 수신·처리 오류를 확인한다. 특정 시험 시나리오나 payload 형식에 종속되지 않는다.

메시지 발행이 원래 시험 작업을 방해하지 않는 것을 우선한다. 전달 손실을 허용하며 메시지별 ACK/NACK이나 자동 재전송을 요구하지 않는다. Source 내부의 비대기 발행 구현과 그 성능 검증은 실제 Source의 책임이다.

## 범위와 산출물

| 포함 | 범위 밖 |
| --- | --- |
| C# DSN: RPC 수신, Workspace 실행, 오류 집계, 영속 저장, HTTP View | 실제 C++ 앱·driver/eBPF의 Source 내부 구현 |
| Workspace 공개 계약, echo/hex 예제 plugin | 업무별 payload 스키마의 공통화 |
| 동일 RPC 계약의 독립 console Mock, 테스트 Source | production Source SDK, 전달 보장, exactly-once |
| build/test/publish 및 Docker 배포 구성 | GUI, 임의 record join, 상세 운영 monitoring |

C# 본체는 `src/`, 테스트는 `tests/Dsn.Tests`와 `tests/Dsn.TestSource`에 있다. `prototype/`은 이전 TypeScript 실험 모델이며 현재 제품 구현과 별개다.

## 완료 판단

기본 기능의 완료 기준은 실제 RPC 입력이 Workspace를 거쳐 저장되고, 원본 반납 이후와 재시작 이후에도 정의된 View 결과를 얻는 것이다. 실패 시 참조 수명·다음 대상 처리·사용자 조회 범위를 지켜야 한다.

Termux에서 기능 통합 및 publish된 Host/Mock 실행은 검증했다. Docker 배포 구성은 작성됐지만 Linux 컨테이너 실행 인수는 남아 있다. 지연·처리량·운영 가용성의 수치 목표와 평가는 [Architecture Evaluation](06-architecture-evaluation.md)에 별도로 구분한다.
