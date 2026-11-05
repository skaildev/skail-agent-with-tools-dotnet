# Build context: repository root.
#   podman build -f src/SkailAgentWithTools.Worker/Dockerfile -t agent-with-tools-worker .
# dotnet publish runs in the SDK image; the final image only needs the .NET runtime.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY src/SkailAgentWithTools.Contracts/SkailAgentWithTools.Contracts.csproj src/SkailAgentWithTools.Contracts/
COPY src/SkailAgentWithTools.Worker/SkailAgentWithTools.Worker.csproj src/SkailAgentWithTools.Worker/
RUN dotnet restore src/SkailAgentWithTools.Worker/SkailAgentWithTools.Worker.csproj
COPY src/SkailAgentWithTools.Contracts src/SkailAgentWithTools.Contracts
COPY src/SkailAgentWithTools.Worker src/SkailAgentWithTools.Worker
RUN dotnet publish src/SkailAgentWithTools.Worker/SkailAgentWithTools.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SkailAgentWithTools.Worker.dll"]
