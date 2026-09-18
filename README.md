# Proyecto

Arquitectura de microservicios construida con **ASP.NET Core (.NET 10)**, **Entity Framework Core** y **PostgreSQL**, detrás de **nginx** como reverse proxy con HTTPS. Cada microservicio vive en su propia carpeta, con su propia base de datos y su propio `Dockerfile`.

## Estructura del repositorio

```
.
├── docker-compose.yml       # Orquesta todos los microservicios + sus DBs + nginx
├── .env / .env.example      # Credenciales compartidas (una DB por servicio)
├── nginx/
│   ├── nginx.conf           # Reverse proxy: HTTPS, redirect HTTP→HTTPS, rutea /api/ y /
│   └── certs/                # Certificados locales (mkcert) — NO se sube a git
└── AuthService/              # Microservicio de autenticación / usuarios
    ├── Dockerfile
    ├── AuthService.csproj
    ├── Program.cs
    ├── Controllers/
    ├── Models/
    └── Migrations/
```

El frontend de React vive en un repositorio/carpeta **hermana** (`../React` respecto a esta carpeta), no dentro de este repo. nginx sirve su build de producción (`dist/`) montándolo directamente.

Los próximos microservicios seguirán el mismo patrón: una carpeta nueva en la raíz, su propio `Dockerfile`, un servicio + volumen extra en `docker-compose.yml`, y una nueva regla `location` en `nginx.conf` para rutearlo.

## Requisitos previos

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) o superior
- [Docker y Docker Compose](https://docs.docker.com/get-docker/) (forma recomendada de levantar todo)
- [mkcert](https://github.com/FiloSottile/mkcert) — para generar certificados HTTPS de desarrollo confiables localmente
- Alternativamente, [PostgreSQL](https://www.postgresql.org/download/) instalado localmente si prefieres correr `AuthService` sin contenedores
- Herramienta global de EF Core (se instala más abajo)

## Opción A: Levantar todo con Docker Compose (recomendado)

Levanta la base de datos, la API y nginx (con HTTPS) en contenedores aislados de tu sistema.

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
```

> `DB_HOST`/`DB_PORT` en `.env` solo se usan si corres el proyecto fuera de Docker (`dotnet run`). Dentro de `docker-compose.yml`, `authservice` se conecta a `authdb` (el nombre del servicio) automáticamente.

### 2. Genera los certificados HTTPS locales (una sola vez)

```bash
mkcert -install
mkdir -p nginx/certs
cd nginx/certs
mkcert localhost 127.0.0.1 ::1
cd ../..
```

Esto instala una autoridad certificadora (CA) local en tu sistema y genera un certificado + llave confiables para `localhost`, sin advertencias del navegador. Ver [`docs/docker-explicado.md`](docs/docker-explicado.md) para el detalle de qué hace cada comando. Los archivos `.pem` generados están en `.gitignore` — nunca se suben al repo.

### 3. Compila el frontend

```bash
cd ../React
npm install
npm run build
cd ../Proyecto
```

nginx sirve el contenido de `React/dist/` como archivos estáticos — necesitas generarlo antes de levantar los contenedores (y cada vez que cambies el frontend y quieras verlo reflejado en `https://localhost`).

### 4. Levanta los contenedores

```bash
docker compose up -d --build
```

Esto crea:
- **`authdb`**: PostgreSQL en un volumen propio (`authdb_data`), expuesto en el host en el puerto **5433** (para no chocar con un Postgres local en 5432).
- **`authservice`**: la API, expuesta también en `http://localhost:5098` (acceso directo, útil para depurar). Al arrancar aplica las migraciones automáticamente sobre `authdb`.
- **`nginx`**: único punto de entrada real. Puerto **80** solo redirige a **443**; **443** sirve HTTPS con el certificado de mkcert, rutea `/api/*` hacia `authservice` y todo lo demás hacia el build de React.

### 5. Verifica que responde

```bash
curl -i https://localhost/api/User      # API, vía nginx
curl -i http://localhost/               # debe responder 301 → https
```

O abre `https://localhost` directo en el navegador — deberías ver tu app de React, sin advertencias de certificado.

### 6. Comandos útiles

```bash
docker compose logs -f nginx          # o authservice / authdb
docker compose restart nginx          # reinicia el proceso, relee archivos montados que cambiaron de CONTENIDO (ej. nginx.conf, certs)
docker compose up -d                  # recrea contenedores cuyo docker-compose.yml cambió de ESTRUCTURA (nuevo volumen, puerto, env var)
docker compose down                   # apaga todo (conserva datos)
docker compose down -v                # apaga y borra también el volumen de la DB
```

> **`restart` vs `up -d`**: si solo cambiaste el *contenido* de un archivo montado (como `nginx.conf` o un certificado), basta con `restart` al servicio afectado. Si cambiaste el `docker-compose.yml` mismo (agregaste un volumen, un puerto, una variable de entorno), necesitas `up -d` para que Compose recree el contenedor con la nueva definición.

## Frontend (React) — dos entornos, igual que el backend

| | Dev (rápido) | "Producción" (build real) |
|---|---|---|
| Cómo se sirve | `npm run dev` (Vite, hot reload) | `npm run build` → `dist/` servido por nginx |
| Cómo llega a la API | Proxy de Vite (`vite.config.js`) → directo a `authservice:5098` | Mismo origen, vía `nginx` → `/api/` |
| URL | `http://localhost:5173` | `https://localhost` |

El código de React usa rutas relativas (`fetch('/api/User')`) en ambos casos, así que nunca hace falta cambiar nada al alternar entre los dos flujos. Cuando cambies código de React en el flujo de nginx, solo necesitas volver a correr `npm run build` — nginx sirve los archivos estáticos directo del disco, sin necesidad de `restart` ni `up -d`.

## CORS

`Program.cs` tiene una política CORS (`AllowViteLocal`) que permite peticiones desde `http://localhost:5173`. Ya no es estrictamente necesaria cuando accedes vía nginx (mismo origen, el navegador nunca aplica CORS), pero se deja como red de seguridad para el flujo de `npm run dev` por si en el futuro se hace algún `fetch` con URL absoluta en vez de relativa.

## Opción B: Correr AuthService directamente con `dotnet` (sin Docker)

### 1. Restaurar paquetes

```bash
cd AuthService
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

Desde la **raíz del repo** (no dentro de `AuthService/`):

```bash
cp .env.example .env
```

Edita `.env` con tus credenciales locales (`DB_HOST=localhost`, `DB_PORT=5432`, etc.). `Program.cs` busca el `.env` recorriendo hacia arriba desde el directorio de trabajo (`Env.TraversePath().Load()`), así que funciona tanto si corres `dotnet run` desde `AuthService/` como desde la raíz.

> ⚠️ Si alguna vez subiste una contraseña real a un repositorio (aunque luego borres el commit o el repo), considérala comprometida y **cámbiala** en PostgreSQL — puede seguir accesible en cachés, forks o el historial de quien lo haya clonado antes.

### 4. Aplicar migraciones

```bash
cd AuthService
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

## Próximos pasos

- Agregar más microservicios (cada uno con su carpeta, `Dockerfile`, entrada en `docker-compose.yml`, y su propio `location` en `nginx.conf`).
- Certificados reales (Let's Encrypt) cuando esto se despliegue en un dominio público — mkcert es solo para desarrollo local.
