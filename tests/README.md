# DSN tests

검증 범위와 합격 조건은 [테스트 계획](plan.md), 최신 판정은 [결과](report.md), 재현된 문제는 [결함 목록](defects.md)에 기록한다. 테스트 코드는 `prototype/test/`에 유지한다.

최초 실행 전 `prototype/`에서 `npm ci`로 고정 의존성을 설치한다. 저장소 루트에서 다음 명령을 실행한다.

```bash
node tests/run.mjs
```

빌드, Verification 27건, Validation 19건씩 3회, 데모를 실행한다. 예상 시험 수와 pass/fail/skip/cancelled/todo를 검사하며 실패 또는 단계별 60초 timeout 시 exit 1이다. 시험 추가 시 실행 도구의 예상 건수도 갱신한다. Validation 반복은 간헐적인 연결·종료 실패를 찾기 위한 것이며 성능 측정이 아니다.

각 실행의 `results/<UTC시각-pid>/summary.json`에 환경, commit, 작업 트리 상태, 소스 SHA256, 명령·종료 코드·판정을 기록한다. 단계별 stdout/stderr는 같은 폴더에 보존한다. 이 폴더는 Git에서 제외되며 검토용 요약·결함 문서는 추적한다. 실패 로그를 덮어쓰지 않는다.

단일 검증은 `prototype/`에서 `npm run verify`, `npm run validate`로 실행한다. 이 명령들은 기존 컴파일 결과를 사용하므로 소스 수정 후 `npm run build`를 먼저 실행한다.
