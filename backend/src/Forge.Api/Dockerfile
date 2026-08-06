# Build context is backend/ (docker-compose.yml at repo root sets it that way) so
# ProjectReference paths between Forge.Api/Forge.Domain/Forge.Infrastructure/Forge.Workflows
# resolve exactly as they do in a local `dotnet build`.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# csproj files copied first (restore layer caches independently of source edits).
COPY src/Forge.Api/Forge.Api.csproj src/Forge.Api/
COPY src/Forge.Domain/Forge.Domain.csproj src/Forge.Domain/
COPY src/Forge.Infrastructure/Forge.Infrastructure.csproj src/Forge.Infrastructure/
COPY src/Forge.Workflows/Forge.Workflows.csproj src/Forge.Workflows/
RUN dotnet restore src/Forge.Api/Forge.Api.csproj

COPY src/Forge.Api/ src/Forge.Api/
COPY src/Forge.Domain/ src/Forge.Domain/
COPY src/Forge.Infrastructure/ src/Forge.Infrastructure/
COPY src/Forge.Workflows/ src/Forge.Workflows/
RUN dotnet publish src/Forge.Api/Forge.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Program.cs applies EF Core migrations on startup and reads Postgres/Temporal/JWT
# config from ConnectionStrings__Forge, TEMPORAL_ADDRESS, FORGE_JWT_SECRET,
# FORGE_CORS_ORIGIN - all supplied by docker-compose.yml / .env, no baked-in host.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Forge.Api.dll"]
