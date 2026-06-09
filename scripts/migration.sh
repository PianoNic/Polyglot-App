#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/../src/Polyglot.Infrastructure"
MIGRATIONS_DIR="$PROJECT/Migrations"
REPO_ROOT="$SCRIPT_DIR/.."
COMMAND="${1:-}"

usage() {
    echo "Usage:"
    echo "  ./migration.sh add <Name> [--allow-empty-previous]  Add a new migration"
    echo "  ./migration.sh remove [--unseal]                    Remove the last migration"
    echo "  ./migration.sh list                                 List all migrations"
    echo "  ./migration.sh update [Name]                        Apply migrations (optionally to a specific one)"
    echo "  ./migration.sh drop --confirm                       Drop the database"
}

latest_migration_file() {
    if [ ! -d "$MIGRATIONS_DIR" ]; then
        return
    fi
    find "$MIGRATIONS_DIR" -maxdepth 1 -name "[0-9]*_*.cs" ! -name "*.Designer.cs" | sort | tail -n 1
}

has_empty_up_or_down() {
    # Strip all whitespace, then look for an empty Up/Down body
    tr -d '[:space:]' < "$1" | grep -qE 'void(Up|Down)\(MigrationBuilder[A-Za-z]+\)\{\}'
}

committed_to_main() {
    [ -n "$(git -C "$REPO_ROOT" log main --diff-filter=A --oneline -- "$1")" ]
}

has_flag() {
    local flag="$1"
    shift
    for arg in "$@"; do
        if [ "$arg" = "$flag" ]; then
            return 0
        fi
    done
    return 1
}

case "$COMMAND" in
    add)
        if [ -z "${2:-}" ] || [[ "$2" == --* ]]; then
            echo "Error: migration name required." >&2
            echo "Example: ./migration.sh add InitialCreate" >&2
            exit 1
        fi
        LATEST="$(latest_migration_file)"
        if [ -n "$LATEST" ] && has_empty_up_or_down "$LATEST" && ! has_flag --allow-empty-previous "$@"; then
            echo "Error: latest migration '$(basename "$LATEST")' has an empty Up() or Down()." >&2
            echo "This is the symptom of snapshot drift: an already-applied migration was edited or removed," >&2
            echo "so EF thinks the snapshot already matches the model and generates no-op migrations." >&2
            echo "Adding another migration now would bake the drift in and break fresh databases." >&2
            echo "" >&2
            echo "If this empty migration is intentional, re-run with:" >&2
            echo "  ./migration.sh add $2 --allow-empty-previous" >&2
            exit 1
        fi
        dotnet ef migrations add "$2" --project "$PROJECT"
        ;;
    remove)
        LATEST="$(latest_migration_file)"
        if [ -n "$LATEST" ] && committed_to_main "$LATEST" && ! has_flag --unseal "$@"; then
            echo "Error: '$(basename "$LATEST")' is committed to main - it is sealed." >&2
            echo "Removing an already-merged migration desyncs the model snapshot from databases" >&2
            echo "that have applied it. Create a new migration that undoes its changes instead." >&2
            echo "" >&2
            echo "If you really must remove it (e.g. reverting an unreleased merge), re-run with:" >&2
            echo "  ./migration.sh remove --unseal" >&2
            exit 1
        fi
        dotnet ef migrations remove --project "$PROJECT"
        ;;
    list)
        dotnet ef migrations list --project "$PROJECT"
        ;;
    update)
        if [ -n "${2:-}" ]; then
            dotnet ef database update "$2" --project "$PROJECT"
        else
            dotnet ef database update --project "$PROJECT"
        fi
        ;;
    drop)
        API_DIR="$SCRIPT_DIR/../src/Polyglot.API"
        SECRETS_ID="$(grep -o '<UserSecretsId>[^<]*' "$API_DIR/Polyglot.API.csproj" | sed 's/<UserSecretsId>//')"
        CONN=""
        for f in "$API_DIR/appsettings.Development.json" "$API_DIR/appsettings.json" "$HOME/.microsoft/usersecrets/$SECRETS_ID/secrets.json"; do
            if [ -f "$f" ]; then
                CONN="$(grep -o '"\(ConnectionStrings:\)\?PolyglotDatabase"[^,}]*' "$f" | sed 's/^"[^"]*"[[:space:]]*:[[:space:]]*//' | tr -d '"' | head -n 1)"
                if [ -n "$CONN" ]; then
                    break
                fi
            fi
        done
        echo "Target connection string: ${CONN:-<not configured - Npgsql defaults / environment>}"
        if ! has_flag --confirm "$@"; then
            echo "Error: 'drop' deletes the database above. Re-run with: ./migration.sh drop --confirm" >&2
            exit 1
        fi
        dotnet ef database drop --force --project "$PROJECT"
        ;;
    "")
        usage
        exit 1
        ;;
    *)
        usage
        exit 1
        ;;
esac
