FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/ src/
RUN dotnet publish src/Dsn.Host/Dsn.Host.csproj -c Release -o /out/host --nologo \
 && dotnet publish src/Dsn.Mock/Dsn.Mock.csproj -c Release -o /out/mock --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out/host/ ./
COPY --from=build /out/mock/ ./mock/
RUN mkdir /data && chown $APP_UID /data
USER $APP_UID
EXPOSE 7070 7071 7072
VOLUME ["/data"]
ENTRYPOINT ["dotnet", "Dsn.Host.dll"]
