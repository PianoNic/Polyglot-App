# Build context is src/ (the API references sibling projects):
#   docker build -f Polyglot.API/Dockerfile .
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Polyglot.API/Polyglot.API.csproj", "Polyglot.API/"]
COPY ["Polyglot.Application/Polyglot.Application.csproj", "Polyglot.Application/"]
COPY ["Polyglot.Infrastructure/Polyglot.Infrastructure.csproj", "Polyglot.Infrastructure/"]
COPY ["Polyglot.Domain/Polyglot.Domain.csproj", "Polyglot.Domain/"]
RUN dotnet restore "Polyglot.API/Polyglot.API.csproj"
COPY . .
WORKDIR /src/Polyglot.API
RUN dotnet publish "Polyglot.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
# pg_dump (matching the postgres 18 server) for the scheduled backup service
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && install -d /usr/share/postgresql-common/pgdg \
    && curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc \
    && . /etc/os-release \
    && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt $VERSION_CODENAME-pgdg main" > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client-18 \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Polyglot.API.dll"]
