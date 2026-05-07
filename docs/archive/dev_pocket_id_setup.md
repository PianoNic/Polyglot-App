# Pocket ID — Initial Setup

Pocket ID runs as a container alongside the API and database (see `compose.dev.yml`).
After the first start, a few things need to be configured in the admin UI before
login with Polyglot works.

For the general dev environment setup, see [`dev_setup.md`](./dev_setup.md).

## 1. Start the containers

```bash
docker compose -f compose.dev.yml up -d
```

Pocket ID will be available at `http://localhost:1411`.

## 2. Register the first account (become admin)

Open in your browser:

```
http://localhost:1411/setup
```

Fill in the registration form. The **first account** registered through this
page automatically becomes the admin — this setup page is disabled afterward.

After submitting, you'll be prompted to register a passkey (Windows Hello,
YubiKey, phone biometrics, etc.).

## 3. Create groups

Navigate to **Administration → Groups** (or **Usergroups**).

Each group has two fields:

- **Display Name:** shown in the Pocket ID UI
- **Name:** used in the `groups` claim of the OIDC token — this is what Polyglot checks for role-based access

Create the following two groups:

| Display Name | Name    | Purpose                      |
|--------------|---------|------------------------------|
| User         | `user`  | Default role for all users   |
| Admin        | `admin` | Access to the admin panel    |

> **Tip:** After creating a group, press the back button before creating the next one — otherwise you'll end up editing the group you just created.

## 4. Assign yourself to the admin group

Go to **Administration → Users**, find your account, and:

1. Click the **⋯ (three dots)** menu → **Edit**
2. Open the **Groups** (or **Usergroups**) section
3. Check the box next to `admin`
4. Click **Save**

## 5. Enable self-service registration

Under **Administration → Application Configuration → User Signup**:

- **Enable User Signups:** `Open Signup`
- **Default User Groups:** select `user`

Save.

From now on, anyone can create an account at `http://localhost:1411/signup`
and will automatically be assigned the `user` group.

## 6. Create an OIDC client for Polyglot

Navigate to **Administration → OIDC Clients** and click **Add OIDC Client**.

Fill in the following fields:

- **Name:** `Polyglot`
- **Callback URLs:** `http://localhost:4200/callback`, `http://localhost:5246/swagger/oauth2-redirect.html`
- **Public Client:** enable — **PKCE** is then activated automatically

Click **Save**.

> Since this is a public client (SPA), no client secret is generated —
> authentication uses PKCE instead.

Next, scroll down to **Allowed User Groups**, add `user` and `admin`, and click **Save** again.

## 7. Add the Client ID to user secrets

Click **"Show more details"** (**"Mehr Details anzeigen"**) at the top of the OIDC client page to reveal all endpoint URLs:

| Field              | Value                                                      |
|--------------------|------------------------------------------------------------|
| Client ID          | `3675b0b6-cb83-4f08-bb96-b0ee0f34c71f`                     |
| Issuer URL         | `http://localhost:1411`                                    |
| OIDC Discovery URL | `http://localhost:1411/.well-known/openid-configuration`   |
| Authorization URL  | `http://localhost:1411/authorize`                          |
| Token URL          | `http://localhost:1411/api/oidc/token`                     |
| UserInfo URL       | `http://localhost:1411/api/oidc/userinfo`                  |
| End Session URL    | `http://localhost:1411/api/oidc/end-session`               |
| JWKS URL           | `http://localhost:1411/.well-known/jwks.json`              |

The backend only needs the **Client ID** — all other endpoints are discovered automatically via `Oidc:Authority`.

Copy the Client ID into your user secrets:

```bash
cd src/Polyglot.API
dotnet user-secrets set "Oidc:ClientId" "<client-id-from-pocket-id>"
```

See [`dev_setup.md`](./dev_setup.md#configure-secrets) for the full secret setup.

> **Note:** Pocket ID's UI may show these URLs with `https://` depending on your `APP_URL` setting. If you access Pocket ID via `http://localhost:1411`, use `http` in all configuration values — the scheme must match exactly, otherwise OIDC discovery fails.