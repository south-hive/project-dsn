# 결함 및 미검증 사항

## DEF-001 — payload 한도 설정 검증 누락 — 수정

- 영향: `new EnvelopeV1(NaN)`과 Infinity를 허용하여 payload 한도 검사가 우회될 수 있었다. 음수·소수도 시작 시 오류 없이 수용됐다. 기본 16 KiB 설정에는 해당하지 않는다. TCP frame 한도는 별도로 남아 있다.
- 재현: B01의 잘못된 설정 생성 거부 assertion이 실패했다. 직접 decoder에 NaN 한도와 20,000 bytes payload를 전달했을 때 20,000 bytes가 수용되는 것도 확인했다.
- 원인: 생성자에서 한도를 검사하지 않고 길이 비교에 사용했다.
- 수정: 0 이상의 안전한 정수만 허용하고 그 외에는 생성 시 예외를 발생시킨다. 0은 빈 payload만 허용한다. wire·Workspace API는 변경하지 않는다.
- 회귀 시험: B01의 한도 직전/일치/초과 및 NaN·Infinity·음수·소수·0 설정. 수정 전 실패 로그는 `results/2026-09-08T07-33-11.637Z-21872/verification.stdout.log`에 있다.
- 재검증 결과: [최신 보고서](report.md).

## 알려진 한계 — 이번에 발견한 구현 결함과 구분

| 항목 | 상태 | 근거·후속 |
| --- | --- | --- |
| 동기 무한 Workspace | 미검증, 설계 위험 | event loop·종료 timer 정지 가능; QA-05, RISK-06 |
| 실제 RSS와 C# 동시 ref count | 미검증 | ledger 검증은 GC·pool·동시 접근 검증을 대체하지 않음 |
| Source no-blocking 및 지연 상한 | 미검증 | 외부 Source와 실제 배포 환경 책임 |
| 영속 저장·사용자 권한 | stub 범위 밖 | 메모리 record·field 선택만 구현 |
| Admin 실시간 전달·심각도 일반 정책 | 후속 결정 | 현재 snapshot 투영과 제한된 session 종료 정책 유지 |

전체 입력에서 결함이 없음을 보장하지 않는다. 이번 시험 범위 안의 미해결 실패 여부는 최신 보고서에서 판정한다.
