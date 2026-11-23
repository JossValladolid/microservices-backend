FROM node:22-alpine AS dev
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .

EXPOSE 5173

CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0"]


# Compila la aplicación (npm run build) para producción / modo offline.
FROM dev AS build
RUN npm run build


# Sirve los archivos estáticos compilados con nginx (sin Node ni node_modules).
FROM nginx:alpine AS prod
COPY nginx-spa.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 5173
