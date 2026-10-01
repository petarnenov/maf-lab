# syntax=docker/dockerfile:1

FROM node:24.21.0-alpine
WORKDIR /app
# No telemetry: CopilotKit's own, and @scarf/scarf's install-time script (agui-protocol-only).
ENV COPILOTKIT_TELEMETRY_DISABLED=true \
    DO_NOT_TRACK=1 \
    SCARF_ANALYTICS=false \
    NODE_ENV=production
COPY package.json package-lock.json .npmrc ./
RUN npm ci --no-audit --no-fund --omit=dev
COPY server.ts ./
USER node
EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=3s --retries=20 CMD wget -qO- http://127.0.0.1:8080/health || exit 1
CMD ["node", "server.ts"]
