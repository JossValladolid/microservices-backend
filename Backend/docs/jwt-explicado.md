# Autenticación con JWT — access token, refresh token y cookies HttpOnly

Notas de referencia sobre cómo funciona el login de este proyecto: backend (`AuthService/Services/TokenService.cs`, `AuthService/Program.cs`, `AuthService/Controllers/AuthController.cs`) y frontend (`React/src/api.js`, `AuthContext.jsx`, `ProtectedRoute.jsx`, `Login.jsx`, `Dashboard.jsx`).

## Conceptos base

### ¿Por qué dos tokens?

- **Access token (JWT)**: es como un *gafete*. Se muestra en cada petición para demostrar quién eres. Lleva dentro tu id, nombre, email y si eres admin, todo **firmado** con `JWT_SECRET`. El backend no consulta la base de datos para validarlo: solo revisa que la firma sea correcta y que no haya expirado. Es rápido, pero **no se puede revocar**: si alguien lo roba, sirve hasta que expira. Por eso dura poco (**15 minutos**).
- **Refresh token**: es la *llave para pedir un gafete nuevo*. No es un JWT, es un texto aleatorio de 64 bytes que se guarda en la tabla `Sessions`. Dura **7 días** y **sí se puede revocar**: basta con borrar la fila de la tabla.

La combinación da peticiones rápidas (gracias al access token) y sesiones que se pueden cerrar de verdad (gracias al refresh token).

### Anatomía de un JWT

Un JWT es un texto con tres partes separadas por puntos: `xxxxx.yyyyy.zzzzz`.

1. **Header**: qué algoritmo se usó para firmar (HS256).
2. **Payload**: los *claims* (datos del usuario), el emisor, la audiencia y la expiración. Va en Base64, **no está cifrado**: cualquiera puede leerlo en jwt.io. Nunca hay que poner contraseñas ahí.
3. **Firma**: un HMAC del header y el payload, calculado con `JWT_SECRET`. Si alguien cambia un solo carácter del payload (por ejemplo, para hacerse admin), la firma ya no coincide y el backend rechaza el token.

### El flujo completo

```
LOGIN
  React ── POST /api/Auth/login {email, password} ──▶ Backend
                                                     ├ verifica contraseña (BCrypt)
                                                     ├ crea fila en Sessions con refresh token
  React ◀── 200 {usuario} + 2 cookies HttpOnly ──────┘

PETICIÓN NORMAL (el navegador manda la cookie solo)
  React ── GET /api/User  (cookie: access token) ──▶ Backend valida firma y expiración → 200

ACCESS TOKEN EXPIRADO (pasaron 15 min)
  React ── GET /api/User ──▶ 401
  apiFetch ── POST /api/Auth/refresh (cookie: refresh token) ──▶ Backend
                                                     ├ busca la sesión en la BD
                                                     ├ genera refresh token NUEVO (rotación)
  apiFetch ◀── 200 + cookies nuevas ─────────────────┘
  apiFetch ── reintenta GET /api/User ──▶ 200      (el usuario no notó nada)

LOGOUT
  React ── POST /api/Auth/logout ──▶ Backend borra la sesión de la BD y las cookies
```

### Las cookies (y por qué no `localStorage`)

Cuando el usuario inicia sesión, el backend manda estas dos cookies:

```
authservice_access_token=...;  path=/api;      max-age=900;    httponly; secure; samesite=strict
authservice_refresh_token=...; path=/api/Auth; max-age=604800; httponly; secure; samesite=strict
```

| Atributo | Para qué sirve |
|---|---|
| `HttpOnly` | JavaScript **no puede leerlas**. Si alguien inyecta código malicioso en la página (XSS), no puede robar los tokens. Con `localStorage` sí podría. |
| `Secure` | Solo viajan por HTTPS. |
| `SameSite=Strict` | El navegador no las manda si la petición viene de otro sitio. Protege contra CSRF: una página maliciosa no puede usar tu sesión. |
| `Path=/api` | El access token solo se manda a la API, no al pedir imágenes o CSS. |
| `Path=/api/Auth` | El refresh token solo se manda a `refresh` y `logout`, así viaja lo menos posible. |
| `max-age` | El navegador las borra solas cuando expiran. |

> **Ojo con `localhost`:** todos los proyectos que corres en `localhost` comparten cookies, sin importar el puerto. Por eso las cookies tienen el prefijo `authservice_`. Cuando se llamaban `access_token`, una cookie vieja con ese mismo nombre, de otro proyecto, se mandaba junto con la nuestra. ASP.NET leía la equivocada y rechazaba todo con `IDX10517: Signature validation failed`.

---

# Backend

## `Services/TokenService.cs` — cómo se crean y validan los tokens

Es el **único lugar** donde vive la configuración JWT. Antes, cada archivo leía las variables de entorno por su cuenta, y por eso pasó el bug de `JWT_KEY` vs `JWT_SECRET`: el token se firmaba con una variable y se validaba con otra.

### Nombres de las cookies

```csharp
public const string AccessTokenCookie = "authservice_access_token";
public const string RefreshTokenCookie = "authservice_refresh_token";
```

Son `const` porque nunca cambian. Se usan en dos archivos: `Program.cs` para **leer** la cookie y `AuthController` para **escribirla**. Tenerlos en un solo lugar evita escribir el nombre distinto en cada archivo.

### Propiedades

```csharp
public string Issuer { get; }
public string Audience { get; }
public SymmetricSecurityKey SigningKey { get; }
public TimeSpan AccessTokenLifetime { get; }
public TimeSpan RefreshTokenLifetime { get; }
```

Solo tienen `get;`, sin `set;`: se asignan en el constructor y nadie las puede cambiar después.

- **Issuer** (emisor): "quién creó el token", por ejemplo `AuthService`. Se escribe dentro del JWT y al validar se comprueba que coincida.
- **Audience** (audiencia): "para quién es el token", por ejemplo tu API. Igual: se escribe y se comprueba. Sirve para que un token hecho para otra app no sea aceptado en la tuya.
- **SigningKey**: `JWT_SECRET` convertido a la clase que usa la librería para firmar. *Simétrica* significa que la misma llave firma y verifica.
- **Lifetimes**: cuánto dura cada token, como `TimeSpan` (un tipo de .NET para duraciones) en lugar de un `int` suelto.

### Constructor

Se ejecuta **una sola vez**, cuando arranca la app (desde `Program.cs`).

```csharp
var secret = Environment.GetEnvironmentVariable("JWT_SECRET");
if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
    throw new InvalidOperationException("JWT_SECRET debe estar definido y tener al menos 32 caracteres.");
```

El algoritmo HmacSha256 exige una llave de al menos 256 bits (32 bytes). Si no se cumple, la app **no arranca** y dice exactamente por qué. Antes arrancaba "bien" y luego fallaba en cada petición con `IDX10703: key length is zero`.

```csharp
Issuer = Environment.GetEnvironmentVariable("JWT_ISSUER")
    ?? throw new InvalidOperationException("JWT_ISSUER no está definido.");
```

`??` significa "si lo de la izquierda es `null`, usa lo de la derecha", y la derecha lanza un error. Es la misma idea: fallar al arrancar si falta una variable.

```csharp
var minutes = int.TryParse(Environment.GetEnvironmentVariable("JWT_ACCESS_EXPIRATION_MINUTES"), out var m) ? m : 15;
```

`int.TryParse` intenta convertir el texto a número. Si puede, devuelve `true` y deja el número en `m`. El `? m : 15` es un if corto: si se pudo convertir, usa `m`; si no (la variable no existe o no es número), usa 15.

### `ValidationParameters` — las reglas para aceptar un token

```csharp
public TokenValidationParameters ValidationParameters => new()
{
    ValidateIssuer = true,           // revisa que el "iss" del token sea nuestro Issuer
    ValidateAudience = true,         // revisa que el "aud" sea nuestra Audience
    ValidateLifetime = true,         // rechaza tokens expirados
    ValidateIssuerSigningKey = true, // revisa la firma con nuestra llave
    ValidIssuer = Issuer,
    ValidAudience = Audience,
    IssuerSigningKey = SigningKey,
    ClockSkew = TimeSpan.Zero
};
```

El middleware de `Program.cs` usa estas reglas en cada petición.

- **`ClockSkew = TimeSpan.Zero`**: por defecto .NET da 5 minutos de tolerancia después de que el token expira, por si los relojes de los servidores no están sincronizados. Con `Zero`, el token muere exactamente a los 15 minutos.
- El `=>` significa que es una propiedad que **calcula** su valor cada vez que se lee. No es un campo guardado.

### `GenerateAccessToken(user)`

```csharp
var claims = new List<Claim>
{
    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new(ClaimTypes.Email, user.Email!),
    new(ClaimTypes.Name, user.Name!)
};
if (user.IsAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
```

- Un **claim** es un dato "afirmado" sobre el usuario que va dentro del token.
- `ClaimTypes.Role` es especial: es el que revisa `[Authorize(Roles = "Admin")]`. Si el usuario no es admin, ese claim simplemente no se agrega.
- El `!` en `user.Email!` le dice al compilador "confía, esto no es null", para quitar la advertencia de nulos.

```csharp
var token = new JwtSecurityToken(
    issuer: Issuer,
    audience: Audience,
    claims: claims,
    expires: DateTime.UtcNow.Add(AccessTokenLifetime),
    signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

return new JwtSecurityTokenHandler().WriteToken(token);
```

Arma el objeto token, y `WriteToken` lo convierte al texto `xxxxx.yyyyy.zzzzz`. Se usa `UtcNow` y no `Now` para que no influya la zona horaria.

### `GenerateRefreshToken()`

```csharp
return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
```

- `RandomNumberGenerator` es el generador aleatorio **criptográficamente seguro**. Nunca hay que usar `new Random()` para esto, porque es predecible.
- 64 bytes aleatorios son imposibles de adivinar, y Base64 los convierte en texto para guardarlos en la BD y en la cookie.
- Es `static` porque no usa ninguna propiedad de la clase. Por eso se llama `TokenService.GenerateRefreshToken()` y no `_tokenService.GenerateRefreshToken()`.

### De dónde salió cada cosa

| Antes | Ahora en `TokenService` |
|---|---|
| `GenerateAccessToken` en `AuthController` | `GenerateAccessToken`: mismos claims y algoritmo, pero usa la configuración leída al arrancar |
| `GenerateRefreshToken` en `AuthController` | `GenerateRefreshToken`: lo mismo, en una línea |
| `GetRefreshTokenExpirationDays()` y lectura de minutos | Propiedades `AccessTokenLifetime` y `RefreshTokenLifetime` |
| `TokenValidationParameters` escritos a mano en `Program.cs` | `ValidationParameters` |
| `GetPrincipalFromExpiredToken` en `AuthController` | **Eliminado**: ya no hace falta (ver `Refresh`) |

La idea de la separación: **`TokenService` sabe *cómo* se hacen y validan los tokens; `AuthController` sabe *cuándo* hay que darlos o quitarlos.**

## `Program.cs` — conectar el middleware JWT

```csharp
var tokenService = new TokenService();
builder.Services.AddSingleton(tokenService);
```

- **Línea 1**: crea el servicio, lo que lee las variables y falla si falta algo.
- **Línea 2**: lo registra en la **inyección de dependencias** como *singleton*, una sola instancia compartida por toda la app.
- Cuando ASP.NET crea un `AuthController` y ve que su constructor pide un `TokenService`, le pasa esta misma instancia automáticamente.
- Se crea "a mano" con `new` (en vez de `AddSingleton<TokenService>()`) porque también se necesita justo abajo, para configurar el middleware.

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = tokenService.ValidationParameters;
```

Activa la autenticación con JWT y le da las reglas de validación de `TokenService`. Como son las mismas propiedades con las que se firmó el token, es imposible que firma y validación queden desincronizadas.

```csharp
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies[TokenService.AccessTokenCookie];
                return Task.CompletedTask;
            }
        };
```

- **`OnMessageReceived`** es un "gancho" (*hook*) que se ejecuta al inicio de cada petición, **antes** de que el middleware busque el token.
- Por defecto el middleware lo buscaría en el header `Authorization: Bearer ...`. Al asignar `context.Token`, le decimos "el token es este, el de la cookie".
- Si la cookie no existe, `context.Token` queda en `null` y la petición se trata como no autenticada.
- `return Task.CompletedTask` existe porque el evento es asíncrono, pero aquí no hay nada que esperar.

**Qué pasa en cada petición protegida:**

1. Llega `GET /api/User` con la cookie.
2. `OnMessageReceived` saca el token de la cookie.
3. El middleware lo valida con `ValidationParameters`.
4. Si es válido, crea el `User` (un `ClaimsPrincipal`) con los claims del token.
5. `[Authorize]` deja pasar. Si el token es inválido o falta, responde **401** sin llegar al controller. Si el endpoint pide `Roles = "Admin"` y el token no tiene ese claim, responde **403**.

## `Controllers/AuthController.cs` — cuándo se dan y se quitan los tokens

| Endpoint | Qué hace |
|---|---|
| `POST /api/Auth/login` | Verifica la contraseña, borra las sesiones vencidas de ese usuario, crea una sesión nueva y pone las cookies |
| `POST /api/Auth/refresh` | Lee la cookie del refresh token, busca la sesión y, si es válida, emite tokens nuevos |
| `POST /api/Auth/logout` | Borra la sesión de la BD y las cookies |
| `GET /api/Auth/me` | Devuelve quién es el usuario de la sesión actual |

### Constructor

```csharp
public AuthController(AuthServiceContext context, TokenService tokenService)
{
    _context = context;
    _tokenService = tokenService;
}
```

ASP.NET le pasa la BD (`context`) y el `TokenService` registrado en `Program.cs`. El controller ya no lee variables de entorno.

### `Login`

```csharp
var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == loginViewModel.Email);
if (user == null || !BCrypt.Net.BCrypt.Verify(loginViewModel.Password!, user.Password!))
    return Unauthorized(new { message = "Credenciales inválidas" });
```

- Busca por email y compara la contraseña con el hash usando BCrypt.
- El mismo mensaje para "no existe" y "contraseña mala" es a propósito: así nadie puede averiguar qué emails están registrados.

```csharp
await _context.Sessions
    .Where(s => s.UserId == user.Id && s.ExpiryDate <= DateTime.UtcNow)
    .ExecuteDeleteAsync();
```

Borra las sesiones vencidas de ese usuario. `ExecuteDeleteAsync` manda un `DELETE ... WHERE ...` directo a la BD, sin cargar las filas en memoria. Sin esto, cada login agregaría una fila y nunca se borraría ninguna.

```csharp
var session = new Session { UserId = user.Id, User = user };
_context.Sessions.Add(session);
return await IssueTokens(session);
```

- Crea la sesión **sin token todavía**: `IssueTokens` se lo pone.
- `Add` solo la marca para insertar; el `INSERT` real ocurre en el `SaveChangesAsync` dentro de `IssueTokens`.

### `Refresh`

```csharp
var refreshToken = Request.Cookies[TokenService.RefreshTokenCookie];

var session = refreshToken == null ? null : await _context.Sessions
    .Include(s => s.User)
    .FirstOrDefaultAsync(s => s.Token == refreshToken);
```

- Lee el refresh token **de la cookie**; no hay body.
- Si no hay cookie, ni siquiera consulta la BD (`session = null`).
- `.Include(s => s.User)` trae también al usuario en la misma consulta (un JOIN). Se necesita para crear el nuevo access token con sus datos **actualizados**: por ejemplo, si ahora es admin.

**La simplificación:** antes `/refresh` pedía también el access token expirado, solo para sacar el id del usuario, y necesitaba un método de unas 30 líneas (`GetPrincipalFromExpiredToken`) para leerlo. Ahora no hace falta, porque la fila de `Sessions` ya sabe a qué usuario pertenece.

```csharp
if (session == null || session.ExpiryDate <= DateTime.UtcNow)
{
    if (session != null)
    {
        _context.Sessions.Remove(session);
        await _context.SaveChangesAsync();
    }
    DeleteTokenCookies();
    return Unauthorized(...);
}
return await IssueTokens(session);
```

Si la sesión no existe (el token es falso, ya fue rotado o se cerró la sesión) o ya expiró:

1. Si existía pero estaba expirada, la borra.
2. Borra las cookies del navegador.
3. Responde **401**. Esa es la señal para que el frontend mande al usuario a `/login`.

Si la sesión es válida, `IssueTokens` le da tokens nuevos.

### `Logout`

```csharp
var refreshToken = Request.Cookies[TokenService.RefreshTokenCookie];
if (refreshToken != null)
{
    await _context.Sessions.Where(s => s.Token == refreshToken).ExecuteDeleteAsync();
}
DeleteTokenCookies();
return NoContent();
```

- Borra la sesión de la BD. Desde ese momento ese refresh token ya no sirve, **aunque alguien lo hubiera copiado**.
- Borra las cookies y responde 204 (éxito, sin contenido).
- No tiene `[Authorize]` a propósito: si el access token ya expiró, igual se debe poder cerrar sesión.
- El access token sigue siendo técnicamente válido hasta que expira (máximo 15 minutos), pero el navegador ya no lo tiene porque se borró la cookie.

### `Me`

```csharp
[Authorize]
[HttpGet("me")]
public async Task<ActionResult<UserResponseDTO>> Me()
{
    if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Unauthorized();

    var user = await _context.Users.FindAsync(userId);
    if (user == null) return Unauthorized();

    return ToDto(user);
}
```

- `[Authorize]`: solo llega aquí si el access token de la cookie es válido.
- `User` es el usuario que armó el middleware a partir de los claims. `FindFirstValue(ClaimTypes.NameIdentifier)` saca el id que se puso en `GenerateAccessToken`.
- Busca al usuario en la BD, en vez de solo devolver los claims, para que los datos estén al día y para detectar si lo borraron.
- **Para qué existe:** el frontend no puede leer las cookies HttpOnly, así que al recargar la página no tiene forma de saber quién es. Le pregunta al backend.

### `IssueTokens` — el corazón

```csharp
private async Task<UserResponseDTO> IssueTokens(Session session)
{
    var user = session.User!;

    session.Token = TokenService.GenerateRefreshToken();
    session.ExpiryDate = DateTime.UtcNow.Add(_tokenService.RefreshTokenLifetime);
    await _context.SaveChangesAsync();
```

Le pone a la sesión un refresh token **nuevo** y una expiración nueva.

- **En login**, `SaveChangesAsync` hace el `INSERT` de la sesión recién creada.
- **En refresh**, hace un `UPDATE` porque la sesión ya existía. El token anterior queda reemplazado y deja de servir. Eso es la **rotación**.

**Por qué rotar:** si alguien roba un refresh token y lo usa, el dueño legítimo recibirá 401 en su siguiente refresh. El robo dura poco y se nota.

```csharp
    Response.Cookies.Append(TokenService.AccessTokenCookie, _tokenService.GenerateAccessToken(user),
        CookieOptions("/api", _tokenService.AccessTokenLifetime));
    Response.Cookies.Append(TokenService.RefreshTokenCookie, session.Token,
        CookieOptions("/api/Auth", _tokenService.RefreshTokenLifetime));

    return ToDto(user);
}
```

- `Response.Cookies.Append` agrega un header `Set-Cookie` a la respuesta. El navegador lo recibe y guarda la cookie.
- La cookie dura lo mismo que el token (`MaxAge`), así el navegador la borra sola cuando ya no sirve.
- Devuelve solo los datos del usuario, **sin tokens**. Eso es lo que llega al `response.json()` del frontend.

### `DeleteTokenCookies`

```csharp
Response.Cookies.Delete(TokenService.AccessTokenCookie, CookieOptions("/api", TimeSpan.Zero));
Response.Cookies.Delete(TokenService.RefreshTokenCookie, CookieOptions("/api/Auth", TimeSpan.Zero));
```

Para borrar una cookie, el servidor manda un `Set-Cookie` con fecha de expiración en el pasado. **Importante:** el `Path` debe ser **el mismo** con el que se creó. Si no, el navegador la considera otra cookie distinta y no la borra. Por eso se pasan las mismas opciones.

### `CookieOptions`

```csharp
private static CookieOptions CookieOptions(string path, TimeSpan lifetime) => new()
{
    HttpOnly = true,                // JavaScript no puede leerla (document.cookie no la ve)
    Secure = true,                  // solo se manda por HTTPS
    SameSite = SameSiteMode.Strict, // no se manda en peticiones que vienen de otros sitios
    Path = path,                    // solo se manda a URLs que empiezan con este path
    MaxAge = lifetime               // cuánto vive en el navegador
};
```

Es un "molde" para no repetir las mismas cinco opciones en cada cookie.

### `ToDto`

Convierte un `User` (que tiene el hash de la contraseña) en un `UserResponseDTO` (que **no** lo tiene). Así nunca se manda el hash al frontend por accidente.

## Permisos en `UserController`

`UserController` tiene `[Authorize]` en toda la clase: todos sus endpoints exigen sesión, salvo los que digan otra cosa.

| Endpoint | Atributo | Quién puede |
|---|---|---|
| `POST /api/User` (registro) | `[AllowAnonymous]` | Cualquiera, sin sesión |
| `GET /api/User`, `GET /api/User/{id}` | (el de la clase) | Cualquier usuario logueado |
| `PUT /api/User/{id}` | `[Authorize(Roles = "Admin")]` | Solo admins |
| `DELETE /api/User/{id}` | `[Authorize(Roles = "Admin")]` | Solo admins |
| `PATCH AddAdmin` / `RevokeAdmin` | `[Authorize(Roles = "Admin")]` | Solo admins |

- **401 Unauthorized** = "no sé quién eres": falta el token o no es válido.
- **403 Forbidden** = "sé quién eres, pero no tienes permiso": el token es válido pero no tiene el claim `Role = Admin`.

---

# Frontend

## `src/api.js` — fetch que renueva el token solo

### Variables del módulo

```js
let alPerderSesion = () => {}
let refrescoEnCurso = null
```

- Viven fuera de cualquier componente, así que son **compartidas** por toda la app.
- **`alPerderSesion`**: una función que se ejecutará cuando la sesión muera. Empieza vacía (`() => {}`) hasta que `AuthContext` registre la suya.
- **`refrescoEnCurso`**: guarda la promesa del refresh mientras está en proceso; si no hay ninguno, es `null`.

### `alExpirarSesion(callback)`

```js
export function alExpirarSesion(callback) {
  alPerderSesion = callback
}
```

Es la forma de que `api.js`, que no es un componente de React, pueda avisarle a React que la sesión terminó. `AuthContext` llama a esto con `() => setUsuarioActual(null)`.

### `refrescarTokens()`

```js
function refrescarTokens() {
  if (!refrescoEnCurso) {
    refrescoEnCurso = fetch('/api/Auth/refresh', { method: 'POST' })
      .then(response => response.ok)
      .catch(() => false)
      .finally(() => { refrescoEnCurso = null })
  }
  return refrescoEnCurso
}
```

- Llama a `/refresh`. No manda body ni tokens: el navegador adjunta la cookie del refresh token solo, porque la URL empieza con `/api/Auth`.
- Devuelve una promesa que termina en `true` (se renovó) o `false` (falló, o no hubo red, gracias al `.catch`).
- `.finally` limpia la variable al terminar, para que el siguiente refresh (dentro de 15 minutos) haga una petición nueva.

**El truco de `refrescoEnCurso`:** supón que el Dashboard hace 3 peticiones a la vez y el access token acaba de expirar.

- **Sin el truco:** las 3 reciben 401 y las 3 llaman a `/refresh`. La primera rota el refresh token; la segunda y la tercera mandan el token **viejo**, reciben 401 y te sacan de la sesión.
- **Con el truco:** la primera crea la promesa. Las otras dos ven que ya existe (`if (!refrescoEnCurso)` es falso) y **reciben la misma promesa**. Se hace un solo refresh y las tres esperan su resultado.

### `apiFetch(url, opciones)`

```js
export async function apiFetch(url, opciones) {
  const response = await fetch(url, opciones)
  if (response.status !== 401) return response

  if (await refrescarTokens()) return fetch(url, opciones)

  alPerderSesion()
  return response
}
```

Recibe lo mismo que `fetch` y devuelve lo mismo que `fetch`, así que se usa igual. Paso a paso:

1. Hace la petición normal. El navegador manda la cookie del access token solo, porque la URL empieza con `/api`.
2. Si **no** es 401 (fue 200, 403, 404, 500, etc.), devuelve la respuesta tal cual.
3. Si es 401, el access token probablemente expiró, así que intenta renovar.
4. Si se renovó, el navegador ya tiene las cookies nuevas y repite la **misma** petición. El componente recibe esa segunda respuesta y nunca se entera del 401.
5. Si no se pudo renovar, la sesión murió: avisa a `AuthContext` y devuelve el 401 original.

**Por qué no hay headers con tokens:** con cookies, el navegador hace ese trabajo. Si los tokens estuvieran en `localStorage`, habría que agregar `Authorization: Bearer ...` a mano en cada petición.

## Cómo se usa en los `.jsx`

### `AuthContext.jsx` — el dueño del estado de sesión

```jsx
const [usuarioActual, setUsuarioActual] = useState(null)
const [verificando, setVerificando] = useState(true)
```

- `usuarioActual`: el usuario logueado, o `null` si no hay sesión.
- `verificando`: empieza en `true` porque al cargar la página **todavía no se sabe** si hay sesión.

```jsx
useEffect(() => {
  alExpirarSesion(() => setUsuarioActual(null))

  apiFetch('/api/Auth/me')
    .then(response => response.ok ? response.json() : null)
    .then(usuario => setUsuarioActual(usuario))
    .catch(() => setUsuarioActual(null))
    .finally(() => setVerificando(false))
}, [])
```

- El `[]` final hace que se ejecute **una sola vez**, cuando la app arranca o se recarga.
- **Primero** registra qué hacer si la sesión muere: poner el usuario en `null`.
- **Después** pregunta `/me`. Hay tres casos:
  - **Access token válido:** 200 con el usuario, y `usuarioActual` queda lleno.
  - **Access token expirado pero refresh válido:** `apiFetch` renueva solo y reintenta. También termina en 200.
  - **Sin sesión:** 401, y `usuarioActual` queda en `null`.
- Al final, en cualquier caso, `verificando` pasa a `false`.

```jsx
const iniciarSesion = (usuario) => setUsuarioActual(usuario)
```

Solo guarda el usuario en el estado, porque el backend ya dejó las cookies.

```jsx
const cerrarSesion = async () => {
  try {
    await fetch('/api/Auth/logout', { method: 'POST' })
  } finally {
    setUsuarioActual(null)
  }
}
```

- Usa `fetch` normal y no `apiFetch`: no tiene sentido renovar el token para cerrar sesión.
- El `finally` asegura que, aunque falle la red, en la pantalla la sesión se cierre.

```jsx
<AuthContext.Provider value={{ usuarioActual, verificando, iniciarSesion, cerrarSesion }}>
```

Pone esos cuatro valores a disposición de cualquier componente dentro del Provider, que en `main.jsx` envuelve toda la app. `useAuth()` es un atajo para leer ese contexto; si se usa fuera del Provider, lanza un error claro.

### `ProtectedRoute.jsx` — el guardia de las rutas privadas

```jsx
const { usuarioActual, verificando } = useAuth()

if (verificando) return <p>Cargando...</p>
if (!usuarioActual) return <Navigate to="/login" replace />
return <Outlet />
```

- **Mientras se verifica:** muestra "Cargando...". Sin esto, al recargar `/home` vería `usuarioActual = null` durante una fracción de segundo y mandaría a login aunque hubiera sesión.
- **Si no hay usuario:** redirige a `/login`.
- **Si hay usuario:** muestra la página hija (`<Outlet />` es el `Dashboard`).
- Como `usuarioActual` es estado de React, cuando `alPerderSesion` o `cerrarSesion` lo ponen en `null`, este componente se vuelve a renderizar y saca al usuario a `/login` automáticamente.

### `Login.jsx`

```jsx
const { iniciarSesion } = useAuth()
...
const response = await fetch('/api/Auth/login', { method: 'POST', ... body: JSON.stringify({ email, password }) })
if (response.ok) {
  const usuario = await response.json()
  toast.success(`Hola, ${usuario.name}`)
  iniciarSesion(usuario)
  navigate('/')
}
```

- Usa `fetch` normal: no hay sesión todavía, así que no hay nada que renovar. Además, un 401 aquí significa "contraseña incorrecta", no "token expirado".
- Cuando la respuesta llega, **el navegador ya guardó las cookies** del `Set-Cookie`. Solo falta avisar a React con `iniciarSesion(usuario)`.
- `navigate('/')` lleva a la ruta comodín, que redirige a `/home`. Como ahora sí hay usuario, `ProtectedRoute` deja pasar.

### `Dashboard.jsx`

```jsx
const { usuarioActual, cerrarSesion } = useAuth()

const response = await apiFetch('/api/User')
...
const response = await apiFetch(`/api/User/${accion}/${usuarioSeleccionado.id}`, { method: 'PATCH' })
...
<button type="button" onClick={cerrarSesion}>Cerrar sesión</button>
```

- Todas las peticiones a endpoints protegidos usan **`apiFetch`**. La regla: **si el endpoint tiene `[Authorize]`, se usa `apiFetch`**.
- `usuarioActual.isAdmin` decide si se muestran los botones de admin, y `usuarioActual.id` evita que un admin se quite el rol a sí mismo.
- El botón llama a `cerrarSesion`, que pone el usuario en `null`. Entonces `ProtectedRoute` manda a `/login`.

### `FormUser.jsx`

Sigue con `fetch` normal porque `POST /api/User` tiene `[AllowAnonymous]`: el registro no necesita sesión.

---

# Resumen de responsabilidades

| Archivo | Responsabilidad |
|---|---|
| `TokenService` | **Cómo** se crean y validan los tokens |
| `Program.cs` | Conecta el middleware JWT con `TokenService` y le dice que lea la cookie |
| `AuthController` | **Cuándo** se dan, rotan y revocan los tokens, y guarda las sesiones en la BD |
| `api.js` | Renueva el token sin que los componentes se enteren |
| `AuthContext` | Guarda **quién** está logueado para React |
| `ProtectedRoute` | Decide si dejar entrar a una página |
| Componentes | Usan `apiFetch` para endpoints protegidos y `useAuth()` para saber quién es el usuario |

## Bugs que se corrigieron en el camino

1. **No se podían crear usuarios (500):** `docker-compose.yml` no pasaba las variables `JWT_*` al contenedor, así que el secreto llegaba vacío.
2. **El registro pedía estar logueado:** `[Authorize]` estaba en todo `UserController`, así que se agregó `[AllowAnonymous]` a `PostUser`.
3. **`JWT_KEY` vs `JWT_SECRET`:** el token se firmaba y se validaba con variables distintas.
4. **El frontend no mandaba el token,** y `Login.jsx` leía mal la respuesta.
5. **La cookie de otro proyecto en `localhost`:** se arregló con el prefijo `authservice_` en los nombres.
6. **`PUT` y `DELETE` de usuarios no exigían ser admin:** cualquier usuario logueado podía editar o borrar a otros.

## Cosas a tener en cuenta

- **Cambio de rol:** si haces admin a alguien, su access token viejo sigue sin el claim `Admin` hasta que se renueva. Eso pasa como máximo 15 minutos después, o al volver a iniciar sesión.
- **`strict-origin-when-cross-origin` no es un error:** es la *Referrer Policy* que el navegador muestra en cada petición en la pestaña Red. El error real es el código de estado (401, 403, 500…).
- **Desarrollo con Vite (`http://localhost:5173`):** las cookies son `Secure`. Chrome y Firefox las aceptan en `localhost` aunque sea HTTP, pero si algún navegador las rechaza, usa `https://localhost` a través de nginx.
