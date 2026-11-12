#!/usr/bin/env bash
set -e

# Directorio donde reside este script
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# 1. Verificar que Docker esté disponible y corriendo
if ! command -v docker >/dev/null 2>&1; then
    echo "Error: Docker no está instalado en este sistema." >&2
    exit 1
fi

if ! docker info >/dev/null 2>&1; then
    echo "Error: El servicio de Docker no está activo o no tienes permisos (prueba con sudo o agrega tu usuario al grupo docker)." >&2
    exit 1
fi

# 2. Verificar archivo .env
if [ ! -f "$SCRIPT_DIR/.env" ]; then
    if [ -f "$SCRIPT_DIR/.env.example" ]; then
        echo "No se encontró '.env', creando uno a partir de '.env.example'..."
        cp "$SCRIPT_DIR/.env.example" "$SCRIPT_DIR/.env"
    else
        echo "Advertencia: No se encontró archivo '.env'."
    fi
fi

# 3. Cargar imágenes desde images.tar si existe
TAR_FILE="$SCRIPT_DIR/images.tar"
if [ -f "$TAR_FILE" ]; then
    echo "Cargando imágenes de Docker desde $TAR_FILE..."
    docker load -i "$TAR_FILE"
    echo "Imágenes cargadas exitosamente."
else
    echo "No se encontró 'images.tar', utilizando las imágenes existentes en el sistema..."
fi

# 4. Determinar archivo docker-compose a utilizar
if [ -f "$SCRIPT_DIR/docker-compose.prod.yml" ]; then
    COMPOSE_FILE="docker-compose.prod.yml"
elif [ -f "$SCRIPT_DIR/docker-compose.yml" ]; then
    COMPOSE_FILE="docker-compose.yml"
else
    echo "Error: No se encontró ningún archivo docker-compose en el directorio." >&2
    exit 1
fi

# 5. Determinar comando docker compose (v2 plugin o v1 binario)
if docker compose version >/dev/null 2>&1; then
    DOCKER_COMPOSE="docker compose"
elif command -v docker-compose >/dev/null 2>&1; then
    DOCKER_COMPOSE="docker-compose"
else
    echo "Error: 'docker compose' no está disponible." >&2
    exit 1
fi

# 6. Levantar la aplicación
echo "Iniciando los contenedores con $COMPOSE_FILE..."
$DOCKER_COMPOSE -f "$COMPOSE_FILE" up -d

echo ""
echo "Aplicacion web: https://localhost"
echo ""
