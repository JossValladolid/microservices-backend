# Proyecto

Arquitectura de microservicios construida con **ASP.NET Core (.NET 10)**, **Entity Framework Core**, **PostgreSQL** y **MinIO (S3 Object Storage)**, con frontend en **React + Vite**, detrás de **nginx** como reverse proxy (HTTP/HTTPS). Cada microservicio vive en su propia carpeta, con su propia base de datos y su propio `Dockerfile`. Un solo `docker compose up` levanta todo (DB, API, MinIO, frontend y nginx).

## Estructura del repositorio

```
.
├── docker-compose.yml        # Orquesta frontend + backend + DBs + MinIO + nginx
├── .env / .env.example       # Variables de entorno y configuración de red
├── Backend/
│   ├── nginx/
│   │   ├── nginx.conf        # Reverse proxy: HTTP/HTTPS, /api/ → API, /images/ → MinIO S3, / → Vite
│   │   └── certs/            # Certificados SSL generados automáticamente por certs-init
│   ├── docs/
│   └── AuthService/          # Microservicio de autenticación / usuarios / archivos (S3)
│       ├── Dockerfile        # targets: dev (dotnet watch) y final (publish)
│       ├── AuthService.csproj
│       ├── Program.cs
│       ├── Controllers/      # AuthController, UserController, FilesController (S3)
│       ├── Models/
│       └── Migrations/
└── Frontend/                 # React + Vite
    ├── Dockerfile            # target: dev (npm run dev)
    ├── vite.config.js
    └── src/
```

El frontend corre en su propio contenedor (`frontend`) con el servidor de desarrollo de Vite y hot reload; nginx le hace proxy (incluido el WebSocket del HMR).

Los próximos microservicios seguirán el mismo patrón: una carpeta nueva dentro de `Backend/`, su propio `Dockerfile`, un servicio + volumen extra en `docker-compose.yml`, y una nueva regla `location` en `nginx.conf` para rutearlo.

## Requisitos previos

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) o superior (solo si corres `AuthService` sin Docker)
- [Node.js](https://nodejs.org/) 22+ (solo si corres el frontend sin Docker)
- [Docker y Docker Compose](https://docs.docker.com/get-docker/) (forma recomendada de levantar todo)
- [mkcert](https://github.com/FiloSottile/mkcert) — para generar certificados HTTPS de desarrollo confiables localmente
- Alternativamente, [PostgreSQL](https://www.postgresql.org/download/) instalado localmente si prefieres correr `AuthService` sin contenedores
- Herramienta global de EF Core (se instala más abajo)

## Opción A: Levantar todo con Docker Compose (recomendado)

Levanta la base de datos, la API, el frontend y nginx (con HTTPS) en contenedores aislados de tu sistema.

### 1. Configura las credenciales

```bash
cp .env.example .env
```

Edita `.env` con tus propios valores:

```dotenv
DB_HOST=localhost
DB_PORT=5432
DB_NAME=ProyectoDB
DB_USER=postgres
DB_PASSWORD=tu_password

JWT_SECRET=una_cadena_aleatoria_de_al_menos_32_caracteres
JWT_ISSUER=AuthService
JWT_AUDIENCE=ProyectoClient
JWT_ACCESS_EXPIRATION_MINUTES=15
JWT_REFRESH_EXPIRATION_DAYS=7

# MinIO Object Storage
MINIO_ROOT_USER=minioadmin
MINIO_ROOT_PASSWORD=minioadmin123
MINIO_SERVICE_USER=backend-service
MINIO_SERVICE_USER_PASSWORD=MySecretPass123!
MINIO_SVCACCT_ACCESS_KEY=bknd-svc-key
MINIO_SVCACCT_SECRET_KEY=backend-svc-secret-key-change-me
MINIO_DEFAULT_BUCKET=images

# Configuración de Red (Opcional - para acceder por LAN o mover de red)
PUBLIC_HOST=192.168.1.99
```

> `DB_HOST`/`DB_PORT` en `.env` solo se usan si corres el proyecto fuera de Docker (`dotnet run`). Dentro de `docker-compose.yml`, `authservice` se conecta a `authdb` automáticamente.
> `PUBLIC_HOST` es tu IP local o dominio; si cambias de red, solo actualizas este valor (o lo dejas vacío para auto-detectar).

#### Variables de JWT

| Variable | Obligatoria | Descripción |
|---|---|---|
| `JWT_SECRET` | Sí | Llave con la que se firman los access tokens (HS256). Mínimo **32 caracteres**; si falta o es más corta, `AuthService` no arranca. |
| `JWT_ISSUER` | Sí | Emisor (`iss`) que se escribe en el token y se exige al validarlo. |
| `JWT_AUDIENCE` | Sí | Audiencia (`aud`) que se escribe en el token y se exige al validarlo. |
| `JWT_ACCESS_EXPIRATION_MINUTES` | No (default `15`) | Vida del access token, en minutos. |
| `JWT_REFRESH_EXPIRATION_DAYS` | No (default `7`) | Vida del refresh token, en días. |

Genera un `JWT_SECRET` seguro con:

```bash
openssl rand -base64 48
```

> ⚠️ Nunca subas el `JWT_SECRET` real a git: quien lo tenga puede fabricar tokens válidos (incluso de administrador). Si se filtra, cámbialo; todos los access tokens existentes quedarán inválidos.

#### Cómo se usan los tokens

- El login (`POST /api/Auth/login`) devuelve el usuario y deja **dos cookies `HttpOnly`, `Secure` y `SameSite=Strict`**: el access token (JWT, path `/api`) y el refresh token (texto aleatorio guardado en la tabla `Sessions`, path `/api/Auth`). JavaScript nunca los puede leer.
- `POST /api/Auth/refresh` genera un access token nuevo y rota el refresh token; `POST /api/Auth/logout` revoca la sesión y borra las cookies; `GET /api/Auth/me` devuelve el usuario autenticado.
- Como las cookies son `Secure`, solo viajan por HTTPS: usa `https://localhost` (nginx) en lugar de `http://localhost:5098` para probar el login desde el navegador.
- Más detalle del flujo completo en [`Backend/docs/jwt-explicado.md`](Backend/docs/jwt-explicado.md).

#### Usuarios demo (seed)

`docker-compose.yml` define `SEED_DEMO_USERS: "true"` para `authservice`, lo que inserta al arrancar los usuarios de `Backend/AuthService/Seed/seed_users.sql`:

| Email | Contraseña | Rol |
|---|---|---|
| `admin@example.com` | `Admin123!` | Admin |
| `usuario@example.com` | `Usuario123!` | Usuario |

> Solo para desarrollo. Quita o pon en `false` `SEED_DEMO_USERS` en cualquier entorno real.

### 2. Genera los certificados HTTPS locales (una sola vez)

```bash
mkcert -install
mkdir -p Backend/nginx/certs
cd Backend/nginx/certs
mkcert localhost 127.0.0.1 ::1
cd ../../..
```

Esto instala una autoridad certificadora (CA) local en tu sistema y genera un certificado + llave confiables para `localhost`, sin advertencias del navegador. Ver [`Backend/docs/docker-explicado.md`](Backend/docs/docker-explicado.md) para el detalle de qué hace cada comando. Los archivos `.pem` generados están en `.gitignore` — nunca se suben al repo.

### 3. Levanta los contenedores

Desde la raíz del repo (donde está `docker-compose.yml`):

```bash
docker compose up -d --build
```

No hace falta compilar el frontend ni instalar nada en tu máquina: todo corre en contenedores. Esto crea:
- **`authdb`**: PostgreSQL en un volumen propio (`authdb_data`), expuesto en el host en el puerto **5433** (para no chocar con un Postgres local en 5432).
- **`authservice`**: la API (target `dev`, corre con `dotnet watch`). Expuesta en `http://localhost:5098`. Al arrancar aplica migraciones automáticamente.
- **`minio`**: servidor de almacenamiento de objetos compatible con AWS S3. Expuesto en puerto **9000** (S3 API) y **9002** (consola administrativa).
- **`minio-init`**: contenedor que inicializa automáticamente el usuario de servicio (`MINIO_SERVICE_USER`), la política de lectura/escritura, el bucket predeterminado (`images`) y las credenciales de servicio.
- **`certs-init`**: genera automáticamente los certificados SSL locales en `Backend/nginx/certs` con SAN para `localhost`, `127.0.0.1` y cualquier IP/dominio configurado en `PUBLIC_HOST`.
- **`frontend`**: Vite (`npm run dev`) con hot reload, expuesto en `http://localhost:5173`.
- **`nginx`**: punto de entrada unificado para todos los dispositivos. Puerto **80** (HTTP) y puerto **443** (HTTPS):
  - `/` → frontend de React
  - `/api/` → microservicio de autenticación
  - `/images/` → almacenamiento de objetos MinIO S3 (Path-Style)
  - `/storage/` → consola de administración de MinIO

### 4. Acceso desde otros dispositivos y redes (LAN)

Para acceder a la aplicación desde tu teléfono, tablet u otra computadora en la misma red Wi-Fi/LAN:

1. Revisa la IP de tu máquina en la red (ejemplo: `192.168.1.99`).
2. Configura en tu `.env`:
   ```dotenv
   PUBLIC_HOST=192.168.1.99
   ```
3. Ejecuta `docker compose up -d` para aplicar los cambios en el certificado.
4. En cualquier dispositivo conectado a tu red, abre:
   - **HTTP (recomendado para LAN sin advertencias de certificado)**: `http://192.168.1.99`
   - **HTTPS**: `https://192.168.1.99`
   - **Vite Dev directo**: `http://192.168.1.99:5173`

> Las URLs prefirmadas de subida y descarga de archivos de S3 se generan dinámicamente usando el host y protocolo con el que te conectas, por lo que subir y descargar archivos funciona en cualquier interfaz o IP automáticamente.

### 5. Verifica que responde

```bash
curl -i http://localhost/api/User         # API vía HTTP
curl -k -i https://localhost/api/User      # API vía HTTPS
curl -i http://localhost/images/           # MinIO S3 vía nginx
```

### 5. Comandos útiles

```bash
docker compose logs -f nginx          # o frontend / authservice / authdb
docker compose restart nginx          # reinicia el proceso, relee archivos montados que cambiaron de CONTENIDO (ej. nginx.conf, certs)
docker compose up -d                  # recrea contenedores cuyo docker-compose.yml cambió de ESTRUCTURA (nuevo volumen, puerto, env var)
docker compose down                   # apaga todo (conserva datos)
docker compose down -v                # apaga y borra también los volúmenes (DB y node_modules del front)
docker compose up -d --build frontend # reconstruye el front tras agregar una dependencia en package.json
```

> **`restart` vs `up -d`**: si solo cambiaste el *contenido* de un archivo montado (como `nginx.conf` o un certificado), basta con `restart` al servicio afectado. Si cambiaste el `docker-compose.yml` mismo (agregaste un volumen, un puerto, una variable de entorno), necesitas `up -d` para que Compose recree el contenedor con la nueva definición.

## Frontend (React)

Con Docker Compose el frontend corre con `npm run dev` dentro del contenedor `frontend`; nginx le hace proxy desde `https://localhost` (con soporte de WebSocket para el hot reload). `vite.config.js` incluye `host: true` y `watch.usePolling` para que Vite sea accesible y detecte cambios dentro de Docker.

| | Docker Compose (recomendado) | Sin Docker |
|---|---|---|
| Cómo se sirve | `frontend` (Vite, hot reload) detrás de nginx | `npm run dev` en `Frontend/` |
| Cómo llega a la API | nginx → `/api/` → `authservice` | Proxy de Vite (`vite.config.js`) → `localhost:5098` (o `API_TARGET`) |
| URL | `https://localhost` | `http://localhost:5173` |

El código de React usa rutas relativas (`fetch('/api/User')`) en ambos casos, así que nunca hace falta cambiar nada. Si agregas una dependencia nueva, reconstruye la imagen: `docker compose up -d --build frontend`.

> **Falta:** Para producción, un stage extra en `Frontend/Dockerfile` que ejecute `npm run build` y sirva `dist/` con nginx, junto con un `nginx.conf` que sirva estáticos en lugar de hacer proxy a Vite.

## CORS

`Program.cs` tiene una política CORS (`AllowViteLocal`) que permite peticiones desde `http://localhost:5173`. Ya no es estrictamente necesaria cuando accedes vía nginx (mismo origen, el navegador nunca aplica CORS), pero se deja como red de seguridad para el flujo de `npm run dev` directo (puerto 5173) por si en el futuro se hace algún `fetch` con URL absoluta en vez de relativa.

## Opción B: Correr AuthService directamente con `dotnet` (sin Docker)

### 1. Restaurar paquetes

```bash
cd Backend/AuthService
dotnet restore
```

Instala también la herramienta de EF Core (una sola vez, de forma global):

```bash
dotnet tool install --global dotnet-ef
dotnet ef --version
```

### 2. Levantar PostgreSQL localmente

```bash
psql -U postgres -c "CREATE DATABASE \"ProyectoDB\";"
```

### 3. Configurar la conexión

Desde la **raíz del repo** (no dentro de `Backend/AuthService/`):

```bash
cp .env.example .env
```

Edita `.env` con tus credenciales locales (`DB_HOST=localhost`, `DB_PORT=5432`, etc.) y las variables `JWT_*` (ver la tabla en la Opción A; sin ellas el servicio no arranca). Si quieres los usuarios demo, exporta también `SEED_DEMO_USERS=true`. `Program.cs` busca el `.env` recorriendo hacia arriba desde el directorio de trabajo (`Env.TraversePath().Load()`), así que funciona tanto si corres `dotnet run` desde `Backend/AuthService/` como desde la raíz.

> ⚠️ Si alguna vez subiste una contraseña real a un repositorio (aunque luego borres el commit o el repo), considérala comprometida y **cámbiala** en PostgreSQL — puede seguir accesible en cachés, forks o el historial de quien lo haya clonado antes.

### 4. Aplicar migraciones

```bash
cd Backend/AuthService
dotnet ef database update
```

Crear una nueva migración (cuando cambies los modelos):

```bash
dotnet ef migrations add NombreDeLaMigracion
dotnet ef database update
```

Revertir la última migración:

```bash
dotnet ef database update NombreDeLaMigracionAnterior
```

### 5. Ejecutar el servicio

```bash
dotnet run
```

Por defecto usa el perfil `http` de `Properties/launchSettings.json`, disponible en:

- `http://localhost:5098`

> `launchSettings.json` también trae un perfil `https` (puerto 7257) generado automáticamente al crear el proyecto, pero `dotnet run` no lo usa a menos que lo pidas explícitamente con `dotnet run --launch-profile https`. En este proyecto el HTTPS real se maneja con nginx (ver arriba), así que normalmente no hace falta tocar ese perfil.

En desarrollo, la documentación interactiva (Scalar) queda disponible en `/scalar` sobre la URL base.