#!/usr/bin/env bash
# Construye las imágenes de producción y las empaqueta en images.tar para el modo offline.
# Se ejecuta en una máquina CON internet y con el código fuente; después se copian a la
# máquina destino: images.tar, docker-compose.prod.yml, setup-and-run.sh, Proxy/ y .env(.example).
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

if ! docker info >/dev/null 2>&1; then
    echo "Error: Docker no está activo o no tienes permisos." >&2
    exit 1
fi

BASE_IMAGES=(firstfinger/minio:latest nginx:alpine postgres:16-alpine)
APP_IMAGES=(microservices-certs-init:latest microservices-backend-authservice:latest microservices-backend-frontend:latest)

echo "1/4 Descargando imágenes base..."
for img in "${BASE_IMAGES[@]}"; do
    docker pull "$img"
done

echo "2/4 Construyendo backend (etapa final: solo runtime de ASP.NET con la app compilada)..."
docker build --pull --target final -t microservices-backend-authservice:latest Backend/AuthService

echo "3/4 Construyendo frontend (npm run build servido con nginx) y generador de certificados..."
docker build --pull --target prod -t microservices-backend-frontend:latest Frontend
docker build --pull -t microservices-certs-init:latest Proxy/certs-init

echo "4/4 Guardando imágenes en images.tar..."
docker save -o images.tar "${APP_IMAGES[@]}" "${BASE_IMAGES[@]}"

echo ""
echo "Listo: $(du -h images.tar | cut -f1) en images.tar"
docker image ls --format '  {{.Repository}}:{{.Tag}}  {{.Size}}' | grep -E "^  (microservices-|firstfinger/minio|nginx:alpine|postgres:16-alpine)"
