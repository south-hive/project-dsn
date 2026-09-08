# DSN 개발 문서 입구

부서원용 문서는 **전체를 보고 → 기능을 나누고 → 맡을 일을 선택하고 → 자기 부분을 구현·검증하는 순서**로 재구성했다.

```mermaid
flowchart LR
    A["전체 목적·사용자"] --> B["기능 분해도"]
    B --> C["작업 배분도"]
    C --> D["담당자별 부분 그림·지시서"]
```

| 순서 | 문서 | 확인할 내용 |
| --- | --- | --- |
| 1 | [개발 안내: 전체에서 시작](../development/README.md) | DSN이 하는 일과 네 단계 |
| 2 | [기능을 확대해서 보기](../development/01-system-breakdown.md) | 받기·해석·저장·View·공통 기반 |
| 3 | [나눠 맡고 다시 합치기](../development/02-work-division.md) | 9개 역할, 경계, 공유 파일, 통합 순서 |
| 4 | [내 지시서 선택](../development/02-work-division.md#담당-지시서-선택) | 부분 그림·입출력·코드·작업 순서·완료 조건 |

기존 14개 설계 문서와 track/task 기록은 [상세 설계 참조 목록](reference-index.md)에 보존했다. 현재 C# 구현 규격은 [구현 계약](../implementation/contracts.md), 실제 통과 범위는 [검증 보고서](../implementation/verification.md)에서 확인한다.
