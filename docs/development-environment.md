# 격리된 개발 환경과 오프라인 빌드

DSN의 기본 개발 환경은 .NET 방식으로 SDK 버전·NuGet 의존성·캐시·CLI 상태를 프로젝트 단위로 관리한다. Python venv를 만들거나 활성화하지 않는다. 기본 Make 명령은 환경 wrapper를 거치며 전역 NuGet 설정이나 전역 Python package 설치를 변경하지 않는다. 컨테이너처럼 OS까지 격리하는 방식은 아니다.

## 구조와 결정

```text
dsn/
├─ global.json                 # .NET SDK 10.0, 최신 feature band 허용
├─ NuGet.Config                # 명시적 온라인 공급처
├─ NuGet.Offline.Config        # vendor/nuget만 사용
├─ src|samples|tests/**/packages.lock.json
├─ requirements-dev.lock       # Python 빌드 도구 버전·SHA-256
├─ Makefile                    # 개발·테스트·데모·오프라인 묶음
├─ make/sdk.mk                 # SDK 패키징과 예제
├─ make/deploy.mk              # Docker와 CI
├─ vendor/nuget/               # 반입 .nupkg (Git 제외)
├─ vendor/python/              # 반입 .whl (Git 제외)
└─ .dev/                       # 로컬 환경, Git/배포 제외
   ├─ dotnet/                  # 선택: 해당 OS용 SDK 직접 배치
   ├─ dotnet-home/             # CLI 상태
   ├─ nuget/                   # DSN 전용 package/http/plugin cache
   └─ python-tools/            # 선택: Python SDK 패키징 도구만
```

SDK는 `global.json`의 `latestFeature` 정책으로 **설치된 .NET 10.0 SDK 중 최신 버전(10.0.100 이상)**을 선택한다. 10.0.2xx/3xx 등 다른 feature band도 허용하므로 호스트에 10.0.1xx가 없어도 사용할 수 있다. .NET 11이나 preview로 자동 이동하지 않는다. 버전 해석은 [.NET global.json 규칙](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)을 따른다.

호환 SDK가 없으면 오류에 복구 방법을 표시한다. 해당 OS/CPU용 SDK를 설치하거나 전체 SDK를 `.dev/dotnet/`에 배치한 뒤 `make doctor`로 확인한다. Make wrapper가 프로젝트의 SDK를 우선 사용한다. `global.json`은 SDK를 다운로드하지 않는다. Docker build SDK는 10.0.103으로 고정하며 온라인에서 받는다. 현재 실행 검증 SDK는 Termux 10.0.111이다. 서로 다른 SDK의 바이너리 동일성까지 보장하지 않으며, SDK 변경 후 locked restore와 해당 기능 검사를 수행한다.

NuGet 의존성은 프로젝트마다 lock 파일로 고정한다. portable 빌드와 Docker의 linux-x64/linux-arm64를 같은 lock에 선언한다. 추가 RID는 RuntimeIdentifiers와 lock을 함께 갱신해야 한다. RID별 publish는 기본 restore로 전체 목록을 복원한 뒤 `--no-restore`로 실행한다(Dockerfile도 같은 순서). 정상 restore는 locked mode이며, 의존 변경 시 명시적으로 lock을 갱신한다([NuGet lock](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files)). 기본 bin/obj는 프로젝트별 SDK 관례대로 유지하고, publish 결과는 artifacts에 둔다. 사용자 데이터 data는 환경이나 clean 대상이 아니다. 기본 배포는 framework-dependent이므로 사용하지 않는 RID runtime/apphost pack을 다운로드하지 않는다. self-contained 배포는 별도 runtime pack 반입과 검증이 필요하다.

Source 런타임과 자동 데모는 시스템 Python의 표준 라이브러리만 사용한다. Python SDK wheel 패키징을 선택할 때만 `scripts/python-sdk.sh`가 hash로 고정한 빌드 도구를 `.dev/python-tools`에 설치하고 해당 명령에서 로딩한다. 전역 Python 설치를 변경하지 않으며 venv도 사용하지 않는다. 이 도구는 기본 .NET 빌드 환경과 분리되어 있다.

Python 선택 명령은 `python3` → `python` 순서로 Python 3.10 이상을 찾는다. 자동 선택이 맞지 않으면 `DSN_PYTHON=/absolute/path/to/python3 make demo`처럼 지정한다. .NET 전용 명령은 Python을 검색하거나 실행하지 않는다. 이전 구성에서 생성한 `.dev/venv`는 사용하지 않으며 삭제해도 된다. 이 디렉터리는 자동으로 다시 생성하지 않는다.

## 첫 준비와 일상 명령

기본 빌드·C# 테스트·publish에는 .NET SDK 10.0 (10.0.100 이상)와 Bash·GNU Make가 필요하다. Python 예제·데모·의존성 ZIP 도구는 Python 3.10+를, Python SDK 패키징과 전체 의존성 ZIP 생성은 pip도 사용한다. C++ SDK 검사는 CMake 3.16+와 C++17 compiler가 필요하다. Termux에서는 bionic용 .NET SDK 및 libsqlite를 사용한다. 일반 Linux용 SDK/native 파일을 Termux에 복사하여 사용하는 방식은 지원하지 않는다.

```sh
make env                       # .NET SDK 선택 확인, Python 설치·패키지 다운로드 없음
make env-check                 # SDK 누락·Python 이름 차이 회귀 검사
make doctor                    # 실제 dotnet 경로·SDK 버전·전용 cache 확인
make build DSN_BUILD_JOBS=1     # locked restore → Release build
make check DSN_BUILD_JOBS=1     # 단위·통합 검사
make demo DSN_BUILD_JOBS=1      # 자동 DUT 앱 + Workspace + View
make demo-check DSN_BUILD_JOBS=1
make publish DSN_BUILD_JOBS=1
make clean                     # C# build 출력 정리, 데이터 보존
```

파일을 나눠 실행해도 동일한 환경·의존성을 사용한다. 저장소 루트에서 실행한다.

```sh
make -f make/sdk.mk sdk-pack DSN_BUILD_JOBS=1
make -f make/sdk.mk sdk-check DSN_BUILD_JOBS=1
make -f make/deploy.mk deploy-help
make -f make/deploy.mk docker-build IMAGE=dsn:local
```

기존 `make sdk-pack`, `make deploy` 등의 루트 명령도 유지한다. `bash scripts/check.sh`와 `bash scripts/publish.sh`를 직접 실행해도 같은 환경을 사용한다. 수동 명령은 `bash scripts/dev.sh dotnet ...`, `bash scripts/dev.sh python ...`로 실행한다. 단순 `dotnet` 호출은 NuGet.Config·global.json을 사용하지만 wrapper의 CLI/HTTP cache 설정까지 적용하지는 않는다. SDK 선택과 NuGet 의존성 관리는 .NET 기본 설정 파일을 그대로 사용한다.

## 오프라인 반입과 검증

인터넷 가능한 준비 PC에서 **동일 소스/lock 버전**으로 실행한다.

```sh
make offline-pack DSN_BUILD_JOBS=1
```

직접·간접 NuGet 의존성과 Python 빌드 wheel을 vendor에 준비하고 `artifacts/dsn-offline-dependencies.zip` 및 `.zip.sha256`을 생성한다. manifest에는 패키지 hash와 SDK/lock 파일 hash를 기록한다. 패키지 원본 내 라이선스·고지는 유지한다. 큰 바이너리를 Git history에 누적하지 않기 위해 ZIP을 소스와 별도로 반입한다. **Git clone만으로 필요한 패키지가 반입되는 것은 아니다.**

대상 PC에서 ZIP을 저장소 루트에 풀고 실행한다.

```sh
make build OFFLINE=1 DSN_BUILD_JOBS=1
make -f make/sdk.mk sdk-check OFFLINE=1 DSN_BUILD_JOBS=1
make demo OFFLINE=1 DSN_BUILD_JOBS=1
make publish OFFLINE=1 DSN_BUILD_JOBS=1
make offline-check DSN_BUILD_JOBS=1
```

OFFLINE=1은 명시적 로컬 NuGet feed와 `pip --no-index`를 사용한다. 외부 보안 메타데이터 조회도 이 모드에서 끈다. 인터넷 가능한 곳에서 의존성 검토·갱신을 수행하고 반입해야 한다. manifest 검사만 하려면 `bash scripts/dev.sh python scripts/offline.py verify`를 실행한다.

`offline-check`는 **새 .dev/offline-check-* 환경**을 만들고 빈 NuGet cache·별도 Python SDK 도구 디렉터리에서 restore/build, C# 테스트, SDK 패키징·연동, E2E 데모·재시작을 검증한다. 일반 HTTP 프록시도 연결 불가 주소로 지정한다. 로컬 공급처만으로 동작함을 확인하는 검사이며 OS 네트워크 namespace 격리를 대신하지 않는다. 검사 환경은 확인을 위해 남긴다. 동시에 다른 빌드를 실행하지 않는다(bin/obj는 같은 checkout에서 공유).

SDK·Python·CMake/compiler·Make·OS native 의존성은 ZIP에 포함하지 않는다. 선택적 브라우저 검사도 Playwright·Chromium을 별도로 준비해야 한다. **Docker 빌드는 온라인 환경을 전제로 한다.** 기본 이미지와 NuGet 패키지는 빌드 중 온라인에서 받는다. `OFFLINE=1`은 로컬 .NET/Python 의존 복원에 적용된다.

## 의존성 변경

csproj 변경 후 `make deps-update`로 모든 lock을 명시적으로 갱신하고 diff를 검토한다. 이어 `make sdk-check`, `make demo-check`, `make offline-pack`, `make offline-check`를 수행한다. Python 빌드 도구를 변경할 때는 requirements-dev.lock의 버전/hash도 함께 갱신한다. OS/SDK가 바뀌었을 때는 cache와 선택적 Python 빌드 도구를 그대로 복제하지 말고 새 환경에서 검증한다. 필요하면 `DSN_DEV_DIR`에 별도 절대 경로를 지정할 수 있다.

현재 실행 검증 대상은 Termux Android arm64다. Windows의 native C++ SDK와 Docker 실행을 검증했다고 간주하지 않는다.
