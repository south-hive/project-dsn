# 격리된 개발 환경과 오프라인 빌드

DSN은 SDK 버전·패키지 공급처·캐시·Python 도구를 프로젝트 단위로 관리한다. 기본 Make 명령은 환경 wrapper를 거치며 전역 NuGet 설정이나 전역 Python package 설치를 변경하지 않는다. 컨테이너처럼 OS까지 격리하는 방식은 아니다.

## 구조와 결정

```text
dsn/
├─ global.json                 # .NET SDK 10.0.1xx 계열, patch만 허용
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
   └─ venv/                    # Python 가상환경
```

SDK는 매번 복제하지 않고 설치된 **10.0.1xx 계열**을 재사용한다. Termux SDK 10.0.111과 공식 Docker SDK 10.0.103의 배포 차이 때문에 같은 feature band 안의 patch만 허용한다. 필요하면 `.dev/dotnet/`에 호환 SDK를 배치하여 Make wrapper가 우선 사용하게 할 수 있다. 다른 feature band로 자동 이동하지 않으며 호환 SDK가 없으면 실패한다([global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)). Docker build 이미지는 실제 제공되는 10.0.103 태그로 고정한다. 개발 PC에서 완전히 같은 SDK가 필요하면 동일 SDK를 .dev/dotnet에 배치한다. 이 구성만으로 서로 다른 OS/SDK patch의 바이너리 동일성까지 보장하지 않는다. SDK 계열 업데이트는 global.json·Dockerfile·의존 lock 및 검증을 함께 갱신하는 변경이다.

NuGet 의존성은 프로젝트마다 lock 파일로 고정한다. portable 빌드와 Docker의 linux-x64/linux-arm64를 같은 lock에 선언한다. 추가 RID는 RuntimeIdentifiers와 lock을 함께 갱신해야 한다. RID별 publish는 기본 restore로 전체 목록을 복원한 뒤 `--no-restore`로 실행한다(Dockerfile도 같은 순서). 정상 restore는 locked mode이며, 의존 변경 시 명시적으로 lock을 갱신한다([NuGet lock](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files)). 기본 bin/obj는 프로젝트별 SDK 관례대로 유지하고, publish 결과는 artifacts에 둔다. 사용자 데이터 data는 환경이나 clean 대상이 아니다. 기본 배포는 framework-dependent이므로 사용하지 않는 RID runtime/apphost pack을 다운로드하지 않는다. self-contained 배포는 별도 runtime pack 반입과 검증이 필요하다.

Python venv는 이 PC에서 생성하며 다른 PC로 복사하지 않는다([Python venv](https://docs.python.org/3/library/venv.html)). Source 런타임은 표준 라이브러리만 사용하고, SDK wheel 패키징에만 hash로 고정한 setuptools/wheel/packaging을 설치한다. 별도 shell activation 없이 Make가 올바른 환경을 사용한다.

## 첫 준비와 일상 명령

사전에 .NET SDK 10.0.1xx, Python 3.10+와 venv/ensurepip, Bash·GNU Make가 필요하다. C++ SDK 검사는 CMake 3.16+와 C++17 compiler도 필요하다. Termux에서는 bionic용 .NET SDK 및 libsqlite를 사용한다. 일반 Linux용 SDK/native 파일을 Termux에 복사하여 사용하는 방식은 지원하지 않는다.

```sh
make env                       # venv 생성·SDK 선택 확인, 패키지 다운로드 없음
make doctor                    # 실제 SDK/Python 경로·전용 cache 확인
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

기존 `make sdk-pack`, `make deploy` 등의 루트 명령도 유지한다. `bash scripts/check.sh`와 `bash scripts/publish.sh`를 직접 실행해도 같은 환경을 사용한다. 수동 명령은 `bash scripts/dev.sh dotnet ...`, `bash scripts/dev.sh python ...`로 실행한다. 단순 `dotnet` 호출은 NuGet.Config·global.json을 사용하지만 wrapper의 CLI/HTTP cache와 Python 격리까지 적용하지는 않는다.

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

`offline-check`는 **새 .dev/offline-check-* 환경**을 만들고 빈 NuGet/pip cache·새 venv에서 restore/build, C# 테스트, SDK 패키징·연동, E2E 데모·재시작을 검증한다. 일반 HTTP 프록시도 연결 불가 주소로 지정한다. 로컬 공급처만으로 동작함을 확인하는 검사이며 OS 네트워크 namespace 격리를 대신하지 않는다. 검사 환경은 확인을 위해 남긴다. 동시에 다른 빌드를 실행하지 않는다(bin/obj는 같은 checkout에서 공유).

SDK·Python·CMake/compiler·Make·OS native 의존성은 ZIP에 포함하지 않는다. 선택적 브라우저 검사도 Playwright·Chromium을 별도로 준비해야 한다. **Docker 빌드는 온라인 환경을 전제로 한다.** 기본 이미지와 NuGet 패키지는 빌드 중 온라인에서 받는다. `OFFLINE=1`은 로컬 .NET/Python 의존 복원에 적용된다.

## 의존성 변경

csproj 변경 후 `make deps-update`로 모든 lock을 명시적으로 갱신하고 diff를 검토한다. 이어 `make sdk-check`, `make demo-check`, `make offline-pack`, `make offline-check`를 수행한다. Python 빌드 도구를 변경할 때는 requirements-dev.lock의 버전/hash도 함께 갱신한다. OS/SDK가 바뀌었을 때는 venv와 cache를 그대로 복제하지 말고 새 환경에서 검증한다. 필요하면 `DSN_DEV_DIR`에 별도 절대 경로를 지정할 수 있다.

현재 실행 검증 대상은 Termux Android arm64다. Windows의 native C++ SDK와 Docker 실행을 검증했다고 간주하지 않는다.
