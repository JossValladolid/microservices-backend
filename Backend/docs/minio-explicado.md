# MinIO y URLs prefirmadas explicado

Cómo se suben, listan, ven y borran archivos en el proyecto, y por qué el navegador habla directo con MinIO sin pasar el archivo por la API.

## 1. Infraestructura (`docker-compose.yml`)

| Servicio | Qué hace |
|---|---|
| `minio` | Servidor de almacenamiento compatible con S3. API en `:9000`, consola de administración en `:9002` (expuesta por nginx en `/storage/`). |
| `minio-init` | Contenedor de un solo uso. Espera a MinIO y: crea un usuario de servicio con política `readwrite`, crea el bucket (`MINIO_DEFAULT_BUCKET`) y crea un *service account* con `ACCESS_KEY`/`SECRET_KEY` fijas. |
| `authservice` | Recibe esas credenciales por variables de entorno (`MINIO_SVCACCT_*`). Nunca usa las credenciales root. |

Variables en `.env`: ver la sección MinIO del [README](../../README.md).

## 2. Dos clientes S3 en el backend (`Program.cs`)

Ambos usan las mismas credenciales y `ForcePathStyle = true` (URLs `host/bucket/key`, necesario con MinIO local).

| Cliente | URL | Uso |
|---|---|---|
| `IAmazonS3` | `MINIO_S3_URL` (default `http://minio:9000`, red interna de Docker) | Operaciones que hace el backend directamente: listar y eliminar. |
| `PresignS3Client` | `MINIO_PUBLIC_URL` (default `https://localhost`) | Generar URLs prefirmadas que usará el **navegador**. |

**Por qué dos:** la URL firmada incluye el host. `minio` solo existe dentro de Docker, así que el navegador no podría usar una URL firmada con `http://minio:9000`. Se firma con la URL pública, que pasa por nginx.

`LocalDevHttpClientFactory` ignora la validación del certificado SSL (certificados autofirmados en desarrollo).

## 3. Metadatos, permisos y endpoints (`FilesController` y `FoldersController`)

Ambos controladores tienen `[Authorize]` (JWT en cookie) y `ActiveUserFilter`, que responde `403` si la cuenta fue desactivada aunque su access token siga vigente.

### Tablas

| Tabla | Columnas principales |
|---|---|
| `Files` | `Id` (uuid), `OwnerId` (FK `Users`), `FolderId` (FK `Folders`), `Name`, `PreviousName`, `ContentType`, `SizeBytes`, `Sha256`, `Status` (`Pending`/`Uploaded`), `CreatedAt`, `UpdatedAt`. Único por `(OwnerId, FolderId, Name)`. |
| `Folders` | `Id`, `OwnerId`, `ParentId` (FK a sí misma, lista de adyacencia), `Name`, `CreatedAt`, `UpdatedAt`. Único por `(OwnerId, ParentId, Name)`. |
| `Users` (nueva columna) | `UserName`: único, minúsculas, 3 a 30 caracteres (`a-z 0-9 - _`), sin nombres reservados (`admin`, `root`, `api`, `files`, `images`, `storage`, `home`, `system`). Se pide en el registro. |

La key del objeto en MinIO es solo el `Id` del archivo, en la raíz del bucket (sin prefijos de usuario ni de carpeta): no contiene el nombre del archivo ni el propietario (viven en la BD) y la jerarquía de carpetas existe solo en la BD, así que mover o renombrar no toca MinIO.

### Carpeta home y raíz

- La **raíz** no es una carpeta: listar `GET /api/folders` sin `parentId` devuelve los **homes** de todos los usuarios activos.
- La **home** de un usuario es su única carpeta con `ParentId = null` (índice único parcial por `OwnerId`). Se llama como su `UserName` y se crea **al activar al usuario**; `GET /api/folders/home` la crea si faltara. No se crean otras carpetas en la raíz, no se puede subir a la raíz y la home no se puede borrar.
- La home se enlaza por `OwnerId`, no por nombre: si en el futuro se permite cambiar el `UserName`, basta renombrar esa carpeta.

### Usuarios desactivados

El estado vive solo en `Users.IsActive`; archivos y carpetas **no** tienen columna propia. `AuthServiceContext` aplica un filtro de consulta (`HasQueryFilter(f => f.Owner.IsActive)`) a `Files` y `Folders`, así que el contenido de un usuario desactivado desaparece de todas las consultas y reaparece solo al reactivarlo, sin actualizar filas. Efecto secundario: ni siquiera un admin ve ese contenido mientras el usuario esté inactivo.

### Permisos

| Acción | Quién |
|---|---|
| Ver, listar y descargar archivos | Cualquier usuario activo (de cualquier propietario) |
| Subir archivos / crear carpetas | Solo dentro de carpetas propias (empezando por su home) |
| Borrar un archivo | El propietario o un admin |
| Borrar una carpeta vacía | El propietario o un admin |
| Borrar una carpeta con contenido | Solo un admin (recursivo: subcarpetas, filas y objetos en MinIO) |
| Borrar una home | Nadie |

Se responde `403` a quien puede ver pero no borrar, y `404` si el recurso no existe o es de un usuario inactivo.

### Endpoints

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/api/folders/home` | Home del usuario autenticado. |
| `GET` | `/api/folders?parentId=` | Subcarpetas de una carpeta; sin `parentId`, los homes. |
| `GET` | `/api/folders/{id}` | Carpeta con su ruta desde la home (breadcrumb). |
| `POST` | `/api/folders` | Crea una subcarpeta `{ name, parentId }` en una carpeta propia. `409` si el nombre ya existe. |
| `DELETE` | `/api/folders/{id}` | Ver permisos. `409` si un usuario no admin la borra con contenido o si es una home. Un admin elimina los objetos de MinIO (`DeleteObjects` en lotes de 1000) antes de quitar las filas. |
| `GET` | `/api/files?folderId=` | Lista los archivos `Uploaded` de una carpeta, con `ownerId`. |
| `GET` | `/api/files/upload-url?fileName=&sha256=&contentType=&folderId=` | `folderId` es obligatorio (`400` sin él). Crea la fila `Pending` y devuelve la URL **PUT** (15 min), `fileId` y `uploadHeaders`. `409` si ya existe ese nombre en la carpeta. |
| `POST` | `/api/files/{id}/confirm` | Hace un `HEAD` a MinIO, comprueba que exista y que su checksum SHA-256 sea el declarado; guarda el tamaño real y pasa a `Uploaded`. Solo el propietario. |
| `GET` | `/api/files/{id}/download-url` | Comprueba tamaño y checksum del objeto contra la BD (`409` si no coinciden) y devuelve la URL **GET** (30 min) más `sha256`, `fileName`, `contentType`. |
| `DELETE` | `/api/files/{id}` | Borra el objeto y la fila (propietario o admin). |

El archivo nunca pasa por la API: el backend solo entrega el permiso temporal (la URL firmada).

## 4. Flujo de subida

```
1. Navegador calcula el SHA-256 del archivo (crypto.subtle)
2. Navegador --GET /api/files/upload-url?fileName=..&sha256=..--> Backend
   Backend crea la fila Pending, firma localmente (sin hablar con MinIO)
   y responde { uploadUrl, fileId, uploadHeaders }
3. Navegador --PUT uploadUrl (headers firmados, body = archivo)--> nginx --> MinIO
   MinIO valida la firma Y que el body tenga ese SHA-256
4. Navegador --POST /api/files/{id}/confirm--> Backend
   Backend hace HEAD a MinIO, verifica checksum y pasa a Uploaded
```

**El GET y el PUT son peticiones distintas a servidores distintos.** El GET es solo "dame una URL". El `Verb = HttpVerb.PUT` del backend no es el verbo de esa petición: es un dato que se mete dentro de la firma y significa "esta URL solo sirve para hacer un PUT".

### Integridad en la subida: el hash va firmado en la URL

El SHA-256 que declara el cliente se convierte a base64 y se firma como header `x-amz-checksum-sha256`:

```csharp
var request = new GetPreSignedUrlRequest { /* bucket, key, Verb = PUT, Expires, ContentType */ };
request.Headers["x-amz-checksum-sha256"] = checksumBase64;
```

La URL queda con `X-Amz-SignedHeaders=content-type;host;x-amz-checksum-sha256`. Al recibir el PUT, MinIO calcula el SHA-256 del body y lo compara con el header; si no coincide responde `XAmzContentChecksumMismatch`. Además guarda el checksum junto al objeto. Esto cierra el hueco de `UNSIGNED-PAYLOAD`: la URL ya no autoriza "escribir cualquier cosa" sino "escribir contenido con este hash". Resultados probados:

| PUT | Resultado |
|---|---|
| Body que coincide con el hash declarado | `200` |
| Body distinto al hash declarado | `XAmzContentChecksumMismatch` |
| Sin el header firmado | `AccessDenied` (headers no firmados) |
| Header con otro hash | `SignatureDoesNotMatch` |

El cliente debe enviar **exactamente** los `uploadHeaders` que devuelve el backend (`Content-Type` y `x-amz-checksum-sha256`).

### Integridad en la descarga (después de subir)

Dos verificaciones:

1. **Backend, antes de emitir la URL:** `HEAD` con `ChecksumMode.ENABLED`; compara tamaño y checksum del objeto con los de la BD. Si alguien reemplazó el objeto directamente en MinIO (sin pasar por una subida con checksum), el checksum no coincide y responde `409`. Probado reemplazando el objeto con `mc cp`.
2. **Frontend, después de descargar:** descarga el archivo como blob, recalcula su SHA-256 con `crypto.subtle` y lo compara con el `sha256` de la BD; solo entonces lo muestra o lo guarda. Por eso "Ver" abre un `blob:` y "Descargar" usa un `<a download>` sobre el blob.

Límites: el archivo se carga completo en memoria del navegador (no apto para archivos muy grandes) y `crypto.subtle` solo existe en contextos seguros (HTTPS o `localhost`; por HTTP en una IP de LAN no funcionará). La verificación 1 protege contra un reemplazo en el almacenamiento, pero no contra alguien que pueda escribir en MinIO *con* el header de checksum de otro contenido y además modificar la BD.

Frontend: `hooks/uploadFiles.jsx` (subida), `components/ArchivosModal.jsx` (explorador de carpetas y archivos) y `utils/sha256.js`.

## 5. Ver / descargar / listar / borrar

- **Explorar:** `ArchivosModal` parte de la raíz (los homes), entra a carpetas con `GET /api/folders?parentId=` y lista archivos con `GET /api/files?folderId=`.
- **Ver / descargar:** `GET /api/files/{id}/download-url`, descarga como blob y verificación del hash (ver arriba).
- **Eliminar:** `DELETE /api/files/{id}` o `DELETE /api/folders/{id}`; lo ejecuta el backend con `IAmazonS3`.

## 6. Cómo se calcula la firma (AWS Signature V4)

Lo hace el SDK dentro de `GetPreSignedURLAsync`. El secret key **nunca viaja**: solo se usa para derivar una clave y firmar.

1. **Canonical request** (texto con formato exacto):
   ```
   PUT
   /files/3f2c9d1e-7b4a-4c55-9a0e-1d2b3c4d5e6f
   X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=...&X-Amz-Date=...&X-Amz-Expires=900&X-Amz-SignedHeaders=content-type%3Bhost
   content-type:image/png
   host:localhost

   content-type;host
   UNSIGNED-PAYLOAD
   ```
   Orden: verbo, ruta, query ordenado, headers firmados, lista de headers firmados, hash del body.
2. **String to sign:**
   ```
   AWS4-HMAC-SHA256
   20261022T150000Z
   20261022/us-east-1/s3/aws4_request
   <SHA256 hex de la canonical request>
   ```
3. **Clave de firma** (cadena de HMAC-SHA256 que usa el secret):
   ```
   kDate    = HMAC("AWS4" + secretKey, "20261022")
   kRegion  = HMAC(kDate,    "us-east-1")
   kService = HMAC(kRegion,  "s3")
   kSigning = HMAC(kService, "aws4_request")
   ```
4. **Firma:** `signature = hex(HMAC(kSigning, stringToSign))`, que se añade como `X-Amz-Signature`.

La URL final queda así:

```
https://localhost/files/3f2c9d1e-7b4a-4c55-9a0e-1d2b3c4d5e6f
  ?X-Amz-Algorithm=AWS4-HMAC-SHA256
  &X-Amz-Credential=ACCESSKEY/fecha/us-east-1/s3/aws4_request
  &X-Amz-Date=...&X-Amz-Expires=900
  &X-Amz-SignedHeaders=content-type;host
  &X-Amz-Signature=...
```

**Verificación en MinIO:** repite los 4 pasos con su copia del secret y los datos que realmente llegaron (verbo, ruta, query, headers, host). Si su firma es igual a `X-Amz-Signature` y `X-Amz-Date + X-Amz-Expires` no ha pasado, acepta. Cambiar cualquier dato firmado (verbo, ruta, host, content-type, expiración) invalida la firma y MinIO responde `403 SignatureDoesNotMatch`.

### `contentType` en la firma

Si se manda `contentType`, `content-type` pasa a ser un header firmado: la URL solo es válida para un PUT con **ese mismo** `Content-Type`. Además MinIO lo guarda como metadato del objeto y lo devuelve al descargar, por eso el navegador sabe si mostrar una imagen o un PDF.

- Es opcional: sin él, MinIO acepta cualquier `Content-Type`.
- **No valida el contenido real**: el cliente declara el tipo; solo se asegura que el header coincida.

### `UNSIGNED-PAYLOAD`

Es el literal que ocupa la última línea de la canonical request, donde normalmente iría el hash SHA-256 del body. En una URL prefirmada el backend aún no tiene el archivo (está en el navegador), así que no puede hashearlo. SDK y MinIO usan ese mismo literal y las firmas coinciden sin importar el contenido.

Por sí solo, esto significa que la URL autoriza "escribir en esta key, con este verbo, hasta tal hora", **no** "escribir este contenido". En este proyecto se compensa firmando además el header `x-amz-checksum-sha256` (sección 4). Sigue sin haber límite de tamaño firmado (`Content-Length` no se firma).

## 7. nginx (`Backend/nginx/locations.conf`)

La URL firmada apunta a `https://localhost/images/...`, o sea a nginx:

```
Navegador --PUT--> nginx (:443) --> minio:9000
```

```nginx
location ~ ^/(images|files)/ {
	proxy_pass http://minio:9000;
	proxy_set_header Host $http_host;
	client_max_body_size 0;
	...
}
```

- Solo se enruta a MinIO si la ruta empieza por `images` o `files`: **el nombre del bucket debe ser uno de esos** (por defecto `images`).
- `Host $http_host` conserva el host original. Es crítico: la firma incluye el host y MinIO debe verlo igual que como se firmó.
- Responde el preflight `OPTIONS` (CORS) con 204 y cabeceras `Access-Control-*`, necesario antes del `PUT`.
- `client_max_body_size 0` quita el límite de tamaño de subida.

### Variables `$` de nginx usadas

| Variable | Contenido |
|---|---|
| `$http_host` | Header `Host` tal como lo envió el cliente |
| `$remote_addr` | IP del cliente conectado a nginx |
| `$proxy_add_x_forwarded_for` | `X-Forwarded-For` existente + `$remote_addr` |
| `$scheme` | `http` o `https` de la petición recibida |
| `$http_upgrade`, `$http_connection` | Headers `Upgrade`/`Connection` (WebSocket: consola de MinIO y HMR de Vite) |
| `$request_method` | Verbo HTTP (se usa para detectar `OPTIONS`) |

Cualquier `$http_xxx` es el header `Xxx` de la petición (minúsculas, guiones → `_`).

## 8. Puntos a tener en cuenta

- Subidas `Pending` que nunca se confirman quedan como fila (y posiblemente objeto) hasta que se reintenta subir un archivo con el mismo nombre; falta un job de limpieza.
- `DELETE /api/User/{id}` sigue existiendo: borra en cascada las filas de `Files`/`Folders` del usuario pero **sus objetos quedan huérfanos en MinIO**. Como los usuarios se desactivan y no se borran, conviene retirar ese endpoint o limpiar antes los objetos.
- Mientras un usuario está desactivado, su contenido no es accesible ni siquiera para un admin (filtro de consulta).
- No se pueden mover ni renombrar carpetas/archivos todavía (no hay endpoints); `PreviousName` está reservado para el renombrado.
- Sin límite de tamaño ni validación de tipo real del contenido, y sin escaneo de malware: el hash detecta modificaciones, no archivos maliciosos subidos desde el inicio.
- **`MINIO_PUBLIC_URL` no está definida en `docker-compose.yml`**, así que las URLs se firman siempre con `https://localhost`. Acceder por IP de LAN (`PUBLIC_HOST`) hará que el host de la URL no coincida con el del navegador. Para LAN hay que pasar `MINIO_PUBLIC_URL` al servicio `authservice`.
- CORS `*` en nginx y MinIO es aceptable en desarrollo; restringirlo en producción.
