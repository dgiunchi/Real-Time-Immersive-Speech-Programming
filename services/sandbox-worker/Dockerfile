# Build stage (needs network to restore + pull base).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY harness/Harness.csproj ./
RUN dotnet restore
COPY harness/Program.cs ./
RUN dotnet publish -c Release -o /app /p:UseAppHost=false

# Runtime stage: minimal runtime, unprivileged. The container is further locked
# down at `docker run` time by DockerSandboxRunner (no-net, read-only, caps, pids).
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app ./
# dotnet needs a writable, isolated HOME; DockerSandboxRunner mounts /tmp as tmpfs.
ENV DOTNET_CLI_HOME=/tmp HOME=/tmp DOTNET_NOLOGO=1
USER 65534:65534
ENTRYPOINT ["dotnet", "/app/Harness.dll"]
