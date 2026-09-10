# DSN 검증

저장소 루트에서 실행한다. 실제 드라이버 연동·시험은 포함하지 않는다.

| 명령 | 범위 |
| --- | --- |
| `make demo-check` | DUT별 앱 생성·네 구간 판정·Filter·원본/결과 대응·HTTP·재시작 |
| `make offline-check` | 빈 프로젝트 NuGet cache와 별도 SDK 도구 디렉터리에서 로컬 feed만으로 C#·SDK·E2E 전체 검증 |
| `make check` | C# Release build, 단위 18 + 통합 10개 그룹 |
| `make sdk-check` | C# 검사·SDK 패키징·SDK 검사 6개 |
| `make pipeline-check` | C# 검사 + 앱 Source → ordered Filters → SQLite·조건 변경 replay·재시작 |
| `make bench-check` | C# 검사 + 세 Python 앱 프로세스 수집·원본·재시작 |
| `make ci` | C#·SDK 검사 성공 후 Docker 이미지 빌드 |

Make와 check.sh는 프로젝트 전용 환경을 사용한다. [환경 준비와 OFFLINE=1](../docs/development-environment.md). 작은 PC/Termux에서는 `DSN_BUILD_JOBS=1`을 지정한다. 로컬 HTTP 검사는 환경 프록시를 우회한다.

[SDK 검사](sdk/test_sources.py)는 binary/복사·포화·전송 실패·종료·동시 발행과 C++·Python → 실제 Host → Workspace → HTTP/재시작을 확인한다. Python wheel, 설치 CMake library, Contracts NuGet을 별도 소비 프로젝트에서 사용한다. `tests/pipeline.py`는 제외된 원본의 재처리, 안정적인 ID와 변경 revision, SQLite BLOB까지 검사한다.

선택적 브라우저 검사:

```sh
npm install --prefix artifacts/browser-tools playwright
artifacts/browser-tools/node_modules/.bin/playwright install chromium
python tests/bench.py --browser
```

`DSN_BROWSER_EXECUTABLE`로 별도 Chromium 경로를 지정할 수 있다. Workspace·처리 경로, 페이지 Source 필터/차트, 다운로드, 원본 재처리, 모바일 가로 넘침을 확인한다. 스크린샷은 `artifacts/presenter-*.png`에 생성한다.

현재 실행 근거·한계는 [Architecture Evaluation](../docs/06-architecture-evaluation.md)에 있다. `plan.md`, `report.md`, `defects.md`는 과거 TypeScript prototype 기록이며 현재 C# 결과로 사용하지 않는다.
