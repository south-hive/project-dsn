# DSN practice 테스트 결과

## 판정

2026-09-08 실행 결과, **46개 고유 테스트가 통과했다.** Verification 27건과 Validation 19건이며 Validation을 3회 반복하여 총 84회 시험 실행을 확인했다. 실패·skip·cancelled·todo·timeout은 모두 0이다. 빌드와 실제 RPC/HTTP 데모도 성공했다.

추가한 14건 중 B01에서 payload 한도 설정 검증 누락 1건을 발견해 수정했다. 최종 시험 범위에서 미해결 실패는 없다. 이는 prototype의 시험 범위 내 판정이며 모든 입력의 무결함이나 production 성능 적합성을 보장하지 않는다.

## 실행 근거

| 항목 | 결과 |
| --- | --- |
| 브랜치 / 기준 commit | practice / `59db15a8ae16ca87d53c37a0089e5a29ca4a5e7b`에 이번 미커밋 변경 적용 |
| 환경 | Android arm64 Termux, Node v26.4.0, npm 11.19.1, TypeScript 5.9.3 |
| 실행 명령 | 저장소 루트에서 `node tests/run.mjs` |
| 수정 전 실행 | `2026-09-08T07-33-11.637Z-21872`: Verification 26/27, 종합 exit 1 |
| 수정 후 실행 | `2026-09-08T07-34-06.322Z-22492`: 전체 통과, 종합 exit 0 |

각 실행의 `tests/results/<실행 ID>/summary.json`과 단계별 stdout/stderr에 원시 결과가 있다. Git commit만으로 이번 작업 상태를 식별할 수 없으므로 실행 시 작업 트리 상태와 파일별 SHA256도 기록했다. 원시 로그는 로컬 산출물이며 Git에 포함하지 않는다.

| 단계 | 판정 | 시험 수 | 실행 시간 |
| --- | --- | --- | --- |
| strict 빌드 | 통과 | 타입 fixture 포함 | 2,775 ms |
| Verification | 통과 | 27/27 | 874 ms |
| Validation 1 | 통과 | 19/19 | 1,916 ms |
| Validation 2 | 통과 | 19/19 | 1,902 ms |
| Validation 3 | 통과 | 19/19 | 1,862 ms |
| 데모 | 통과 | TCP → echo/hex → HTTP View → 종료 | 778 ms |

위 시간은 테스트 프로세스 실행 시간이며 DSN 처리·전송 지연 측정값이 아니다.

## 검증 범위와 수정

기존 V01–V20, A01–A12에 [계획](plan.md)의 B01–B07, N01–N07을 추가했다. 실제 소켓으로 framing 경계, UTF-8 분할, session 제한·회복, 원본 한도 폐기·회복, 활성 작업과 동시 종료, 포트 충돌·재사용, Mock 출력 제한을 검증했다. 내부 시험은 FIFO·공유 소유권·저장 실패·오류 동일성·Admin 재시도를 확인했다.

DEF-001은 잘못된 `maxPayloadBytes` 설정이 길이 검사를 우회하는 문제다. 생성자에서 0 이상의 안전한 정수만 허용하도록 수정했고 B01로 회귀를 검증했다. 설정 0은 빈 payload만 허용한다. 상세 재현·영향은 [결함 목록](defects.md)에 있다.

데모 결과는 `created=1`, `reclaimed=1`, `checkouts=2`, `checkins=3`, `references=0`, `responseBytes=0`이었다. 두 Workspace의 message_id는 1로 같았다. checkins에는 executor root 반환이 포함된다.

실행 도구는 수정 전 Verification 실패를 종합 exit 1로 판정하면서 독립적인 Validation·데모는 계속 실행했다. 수정 후에는 예상 시험 수와 모든 종료 코드를 검사해 exit 0으로 종료했다. 실제 60초 timeout 유발 시험은 수행하지 않았다.

## 미검증·잔여 위험

C#의 실제 메모리·동시 ref count, Docker 배포, native Source/kernel/eBPF, Source no-blocking·지연 상한, 영속 저장·권한은 이번 대상이 아니다. 동기적으로 멈추는 플러그인과 운영 모니터링·자동 조치도 평가하지 않았다. QA 위험의 production 상태는 변경하지 않는다.

정상 종료 후 참조 수와 session 정리는 해당 시험에서 확인했다. OS 전체의 모든 자원 누수나 장시간 안정성을 증명하는 시험은 아니다. 성능·장기 운용 평가는 기존 후속 계획으로 남긴다.
