# Docker y microservicios — explicación de `Dockerfile` y `docker-compose.yml`

Notas de referencia sobre cómo funciona el setup de Docker de este proyecto (`AuthService/Dockerfile` + `docker-compose.yml` en la raíz).

## Conceptos base

- **Imagen**: una "foto congelada" de un sistema operativo mínimo + tu aplicación instalada dentro.
- **Contenedor**: esa imagen ejecutándose (como una máquina virtual ligera, pero comparte el kernel de Linux con el host, así que arranca en segundos).
- **Dockerfile**: la receta para construir la imagen.
- **docker-compose.yml**: la receta para levantar *varios* contenedores juntos y conectarlos entre sí — ideal para microservicios, porque cada microservicio = un contenedor.

## El `Dockerfile` — cómo se construye la imagen de AuthService

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
```

`FROM` dice "empieza desde esta imagen base". Se usa la imagen del **SDK** de .NET (pesada, ~800MB) porque trae el compilador — se necesita solo para compilar. `AS build` le pone nombre a esta etapa para referenciarla después. `WORKDIR /src` es como un `cd /src` dentro del contenedor: crea esa carpeta y la vuelve el directorio de trabajo.

```dockerfile
COPY AuthService.csproj ./
RUN dotnet restore
```

Se copia **solo** el `.csproj` primero (no todo el código) y se corre `dotnet restore` (descarga los paquetes NuGet). ¿Por qué en dos pasos? Docker cachea cada instrucción por capas: si luego cambias una línea de `Program.cs` pero no tocas el `.csproj`, Docker reutiliza la capa de `restore` (que es la lenta, descarga paquetes de internet) y solo repite lo de después. Acelera builds repetidos.

```dockerfile
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore
```

Ahora sí se copia todo el código fuente y se compila en modo `Release`, dejando el resultado (los `.dll` ya compilados) en `/app/publish`.

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
```

Esta es la parte clave del **multi-stage build**: empieza una **segunda imagen**, esta vez desde `aspnet:10.0` — el runtime de ASP.NET nada más (sin compilador, mucho más liviana, ~200MB). `COPY --from=build` trae *solo* los archivos ya compilados de la etapa anterior. Resultado: la imagen final que corre en producción no carga el SDK de compilación completo, solo lo necesario para ejecutar.

```dockerfile
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "AuthService.dll"]
```

`ENV` define una variable de entorno dentro del contenedor — le dice a Kestrel (el servidor web de ASP.NET) que escuche en el puerto 8080 de todas las interfaces (`+`). `EXPOSE` es solo documentación (le avisa a quien lea el Dockerfile "este contenedor usa el puerto 8080", pero no abre el puerto por sí solo). `ENTRYPOINT` es el comando que se ejecuta cuando arranca el contenedor — equivalente a correr `dotnet AuthService.dll` en una terminal.

## El `docker-compose.yml` — cómo se conectan los contenedores

```yaml
services:
  authdb:
    image: postgres:16-alpine
```

Compose define **servicios** (cada uno = un contenedor). `authdb` no tiene `build:`, así que usa directamente una imagen ya hecha de Docker Hub: Postgres 16 sobre Alpine Linux (una distro minimalista, imagen chica).

```yaml
    environment:
      POSTGRES_USER: ${DB_USER}
      POSTGRES_PASSWORD: ${DB_PASSWORD}
      POSTGRES_DB: ${DB_NAME}
```

La imagen oficial de Postgres, al arrancar por primera vez, lee estas variables de entorno y crea automáticamente ese usuario/contraseña/base de datos. El `${DB_USER}` es Compose leyendo el archivo `.env` de la raíz y sustituyendo el valor ahí.

```yaml
    volumes:
      - authdb_data:/var/lib/postgresql/data
```

Un **volumen** es almacenamiento que sobrevive aunque borres el contenedor. Sin esto, cada vez que hicieras `docker compose down` y `up` de nuevo, se perderían todos los datos (el contenedor nace y muere "limpio" cada vez). `authdb_data` es un volumen con nombre que Docker administra; se declara al final del archivo en `volumes:`.

```yaml
    ports:
      - "5433:5432"
```

Formato `"puerto_de_tu_máquina:puerto_del_contenedor"`. Adentro del contenedor Postgres siempre escucha en 5432 (su puerto estándar); se mapea al **5433** de la máquina host para que no choque con un Postgres local, que ya usa el 5432.

```yaml
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${DB_USER} -d ${DB_NAME}"]
      interval: 5s
      retries: 5
```

Docker corre este comando cada 5 segundos dentro del contenedor para saber si Postgres ya está listo para aceptar conexiones (no basta con que el contenedor esté "corriendo" — Postgres tarda un par de segundos en inicializar).

```yaml
  authservice:
    build:
      context: ./AuthService
      dockerfile: Dockerfile
```

Este servicio **sí** se construye (a diferencia de `authdb`). `context: ./AuthService` le dice "usa esa carpeta como base para el `COPY . .` del Dockerfile"; ahí es donde busca el `Dockerfile`.

```yaml
    depends_on:
      authdb:
        condition: service_healthy
```

"No arranques `authservice` hasta que `authdb` pase su healthcheck". Sin esto, la API podría arrancar antes de que Postgres esté listo y fallar al conectar.

```yaml
    environment:
      DB_HOST: authdb
```

**Esta es la parte más importante para entender microservicios con Docker**: Compose crea automáticamente una red privada interna donde cada servicio puede llamar a otro **por su nombre** como si fuera un hostname de DNS. Por eso `DB_HOST: authdb` funciona — no es "localhost", es literalmente el nombre `authdb` resuelto por la red interna de Docker hacia el contenedor de Postgres. Es igual a como `Program.cs` arma `Host=authdb;...` en la cadena de conexión.

```yaml
    ports:
      - "5098:8080"
```

Igual que antes: adentro del contenedor la API escucha en 8080 (lo definido con `ASPNETCORE_URLS`), se mapea al 5098 de la máquina host — por eso se puede hacer `curl http://localhost:5098/api/User` desde una terminal normal.

## El flujo completo cuando corres `docker compose up` (solo backend + DB)

1. Compose lee `docker-compose.yml` y el `.env`.
2. Crea una red privada y un volumen.
3. Levanta `authdb`, espera a que el healthcheck pase.
4. Construye la imagen de `authservice` con el Dockerfile (build de dos etapas) y la arranca.
5. `Program.cs` corre `db.Database.Migrate()` al iniciar → crea las tablas dentro de `authdb` automáticamente.
6. La API queda escuchando en `localhost:5098`, hablando con Postgres internamente vía `authdb:5432`.

---

# nginx como reverse proxy con HTTPS

## Certificados locales con mkcert

Un navegador no confía en un certificado "de la nada" — necesita que lo haya firmado una autoridad certificadora (CA) en la que ya confía (como Let's Encrypt en producción). Para `localhost` no existe eso, así que **mkcert** crea tu propia CA local y la instala en el almacén de confianza de tu sistema/navegador.

```bash
mkcert -install
```

Genera (si no existe) una CA local y la instala en el almacén de certificados confiables de tu sistema (y el de Firefox/Java si los detecta, porque tienen almacenes propios). A partir de aquí, cualquier certificado firmado por esa CA se ve como válido, sin advertencias.

```bash
mkcert localhost 127.0.0.1 ::1
```

Genera el certificado "hoja" de verdad — firmado por la CA anterior — válido para esos tres nombres/IPs (un certificado siempre está atado a nombres específicos). Produce dos archivos:
- `localhost+2.pem` → el certificado (público)
- `localhost+2-key.pem` → la llave privada (nunca se sube a git)

## El `nginx.conf` — dos bloques `server`

```nginx
server {
	listen 80;
	return 301 https://$host$request_uri;
}
```

Este bloque escucha en el puerto HTTP estándar (80) y **no sirve contenido real** — su único trabajo es redirigir todo hacia HTTPS con un código `301` (movido permanentemente). `$host` y `$request_uri` son variables de nginx: `$host` es el hostname que usó el cliente para conectarse (ej. `localhost`), `$request_uri` es la ruta + query string original de la petición (ej. `/api/User?x=1`). Juntas arman la misma URL que pidieron, solo que con `https://` al frente — así nadie pierde a dónde iba.

```nginx
server {
	listen 443 ssl;
	ssl_certificate /etc/nginx/certs/localhost+2.pem;
	ssl_certificate_key /etc/nginx/certs/localhost+2-key.pem;
```

`listen 443 ssl` le dice a nginx que en ese puerto debe negociar TLS antes de hablar HTTP. `ssl_certificate` / `ssl_certificate_key` apuntan a los archivos que generó mkcert — pero a la ruta **dentro del contenedor**, no en tu máquina (eso lo resuelve el `volumes:` del `docker-compose.yml`, más abajo).

```nginx
	location /api/ {
		proxy_pass http://authservice:8080;
	}
```

Cualquier petición que empiece con `/api/` se reenvía hacia `authservice` (el nombre del servicio en la red interna de Docker) en su puerto 8080. Detalle importante de `proxy_pass`: como la URL **no** termina en `/`, nginx reenvía la petición completa tal cual llegó (`/api/User` sigue siendo `/api/User` del lado del backend). Si le hubieras puesto un `/` al final (`http://authservice:8080/`), nginx recortaría el prefijo `/api/` antes de reenviar — y tu API respondería 404 porque esa ruta no existe sin el prefijo.

```nginx
	location / {
		root /usr/share/nginx/html;
		try_files $uri /index.html;
	}
```

Todo lo que no matcheó `/api/` cae aquí — el resto de rutas, es decir, tu frontend. `root` le dice a nginx desde qué carpeta servir archivos. `try_files $uri /index.html;` intenta servir un archivo real que coincida con la ruta pedida (`$uri`, la ruta normalizada sin dominio ni query string); si no existe ningún archivo con ese nombre, cae de vuelta a `index.html`. Esto es necesario para SPAs (Single Page Apps) como React: rutas de cliente tipo `/dashboard` no son archivos reales en el servidor, las maneja JavaScript en el navegador — sin este fallback, recargar la página en `/dashboard` daría 404 en vez de dejar que React la resuelva.

## El servicio `nginx` en `docker-compose.yml`

```yaml
  nginx:
    image: nginx:alpine
```

Igual que `authdb`, usa una imagen ya hecha (no tiene `build:` propio) — el software de nginx nunca cambia, solo su configuración.

```yaml
    volumes:
      - ./nginx/nginx.conf:/etc/nginx/conf.d/default.conf:ro
      - ./nginx/certs:/etc/nginx/certs:ro
      - ../React/dist:/usr/share/nginx/html:ro
```

Tres bind mounts: tu configuración, tus certificados, y el build de React. Todos con `:ro` (read-only) porque el contenedor no debería poder modificarlos. Nota la ruta del `nginx.conf`: se monta exactamente en `/etc/nginx/conf.d/default.conf`, reemplazando el archivo por defecto que trae la imagen (una página de bienvenida) — si lo montaras en otra ruta, ese `default.conf` seguiría sirviendo su contenido en vez del tuyo.

**Ojo con un malentendido común**: el `/etc/nginx/` del contenedor no tiene nada que ver con el `/etc/` de tu sistema operativo. Cada contenedor tiene su propio sistema de archivos, aislado — esa ruta es solo la convención que usa el paquete de nginx para buscar su configuración.

```yaml
    depends_on:
      authservice:
        condition: service_started
```

`service_started` (a diferencia de `service_healthy`, que usa `authdb`) solo espera a que el contenedor de `authservice` *exista y arranque* — no a que pase un healthcheck, porque `authservice` no tiene uno configurado. Para este proyecto es suficiente: si nginx arranca un segundo antes de que la API esté 100% lista, las primeras peticiones fallarían brevemente hasta que la API termine de levantar, sin mayor problema en desarrollo.

## `restart` vs `up -d` — por qué importa

Un bind mount actualiza el archivo **dentro** del contenedor apenas lo guardas en tu máquina — eso nunca falla. El problema es que nginx (el proceso) solo **lee** su configuración al arrancar y la guarda en memoria; cambiar el archivo en disco no hace que la relea sola.

- **`docker compose restart nginx`** — apaga y prende el mismo contenedor, con la misma definición de volúmenes/puertos que ya tenía. Sirve para que un proceso relea un archivo montado que cambió de **contenido** (ej. corregiste una ruta en `nginx.conf`, o el `.pem`).
- **`docker compose up -d`** — compara tu `docker-compose.yml` actual contra cómo está corriendo cada contenedor, y si hay una diferencia **estructural** (agregaste un volumen nuevo, un puerto nuevo, una variable de entorno nueva), **recrea** el contenedor desde cero con la definición actualizada. Si solo usas `restart` después de agregar un volumen nuevo al compose, el contenedor sigue corriendo con los volúmenes viejos — el nuevo simplemente no se monta.

Para servir código estático nuevo (como una nueva build de React) ni siquiera hace falta ninguno de los dos: nginx lee esos archivos del disco en cada petición, así que un simple `npm run build` ya se refleja en el siguiente refresh del navegador.

## CORS: ¿por qué ya casi no hace falta?

CORS es una restricción que aplica el **navegador**, y solo cuando JavaScript intenta pedir algo a un **origen distinto** (protocolo + dominio + puerto) al de la página cargada. Con nginx sirviendo tanto el frontend (`/`) como la API (`/api/`) bajo el mismo `https://localhost`, nunca hay una petición cross-origin real — el navegador ni siquiera activa el mecanismo de CORS.

Esto solo es cierto si el código de React usa **rutas relativas** (`fetch('/api/User')`) en vez de URLs absolutas (`fetch('https://otro-origen/...')`). Con rutas relativas, tanto en `npm run dev` (gracias al proxy de Vite, que reenvía del lado del servidor de Node, sin que el navegador salga de su origen) como en el flujo de nginx, CORS nunca entra en juego — por eso la política `AllowViteLocal` en `Program.cs` se puede dejar como una red de seguridad inofensiva: no estorba si nunca se activa, pero protege si algún día alguien agrega un `fetch` con URL absoluta.
