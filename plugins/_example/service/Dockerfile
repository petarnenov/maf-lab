# syntax=docker/dockerfile:1
# Build context: the repository root (the plugin's compose.yml), so the build keeps the shared pins (Directory.*.props,
# global.json); the only project it copies is this one.
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY plugins/_example/service/ service/
# NuGet packages come from the cache every image build shares (DECISIONS.md §58).
ENV NUGET_PACKAGES=/nuget
RUN --mount=type=cache,id=maf-lab-nuget,target=/root/.nuget/packages,sharing=locked \
    NUGET_PACKAGES=/root/.nuget/packages dotnet restore service/Example.McpServer.csproj \
    && dotnet restore service/Example.McpServer.csproj --force --source /root/.nuget/packages
RUN dotnet publish service/Example.McpServer.csproj -c Release -o /app /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --retries=12 CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' >&3 && head -1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "Example.McpServer.dll"]
