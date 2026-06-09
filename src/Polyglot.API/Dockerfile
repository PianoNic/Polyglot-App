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
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Polyglot.API.dll"]
