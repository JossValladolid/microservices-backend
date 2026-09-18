# Proyecto

API construida con **ASP.NET Core (.NET 10)** y **Entity Framework Core**, usando **PostgreSQL** como base de datos.

## Requisitos previos

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) o superior
- [PostgreSQL](https://www.postgresql.org/download/) instalado y corriendo (local o vía Docker)
- Herramienta global de EF Core (se instala más abajo)

## 1. Descargar/restaurar los paquetes

Clona el repositorio y, desde la raíz del proyecto, restaura las dependencias del `.csproj`:

```bash
dotnet restore
```

Esto descarga automáticamente los paquetes NuGet definidos en `Proyecto.csproj` (EF Core, Npgsql, Scalar, etc.).

Si no tienes instalada la herramienta de línea de comandos de EF Core (necesaria para crear/aplicar migraciones), instálala una sola vez de forma global:

```bash
dotnet tool install --global dotnet-ef
```

Verifica que quedó disponible:

```bash
dotnet ef --version
```

## 2. Levantar la base de datos (PostgreSQL)

### Opción A: PostgreSQL instalado localmente

Asegúrate de que el servicio de PostgreSQL esté corriendo y crea la base de datos que usará el proyecto (por defecto `ProyectoDB`):

```bash
psql -U postgres -c "CREATE DATABASE \"ProyectoDB\";"
```

### Opción B: PostgreSQL con Docker

```bash
docker run --name proyecto-db \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=tu_password \
  -e POSTGRES_DB=ProyectoDB \
  -p 5432:5432 \
  -d postgres
```

## 3. Conectar tu usuario a la base de datos

Las credenciales de conexión **no** viven en `appsettings.json` — se cargan desde un archivo `.env` (ignorado por git) mediante el paquete `DotNetEnv`.

Copia el archivo de ejemplo y edítalo con tus propias credenciales:

```bash
cp .env.example .env
```

```dotenv
# .env
DB_HOST=localhost
DB_NAME=ProyectoDB
DB_USER=postgres
DB_PASSWORD=tu_password
```

`Program.cs` carga este archivo al arrancar (`Env.Load()`) y arma la cadena de conexión a partir de esas variables. El archivo `.env` nunca debe subirse al repositorio (ya está en `.gitignore`); `.env.example` sí se versiona, como plantilla sin credenciales reales.

> ⚠️ **Importante:** si alguna vez subiste una contraseña real a un repositorio (aunque después borres el commit o el repo), considérala comprometida y **cámbiala** en PostgreSQL, ya que puede seguir accesible en cachés, forks o el historial de quien la haya clonado antes.

## 4. Aplicar las migraciones

Con la base de datos levantada y la cadena de conexión configurada, aplica las migraciones existentes para crear/actualizar las tablas:

```bash
dotnet ef database update
```

### Crear una nueva migración (cuando cambies los modelos)

```bash
dotnet ef migrations add NombreDeLaMigracion
dotnet ef database update
```

### Revertir la última migración

```bash
dotnet ef database update NombreDeLaMigracionAnterior
```

## 5. Ejecutar el proyecto

```bash
dotnet run
```

Por defecto la API queda disponible en:

- HTTP: `http://localhost:5098`
- HTTPS: `https://localhost:7257`

En entorno de desarrollo, la documentación interactiva (Scalar) queda disponible en `/scalar` sobre la URL base del proyecto.
