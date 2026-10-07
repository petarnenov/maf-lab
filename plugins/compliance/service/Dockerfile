# syntax=docker/dockerfile:1
# Build context: the repository root (the plugin's compose.yml); it copies the core libraries it references and itself.
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
COPY plugins/compliance/service/ plugins/compliance/service/
# NuGet packages come from a cache shared by every image build (DECISIONS.md §58). The first restore fills it with
# anything it lacks, holding it alone so parallel builds never extract into the same folder; the second copies this
# project's packages out of it into the stage, so publish needs neither the cache nor the network.
ENV NUGET_PACKAGES=/nuget
RUN --mount=type=cache,id=maf-lab-nuget,target=/root/.nuget/packages,sharing=locked \
    NUGET_PACKAGES=/root/.nuget/packages dotnet restore plugins/compliance/service/Maf.Lab.ComplianceAgent.csproj \
    && dotnet restore plugins/compliance/service/Maf.Lab.ComplianceAgent.csproj --force --source /root/.nuget/packages
RUN dotnet publish plugins/compliance/service/Maf.Lab.ComplianceAgent.csproj -c Release -o /app /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
# A port that accepts is not a replica that can serve: /health says whether this one can reach the state it
# shares, and a replica that cannot should be routed around rather than sent work it will lose.
HEALTHCHECK --interval=10s --timeout=3s --retries=12 CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' >&3 && head -1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "Maf.Lab.ComplianceAgent.dll"]
