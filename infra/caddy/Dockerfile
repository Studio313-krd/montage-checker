FROM caddy:2.11.4-alpine

COPY infra/caddy/Caddyfile /etc/caddy/Caddyfile

RUN caddy fmt --overwrite /etc/caddy/Caddyfile
