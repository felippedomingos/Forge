# Build context is frontend/ (see docker-compose.yml at repo root).
FROM node:20-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:1.27-alpine AS runtime

# frontend/src/lib/api.ts hits a same-origin relative BASE_URL = '/api' (and
# useTaskWebSocket.ts builds its ws:// URL off location.host) - no localhost:5080 is
# baked into the built JS. nginx is what turns that same-origin /api into the
# forge-api container: FORGE_API_URL is substituted into default.conf at container
# start (official nginx image envsubst-on-templates behavior), so which backend this
# frontend talks to is a runtime env var, not a rebuild.
ENV FORGE_API_URL=http://forge-api:8080
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
