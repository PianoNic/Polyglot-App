FROM node:22-bookworm AS theme
RUN apt-get update
RUN apt-get install -y maven
WORKDIR /build
COPY keycloakify .
RUN npm install
RUN npm run build-keycloak-theme

FROM debian:bookworm-slim AS blacklist
RUN apt-get update
RUN apt-get install -y curl ca-certificates
RUN curl -fsSL -o /rockyou.tar.gz https://github.com/danielmiessler/SecLists/raw/master/Passwords/Leaked-Databases/rockyou.txt.tar.gz
RUN tar -xzf /rockyou.tar.gz -C /
RUN iconv -f utf-8 -t utf-8 -c /rockyou.txt -o /rockyou-utf8.txt

FROM quay.io/keycloak/keycloak:26.6
COPY --from=theme /build/dist_keycloak/keycloak-theme-for-kc-all-other-versions.jar /opt/keycloak/providers/polyglot-keycloak-theme.jar
COPY --from=blacklist /rockyou-utf8.txt /opt/keycloak/password-blacklists/rockyou.txt
ENV JAVA_OPTS_APPEND="-Dkeycloak.password.blacklists.path=/opt/keycloak/password-blacklists"
