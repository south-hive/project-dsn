# Workspace 개발

Workspace SDK는 기존 **Dsn.Contracts**다. 같은 인터페이스를 감싼 별도 SDK assembly를 만들지 않는다. plugin은 .NET 10 class library로 만들고 Contracts만 참조한다. Host·Runtime·Persistence 구현을 참조할 필요가 없다.

## 저장소 밖에서 시작하기

DSN 저장소에서 `make workspace-sdk`를 실행하면 `artifacts/sdk/Dsn.Contracts.0.1.0.nupkg`가 생성된다. 개발 프로젝트에서는 이 디렉터리를 로컬 feed로 사용한다.

```bash
dotnet new classlib -n Acme.Workspace -f net10.0
dotnet add Acme.Workspace package Dsn.Contracts --version 0.1.0 --source /absolute/path/to/dsn/artifacts/sdk
```

프로젝트의 핵심 설정은 다음과 같다. `EnableDynamicLoading`은 plugin의 추가 의존 파일을 준비하며, Contracts runtime은 Host가 공유한다([.NET plugin 안내](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support)).

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <EnableDynamicLoading>true</EnableDynamicLoading>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Dsn.Contracts" Version="0.1.0">
    <ExcludeAssets>runtime</ExcludeAssets>
  </PackageReference>
</ItemGroup>
```

`dotnet add`가 만든 PackageReference를 위 형태로 수정한다. 저장소 안의 예제는 같은 Contracts에 대한 ProjectReference를 사용한다.

## 구현과 수명

권장 출력 방식은 [BenchWorkspace.cs](../../samples/Dsn.Workspaces.Examples/BenchWorkspace.cs)의 `await message.EmitAsync(fields, token)`이다. Runtime이 현재 Workspace와 원본/실행 식별자를 추가하고 저장한다. plugin assembly 버전을 변경마다 갱신한다. 결과 이름 `message_id`, `processing_id`, `replay_id`, `source_id`, `time`, `event_type`, `processor_version`은 Runtime이 덮어쓴다.

기존 직접 저장 호환 예제는 [TemperatureWorkspace.cs](../../samples/temperature/Dsn.Workspaces.Temperature/TemperatureWorkspace.cs)에 있다.

| 계약 | 구현자의 역할 |
| --- | --- |
| `IWorkspacePlugin` | public 기본 생성자, `ApiVersion=1`, `Create(WorkspaceServices)` factory |
| `IWorkspace` | 고유한 `Name`, `ProcessAsync(message, cancellationToken)` |
| `IMessageContext` | Checkout으로 lease 획득, finally에서 Checkin, 독립 field를 EmitAsync로 출력 |
| `IPayloadLease` | Envelope와 원본 bytes의 검사되는 읽기 façade |
| `IRecordStore` | `RecordInput`의 독립된 scalar field 저장. `Fields.From` 사용 가능 |
| `IErrorSink` | 업무별 진단 보고. 처리 예외는 Runtime도 진단·격리 |

Source와 `Workspace.Name`, payload schema/version/단위를 합의한다. lease에서 필요한 값을 복사·해석하고 Checkin한 다음 저장한다. JsonDocument/lease를 해제한 뒤에도 record 값은 독립적으로 유효해야 한다. `ProcessAsync` 완료에는 context·lease를 쓰는 모든 작업이 포함되어야 하며 detached 작업에 넘기면 안 된다. 현재 Host는 순차 호출하며 병렬 호출을 지원한다고 가정하지 않는다.

## 배치

`dotnet publish Acme.Workspace -c Release -o plugin`으로 생성한 DLL·deps.json·추가 의존 DLL을 같은 디렉터리에 배치하고 Host 설정의 `plugins` 배열에 주 DLL 경로를 지정한다. Contracts.dll은 Host가 제공한다. 상대 경로는 실행 디렉터리 기준이다. 명시한 plugins 배열은 기본 echo/hex/bench plugin 목록을 대체한다.

Docker에서는 plugin 디렉터리를 읽기 전용 mount하고 컨테이너 안의 DLL 경로를 `settings.docker.json`에 넣는다. HTTP 사용자에게 새 Workspace 이름의 조회 권한도 부여한다. 기본 Host 이미지에 업무 plugin을 자동 포함하지 않는다.

패키지 버전 `0.1.0`, plugin API 버전 `1`, 업무 payload의 `temperature.v1`은 서로 다른 버전이다. 현재 제공하는 패키지와 일치하는 Host로 검증한다. 예제는 `make sdk-check`에서 저장소 밖의 NuGet 소비 프로젝트로도 빌드·로딩한다.

## Filter 개발

Workspace는 payload를 해석하고, Filter는 이미 해석된 scalar record를 변환한다. `EmitAsync`와 legacy `WorkspaceServices.Records` 모두 설정된 Filter → Sink를 거친다. 다음처럼 같은 Contracts만 참조하는 DLL에 Filter를 추가하고 `plugins`에 배치한다.

```csharp
using System.Text.Json;
using Dsn.Contracts;

public sealed class ExampleFilterPlugin : IFilterPlugin
{
    public int ApiVersion => 1;
    public string Type => "example-tag";
    public IRecordFilter Create(JsonElement options) => new ExampleFilter();
}

public sealed class ExampleFilter : IRecordFilter
{
    public ValueTask<RecordInput?> ProcessAsync(RecordInput record, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var fields = record.Fields.ToDictionary(p => p.Key, p => p.Value.Clone());
        fields["classification"] = JsonSerializer.SerializeToElement("observed");
        return ValueTask.FromResult<RecordInput?>(new(record.Workspace, fields));
    }
}
```

설정의 Filter 항목은 `{"type":"example-tag","options":{}}`다. factory에서 옵션을 검증하고 잘못된 옵션은 예외로 시작을 중단한다. `null` 반환은 해당 결과를 제외한다. 원본은 `retainRaw=true`일 때 이미 보존되어 있다. Filter 객체는 해당 pipeline에서 재사용되고 호출은 순차이며 작업은 반환 전에 완료되어야 한다.

입력 field는 읽기 전용 scalar 사본이다. 새 결과에도 같은 Workspace를 사용한다. `message_id`, `processing_id`, `replay_id`, `source_id`, `time`, `event_type`, `processor_version`, `pipeline_revision`, `node_id`, `record_id`는 시스템 예약 field로, Filter가 변경·삭제해도 복원된다. SQLite는 최종 node/record ID를 직접 부여한다. Filter assembly 버전을 갱신해야 변경된 구현이 pipeline revision에 반영된다.

한 Filter는 1→0 또는 1이며 다중 record 출력·window 집계·분기·동시 실행을 가정하지 않는다. 예외는 PIPELINE_FAILED로 보고되고 호출 실패로 전파된다. 기본 Filter 옵션과 Sink의 부분 성공 규칙은 [파이프라인 예제](../../samples/pipeline/README.md)를 따른다.
