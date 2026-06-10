# Builds the Polyglot Keycloak image: Keycloakify theme compiled from source
# (no pre-built jar in the repo) + SecLists top-100k password blacklist.

# --- Stage 1: build the Keycloakify theme ---
FROM node:22-bookworm AS theme
# Keycloakify packages the theme jar with Maven
RUN apt-get update \
    && apt-get install -y --no-install-recommends maven \
    && rm -rf /var/lib/apt/lists/*
RUN npm install -g bun
WORKDIR /build
COPY keycloakify/package.json keycloakify/bun.lock ./
RUN bun install --frozen-lockfile
COPY keycloakify/ .
RUN bun run build-keycloak-theme

# --- Stage 2: fetch the password blacklist ---
FROM debian:bookworm-slim AS blacklist
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
RUN curl -fsSL -o /top100k.txt \
    https://raw.githubusercontent.com/danielmiessler/SecLists/master/Passwords/Common-Credentials/xato-net-10-million-passwords-100000.txt

# --- Final stage ---
FROM quay.io/keycloak/keycloak:26.6
COPY --from=theme /build/dist_keycloak/keycloak-theme-for-kc-all-other-versions.jar /opt/keycloak/providers/polyglot-keycloak-theme.jar
# Outside /opt/keycloak/data so a mounted data volume cannot shadow it
COPY --from=blacklist /top100k.txt /opt/keycloak/password-blacklists/top100k.txt
ENV JAVA_OPTS_APPEND="-Dkeycloak.password.blacklists.path=/opt/keycloak/password-blacklists"
