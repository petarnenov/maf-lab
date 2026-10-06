# syntax=docker/dockerfile:1

FROM node:24.21.0-alpine AS build
# Built from the repository root: the app in /src/web, each plugin's web part in /src/plugins/<name>/web (decision 8).
WORKDIR /src/web
# CopilotKit depends on @scarf/scarf, an install-time telemetry script: off (agui-protocol-only).
ENV SCARF_ANALYTICS=false
COPY web/package.json web/package-lock.json web/.npmrc ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
COPY plugins/ /src/plugins/
# The product variant (introduce-plugins decision 5e) builds without the web part of any plugin whose manifest does not
# allow stage or prod: MAF_PRODUCT_WEB lists the plugins it keeps, as |a|b|.
ARG MAF_IMAGE_VARIANT=full
ARG MAF_PRODUCT_WEB=||
RUN if [ "$MAF_IMAGE_VARIANT" = product ]; then \
      for d in /src/plugins/*/; do n="$(basename "$d")"; case "$MAF_PRODUCT_WEB" in *"|$n|"*) ;; *) rm -rf "$d/web";; esac; done; \
    fi
RUN npm run build

FROM nginx:1.30.5-alpine AS runtime
COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/web/dist /usr/share/nginx/html
EXPOSE 80
HEALTHCHECK --interval=15s --timeout=3s --retries=3 CMD wget -qO- http://127.0.0.1/healthz || exit 1
