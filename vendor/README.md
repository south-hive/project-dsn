# 오프라인 의존 패키지 / Offline dependencies

이 디렉터리에는 `make offline-pack`으로 준비한 NuGet `.nupkg`와 Python 빌드 도구 `.whl`을 둔다. 실제 패키지·manifest는 Git에 넣지 않고 `artifacts/dsn-offline-dependencies.zip`으로 반입한다. 원본 패키지의 라이선스와 고지를 그대로 보존한다.

- `nuget/`: 프로젝트 lock 파일에서 추출한 직접·간접 NuGet 의존성
- `python/`: requirements-dev.lock의 해시로 고정한 Python 빌드 도구
- `manifest.json`: SDK/lock 파일 대응과 각 패키지 SHA-256

ZIP을 **저장소 루트**에 풀고 `make offline-check DSN_BUILD_JOBS=1`로 검증한다. SDK·Python·CMake/compiler·Make·OS 라이브러리와 Docker 이미지는 이 묶음에 포함되지 않는다. 상세 준비는 [개발 환경](../docs/development-environment.md)을 따른다.
