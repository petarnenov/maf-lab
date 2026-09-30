# syntax=docker/dockerfile:1
# Build context: repository root (see compose/docker-compose.yml). Its own ignore file (Dockerfile.dockerignore)
# lets tests/ and web/ in: the runner restores their dependencies at build time, because at run time it has no
# network at all.

FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Maf.Lab.CoverageRunner/Maf.Lab.CoverageRunner.csproj -c Release -o /app /p:UseAppHost=false

# The web app's dependencies, installed once for the platform the runner runs on.
FROM node:24.21.0-bookworm-slim AS webdeps
WORKDIR /web
COPY web/package.json web/package-lock.json web/.npmrc ./
RUN npm ci --no-audit --no-fund

# The runner builds and tests, so it is an SDK image, with node beside it and the packages the tests need.
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble
COPY --from=webdeps /usr/local/bin/node /usr/local/bin/node
ENV NUGET_PACKAGES=/opt/nuget \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
# Restore every project the unit tests build, then keep only the package cache.
COPY global.json Directory.Build.props Directory.Packages.props /seed/
COPY src/ /seed/src/
COPY tests/ /seed/tests/
COPY tools/ /seed/tools/
RUN dotnet restore /seed/tests/Maf.Lab.Tests/Maf.Lab.Tests.csproj \
    && rm -rf /seed \
    && chmod -R a+rX /opt/nuget
COPY --from=webdeps /web/node_modules /opt/web/node_modules
COPY --from=build /app /app
# The repository is mounted read-only and owned by someone else; git must still read it.
RUN git config --system --add safe.directory '*'
# Model-written code runs here: as a user with nothing to its name, not as root.
RUN useradd --create-home --uid 10001 runner && mkdir -p /work && chown runner /work
USER runner
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    Runner__WorkRoot=/work \
    Runner__WebNodeModules=/opt/web/node_modules
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --retries=12 CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' >&3 && head -1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "Maf.Lab.CoverageRunner.dll"]
