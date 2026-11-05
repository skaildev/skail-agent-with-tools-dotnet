# Build context: repository root.
#   podman build -f src/SkailAgentWithTools.Web/Dockerfile -t agent-with-tools-web .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY src/SkailAgentWithTools.Contracts/SkailAgentWithTools.Contracts.csproj src/SkailAgentWithTools.Contracts/
COPY src/SkailAgentWithTools.Web/SkailAgentWithTools.Web.csproj src/SkailAgentWithTools.Web/
RUN dotnet restore src/SkailAgentWithTools.Web/SkailAgentWithTools.Web.csproj
COPY src/SkailAgentWithTools.Contracts src/SkailAgentWithTools.Contracts
COPY src/SkailAgentWithTools.Web src/SkailAgentWithTools.Web
RUN dotnet publish src/SkailAgentWithTools.Web/SkailAgentWithTools.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SkailAgentWithTools.Web.dll"]
