param(
    [Parameter(Position=0)]
    [string]$Command,

    [Parameter(Position=1, ValueFromRemainingArguments=$true)]
    [string[]]$Rest
)

$Project = Join-Path $PSScriptRoot "..\src\Polyglot.Infrastructure"
$MigrationsDir = Join-Path $Project "Migrations"
$RepoRoot = Join-Path $PSScriptRoot ".."

function Show-Usage {
    Write-Host "Usage:"
    Write-Host "  migration add <Name> [--allow-empty-previous]  Add a new migration"
    Write-Host "  migration remove [--unseal]                    Remove the last migration"
    Write-Host "  migration list                                 List all migrations"
    Write-Host "  migration update [Name]                        Apply migrations (optionally to a specific one)"
    Write-Host "  migration drop --confirm                       Drop the database"
}

function Get-LatestMigrationFile {
    if (-not (Test-Path $MigrationsDir)) {
        return $null
    }
    return Get-ChildItem $MigrationsDir -Filter "*.cs" |
        Where-Object { $_.Name -match '^\d+_' -and $_.Name -notlike '*.Designer.cs' } |
        Sort-Object Name |
        Select-Object -Last 1
}

function Test-EmptyUpOrDown([string]$Path) {
    $content = Get-Content $Path -Raw
    return [regex]::IsMatch($content, 'void (Up|Down)\(MigrationBuilder \w+\)\s*\{\s*\}')
}

function Test-CommittedToMain([string]$Path) {
    $log = git -C $RepoRoot log main --diff-filter=A --oneline -- $Path
    return [bool]$log
}

function Get-TargetConnectionString {
    $apiDir = Join-Path $PSScriptRoot "..\src\Polyglot.API"
    $candidates = @(
        (Join-Path $apiDir "appsettings.Development.json"),
        (Join-Path $apiDir "appsettings.json")
    )
    $csprojContent = Get-Content (Join-Path $apiDir "Polyglot.API.csproj") -Raw
    if ($csprojContent -match '<UserSecretsId>([^<]+)</UserSecretsId>') {
        $candidates += Join-Path $env:APPDATA "Microsoft\UserSecrets\$($Matches[1])\secrets.json"
    }
    foreach ($file in $candidates) {
        if (-not (Test-Path $file)) {
            continue
        }
        $json = Get-Content $file -Raw | ConvertFrom-Json
        $nested = $json.ConnectionStrings.PolyglotDatabase
        if ($nested) {
            return $nested
        }
        $flat = $json.'ConnectionStrings:PolyglotDatabase'
        if ($flat) {
            return $flat
        }
    }
    return $null
}

switch ($Command) {
    "add" {
        if (-not $Rest -or -not $Rest[0] -or $Rest[0].StartsWith("--")) {
            Write-Host "Error: migration name required." -ForegroundColor Red
            Write-Host "Example: migration add InitialCreate"
            exit 1
        }
        $latest = Get-LatestMigrationFile
        if ($latest -and (Test-EmptyUpOrDown $latest.FullName) -and $Rest -notcontains "--allow-empty-previous") {
            Write-Host "Error: latest migration '$($latest.Name)' has an empty Up() or Down()." -ForegroundColor Red
            Write-Host "This is the symptom of snapshot drift: an already-applied migration was edited or removed,"
            Write-Host "so EF thinks the snapshot already matches the model and generates no-op migrations."
            Write-Host "Adding another migration now would bake the drift in and break fresh databases."
            Write-Host ""
            Write-Host "If this empty migration is intentional, re-run with:"
            Write-Host "  migration add $($Rest[0]) --allow-empty-previous"
            exit 1
        }
        dotnet ef migrations add $Rest[0] --project $Project
    }
    "remove" {
        $latest = Get-LatestMigrationFile
        if ($latest -and (Test-CommittedToMain $latest.FullName) -and $Rest -notcontains "--unseal") {
            Write-Host "Error: '$($latest.Name)' is committed to main - it is sealed." -ForegroundColor Red
            Write-Host "Removing an already-merged migration desyncs the model snapshot from databases"
            Write-Host "that have applied it. Create a new migration that undoes its changes instead."
            Write-Host ""
            Write-Host "If you really must remove it (e.g. reverting an unreleased merge), re-run with:"
            Write-Host "  migration remove --unseal"
            exit 1
        }
        dotnet ef migrations remove --project $Project
    }
    "list" { dotnet ef migrations list --project $Project }
    "update" {
        if ($Rest -and $Rest[0]) {
            dotnet ef database update $Rest[0] --project $Project
        } else {
            dotnet ef database update --project $Project
        }
    }
    "drop" {
        $conn = Get-TargetConnectionString
        Write-Host "Target connection string: $(if ($conn) { $conn } else { '<not configured - Npgsql defaults / environment>' })"
        if ($Rest -notcontains "--confirm") {
            Write-Host "Error: 'drop' deletes the database above. Re-run with: migration drop --confirm" -ForegroundColor Red
            exit 1
        }
        dotnet ef database drop --force --project $Project
    }
    default { Show-Usage; if ($Command) { exit 1 } }
}
