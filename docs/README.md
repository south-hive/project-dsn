# DSN Architecture Documentation

현재 C# 구현의 목적, 설계 근거, 구조와 검증 범위를 설명한다. 기준일은 2026-09-09이며 기능 검증 기록은 2026-09-08 실행 결과다.

| 문서 | 다루는 질문 |
| --- | --- |
| [1. Project Overview](01-project-overview.md) | 무엇을 왜 만들며 범위는 어디까지인가? |
| [2. System Overview](02-system-overview.md) | 외부와 어떻게 연결되고 어떤 데이터를 다루는가? |
| [3. Architectural Drivers](03-architectural-drivers.md) | 어떤 사용 사례·품질 요구·제약이 설계를 결정하는가? |
| [4. Top Level Design Description](04-top-level-design.md) | 전체 구조·동작·배포 방식과 핵심 결정은 무엇인가? |
| [5. Component Level Design Description](05-component-design.md) | 각 컴포넌트의 내부 요소와 설계 이유는 무엇인가? |
| [6. Architecture Evaluation](06-architecture-evaluation.md) | 설계가 요구를 만족한다는 근거와 남은 위험은 무엇인가? |

그림은 Mermaid로 작성했다. 구조 그림의 화살표는 의존/호출, 시퀀스 그림은 시간 순서이며, 다른 의미를 쓰는 그림은 본문에 명시한다. 문서의 컴포넌트는 논리 단위로, 반드시 별도 assembly나 프로세스를 뜻하지 않는다.

[실행 안내](../README.md) · [설정 기본값](../settings.example.json) · [공개 계약 코드](../src/Dsn.Contracts/Contracts.cs)
