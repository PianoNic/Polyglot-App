# Keycloak — Dev Setup

Keycloak runs as a container alongside the API and database (see `compose.dev.yml`).
The realm, client, roles, and a default admin user are imported automatically from
`keycloak/polyglot-realm.json` on first start — **no manual setup required**.

For the general dev environment setup, see [`dev_setup.md`](./dev_setup.md).

## Start

```bash
docker compose -f compose.dev.yml up -d
```

Keycloak is available at `http://localhost:8080` once started (takes ~30s the first time).

## What got imported

The compose file mounts `keycloak/polyglot-realm.json` and starts Keycloak with `--import-realm`. This creates:

| Item            | Value                                                  |
|-----------------|--------------------------------------------------------|
| Realm           | `polyglot`                                             |
| Client ID       | `polyglot` (public, PKCE)                     |
| Roles           | `user` (default), `admin`                              |
| Default admin   | `admin@polyglot.local` / `admin` (assigned both roles) |
| Registration    | Open — anyone can sign up at `/realms/polyglot/...`    |

## Endpoint URLs

| Field              | Value                                                                              |
|--------------------|------------------------------------------------------------------------------------|
| Authority (Issuer) | `http://localhost:8080/realms/polyglot`                                            |
| OIDC Discovery     | `http://localhost:8080/realms/polyglot/.well-known/openid-configuration`           |
| Authorization      | `http://localhost:8080/realms/polyglot/protocol/openid-connect/auth`               |
| Token              | `http://localhost:8080/realms/polyglot/protocol/openid-connect/token`              |
| UserInfo           | `http://localhost:8080/realms/polyglot/protocol/openid-connect/userinfo`           |
| Logout             | `http://localhost:8080/realms/polyglot/protocol/openid-connect/logout`             |
| JWKS               | `http://localhost:8080/realms/polyglot/protocol/openid-connect/certs`              |

The backend only needs the **Authority** and **Client ID** — every other URL is discovered automatically.

## Admin console

Log into the Keycloak master realm to manage users, roles, etc.:

- URL: `http://localhost:8080/admin`
- Username: `admin`
- Password: `admin`

Then switch the dropdown in the top-left from `master` to `polyglot` to see the imported realm.
