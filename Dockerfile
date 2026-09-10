FROM mcr.microsoft.com/dotnet/sdk:10.0.103 AS build
ARG TARGETARCH
WORKDIR /source
COPY Directory.Build.props Directory.Build.targets global.json NuGet.Config ./
COPY src/ src/
COPY samples/ samples/
RUN case "$TARGETARCH" in amd64) DSN_RUNTIME=linux-x64 ;; arm64) DSN_RUNTIME=linux-arm64 ;; *) echo "Unsupported TARGETARCH" >&2; exit 1 ;; esac \
 && dotnet restore src/Dsn.Host/Dsn.Host.csproj --locked-mode --configfile NuGet.Config --nologo \
 && dotnet publish src/Dsn.Host/Dsn.Host.csproj -c Release -o /out/host --no-restore --nologo -r "$DSN_RUNTIME" --self-contained false \
    -p:DebugType=None -p:DebugSymbols=false \
 && mkdir /out/data

# Keep ICU/time-zone support for plugins, without a shell or package manager.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /out/host/ ./
COPY --from=build --chown=$APP_UID:$APP_UID /out/data/ /data/
USER $APP_UID
EXPOSE 7070 7071
VOLUME ["/data"]
ENTRYPOINT ["dotnet", "Dsn.Host.dll"]
