FROM node:22-alpine AS build
WORKDIR /app
COPY samples/NuxtUiSample/frontend/package*.json ./
RUN npm install
COPY samples/NuxtUiSample/frontend ./
ARG NUXT_PUBLIC_API_BASE=/api
ENV NUXT_PUBLIC_API_BASE=$NUXT_PUBLIC_API_BASE
RUN npm run build

FROM node:22-alpine
WORKDIR /app
COPY --from=build /app/.output ./.output
ENV HOST=0.0.0.0
ENV PORT=3000
EXPOSE 3000
CMD ["node", ".output/server/index.mjs"]
