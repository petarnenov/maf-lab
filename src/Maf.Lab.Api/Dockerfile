# Build context: repository root (see compose/docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Maf.Lab.Api/Maf.Lab.Api.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --retries=12 CMD ["bash", "-c", ":> /dev/tcp/127.0.0.1/8080"]
ENTRYPOINT ["dotnet", "Maf.Lab.Api.dll"]
