$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Compose = Join-Path $RepoRoot "compose.dev.yml"
$Realms = Join-Path $RepoRoot "keycloak\realms"

docker compose -f $Compose stop keycloak
docker compose -f $Compose run --rm -v "${Realms}:/export" keycloak export --dir /export --users skip
docker compose -f $Compose up -d --wait keycloak
