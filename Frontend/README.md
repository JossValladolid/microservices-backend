# Frontend — Dashboard de Usuarios (React + Vite)

Frontend del microservicio de autenticación. Consume la API de `AuthService` (`/api/Auth/login`, `/api/User`) a través del proxy configurado en `vite.config.js` (`/api` → `http://localhost:5098`, que es el puerto donde corre `authservice` en Docker).

Este README documenta cómo está armado `src/App.jsx` para no perder de vista la lógica cuando pase tiempo sin tocarlo. Está pensado como apunte de aprendizaje de React, no solo como documentación del proyecto.

## Cómo correrlo

```bash
npm install
npm run dev      # levanta Vite en http://localhost:5173
npm run build    # build de producción a dist/
npm run lint     # eslint
```

Requiere que `authservice` esté corriendo en el puerto `5098` (ver `docker-compose.yml` en la raíz del repo) para que el login y las peticiones a `/api/*` funcionen.

## Mapa del archivo `src/App.jsx`

Todo vive en un solo archivo, con 4 componentes:

```
DashboardUsuarios   ← componente raíz, dueño de todo el estado importante
 ├── Login          ← formulario de inicio de sesión
 ├── FormUser       ← formulario para crear un usuario (se reutiliza en 2 contextos)
 └── App            ← tabla que lista los usuarios (nombre confuso: no es el "App" raíz)
      └── ConfirmarAdminModal   ← modal de confirmación para dar/quitar admin
```

`main.jsx` solo monta `<DashboardUsuarios />` en el DOM — es el punto de entrada estándar de Vite/React, no tiene lógica propia.

---

## Conceptos de React usados aquí

### 1. Componente = función que regresa JSX

Cada componente (`Login`, `FormUser`, `App`, `DashboardUsuarios`) es una función normal de JS. React la llama, y lo que regresa (`return (...)`) es lo que se pinta en pantalla. Cuando el estado interno cambia, React vuelve a llamar la función (**re-render**) para recalcular qué mostrar.

### 2. Props = parámetros de la función del componente

Cuando escribes en JSX:

```jsx
<Login onLoginSuccess={setUsuarioActual} onSwitchToRegister={() => setVista('registro')} />
```

React arma un objeto `{ onLoginSuccess: setUsuarioActual, onSwitchToRegister: () => setVista('registro') }` y se lo pasa a la función `Login` como su único argumento. Por eso la firma del componente lo "desempaca" con destructuring:

```jsx
function Login({ onLoginSuccess, onSwitchToRegister }) {
```

Es lo mismo que hacer `function saluda({ nombre, apellido })` con cualquier objeto JS — no hay nada mágico de React ahí, es destructuring de JS puro. El nombre de la prop (`onLoginSuccess`) es solo una etiqueta que tú inventas; no está "conectado" automáticamente a nada, solo coincide porque lo escribiste igual en ambos lados (quien la pasa y quien la recibe).

**Importante:** una misma variable/prop se puede usar varias veces dentro del cuerpo de la función, sin que eso signifique que "se repite" o se vuelve a crear. Ejemplo en `App`:

```jsx
function App({actualizarUsuarios}) {     // 1) nace aquí como parámetro
  useEffect(() => {
    fetch('/api/User')...
  }, [actualizarUsuarios])                // 2) mismo valor, solo se vuelve a leer
}
```

Y en `FormUser`, el patrón de "verificar antes de llamar":

```js
if (onUserCreated) {   // ¿la variable tiene una función adentro? (sin paréntesis = valor)
  onUserCreated()       // ejecútala (con paréntesis = invocación)
}
```

Es la misma variable, primero evaluada como valor (¿existe?), después invocada como función. Equivalente moderno más corto: `onUserCreated?.()`.

### 3. `useState` — estado local

```js
const [valor, setValor] = useState(inicial)
```

`valor` es de solo lectura fuera de un `setValor(...)`. Cada `setValor(nuevo)` le dice a React "guarda este nuevo valor y vuelve a renderizar el componente". React no compara si el valor es "mejor" o "distinto" para decidir si renderiza — solo con dependencias de `useEffect` sí importa si cambió (ver abajo).

### 4. `useEffect` — efectos secundarios (fetch, timers, etc.)

```js
useEffect(funcionEfecto, arrayDeDependencias)
```

- **Sin array** → corre después de *cada* render.
- **Array vacío `[]`** → corre *una sola vez*, al montar el componente.
- **Array con valores `[a, b]`** → corre al montar, y de nuevo cada vez que `a` o `b` tengan un valor *distinto* al que tenían en el render anterior.

La primera ejecución (al montar) siempre pasa, sin importar qué valor traigan las dependencias en ese momento — el array solo decide si se **repite** en renders futuros, comparando contra el valor anterior.

Ejemplo real, en `App` (la tabla):

```jsx
useEffect(() => {
  fetch('/api/User')
    .then(r => r.json())
    .then(data => {
      setUsuarios(data)
      setCargando(false)
    })
}, [actualizarUsuarios])
```

- Al montar `App`: corre sí o sí, trae la tabla inicial.
- Cada vez que la prop `actualizarUsuarios` (que viene de `recargar` en el padre) cambia de valor, se vuelve a ejecutar → refresca la tabla.

`recargar` empieza en `false` (`useState(false)`). Eso no afecta el primer fetch (siempre ocurre al montar). Lo que importa es que cada vez que un admin crea un usuario, se hace `setRecargar(!recargar)` — el valor se invierte (`false→true`, `true→false`), y como siempre es *distinto* al anterior, siempre dispara el efecto de nuevo. Si en vez de alternar se pusiera siempre el mismo valor, el efecto no se repetiría.

### 5. Renderizado condicional

```jsx
{onCancelar && (<p>...</p>)}
```

Si `onCancelar` es "truthy" (existe, no es `undefined`/`false`/`null`), se renderiza lo que sigue; si no, no se renderiza nada. Se usa para mostrar/ocultar partes de la UI según props opcionales, sin necesitar un `if` completo.

```jsx
if (!usuarioActual) { ... }
```

Los `if` normales dentro del cuerpo del componente (antes del `return` final) sirven para decidir *qué JSX completo* regresar, como una máquina de estados.

### 6. "Levantar el estado" (lifting state up)

`Login`, `FormUser` y `App` no saben nada del flujo general de la aplicación — solo saben hacer su tarea (mostrar un formulario, mostrar una tabla) y avisarle a quien los usa cuando algo pasó, llamando a una función que les llega como prop. El estado importante (`usuarioActual`, `vista`, `recargar`) vive únicamente en `DashboardUsuarios`, el ancestro común. Esto es un patrón estándar en React: los hijos son "tontos" (reciben datos/funciones, no deciden el flujo), el padre es quien coordina.

### 7. Actualizar estado sin mutarlo (arrays y objetos)

Cuando el usuario admin confirma el cambio de rol, la tabla se actualiza así (dentro de `App`):

```jsx
setUsuarios(prev => prev.map(u =>
  u.id === actualizado.id ? { ...u, isAdmin: actualizado.isAdmin, updatedAt: actualizado.updatedAt } : u
))
```

Dos ideas nuevas aquí:

- **`setAlgo(prev => ...)`**: en vez de pasarle el valor nuevo directo, le pasas una *función*. React te da el valor **actual** del estado como parámetro (`prev`), útil cuando el valor nuevo depende del anterior.
- **`.map()` + spread (`...`)**: `usuarios` es un array, nunca se debe modificar directamente (`usuarios[i].isAdmin = true` está prohibido en React — mutaría el estado sin que React se entere). En su lugar, `.map()` recorre cada elemento y regresa un array **nuevo**: para el usuario que coincide por `id`, regresa una copia con `{ ...u, isAdmin: ..., updatedAt: ... }` (el spread copia todas las propiedades viejas de `u`, y luego las dos que siguen sobrescriben solo esas); para todos los demás, regresa el mismo `u` sin tocar.

### 8. Batching: varios `setEstado` juntos = un solo re-render

```jsx
setUsuarios(prev => prev.map(...))   
toast.success(...)
setUsuarioSeleccionado(null)          
// ...en el finally:
setProcesando(false)                  
```

Estas 3 llamadas a `set...` ocurren de forma síncrona, sin ningún `await` entre ellas. React (desde la v18) agrupa automáticamente todas las llamadas a setters que pasan "juntas" (sin una espera asíncrona de por medio) en **un solo re-render**, en vez de repintar la pantalla 3 veces. Por eso, cuando confirmas el cambio de admin, la tabla se actualiza, el modal se cierra y el botón se reactiva todo en el mismo instante visual.

En cambio, el `setProcesando(true)` que está *antes* del primer `await fetch(...)` sí dispara su propio re-render aislado — por eso alcanzas a ver el botón decir "Guardando..." mientras la petición sigue en curso, antes de que lleguen los otros cambios.

### 9. Propagación de eventos (`stopPropagation`)

```jsx
<div className="modal-overlay" onClick={onCancelar}>
  <div className="modal" onClick={(e) => e.stopPropagation()}>
```

Un clic en el navegador "burbujea": se dispara primero en el elemento exacto donde clicaste, y luego sigue disparándose hacia arriba en cada elemento padre. Aquí, un clic en el fondo oscuro (`modal-overlay`) debe cerrar el modal, pero un clic *dentro* de la caja del modal también burbujearía hasta el overlay y lo cerraría sin querer. `e.stopPropagation()` en la caja interna corta esa burbuja ahí mismo: el clic no sigue subiendo, así que el overlay nunca se entera y no se cierra.

---

## Componente por componente

### `Login({ onLoginSuccess, onSwitchToRegister })`

Formulario de inicio de sesión, con estado propio (`email`, `password`, `cargando`) que nadie más ve.

`handleSubmit`:
1. `POST /api/Auth/login` con `{ email, password }`.
2. Si responde OK → `onLoginSuccess(user)` (esto en realidad ejecuta `setUsuarioActual(user)` en el padre, cambiando el estado global de sesión).
3. Si falla → toast de error.
4. Tiene un link "¿No tienes cuenta? Regístrate" que llama a `onSwitchToRegister()`.

### `FormUser({ onUserCreated, onCancelar, titulo = 'Crear Usuario' })`

Formulario para crear un usuario. Se reutiliza en **dos contextos distintos**, pasando props diferentes cada vez:

| Contexto | `titulo` | `onUserCreated` hace | `onCancelar` |
|---|---|---|---|
| Registro público (sin sesión) | "Crear cuenta" | `setVista('login')` — vuelve al login | `setVista('login')` — sí se pasa, aparece el link "¿Ya tienes cuenta?" |
| Admin, dentro del dashboard | "Registrar nuevo usuario" | `manejarNuevoUsuario` — refresca la tabla | no se pasa (`undefined`) — el link no aparece |

`handleSubmit`: valida que las contraseñas coincidan (solo UX, la validación real está en el backend), hace `POST /api/User`, y si sale bien limpia el formulario y llama `onUserCreated?.()`.

### `App({ actualizarUsuarios, usuarioActual })` — la tabla

**Ojo:** se llama `App` por el nombre por defecto de la plantilla de Vite, pero *no* es el componente raíz de la aplicación — es solo la tabla de usuarios. El componente raíz real es `DashboardUsuarios`.

`useEffect` trae `GET /api/User` al montar, y cada vez que `actualizarUsuarios` cambia de valor (ver sección de `useEffect` arriba). Mientras `cargando` es `true`, regresa solo un `<p>Cargando usuarios...</p>` (early return).

**Columna de acciones (dar/quitar admin):** solo aparece si `usuarioActual?.isAdmin` es verdadero. Por cada fila, un botón dice "Hacer admin" o "Quitar admin" según `user.isAdmin` de esa fila — y no aparece en la fila del propio usuario logueado (`user.id !== usuarioActual.id`), para no poder quitarte tu propio admin desde la tabla por accidente (eso dejaría desincronizado el `usuarioActual.isAdmin` que vive en `DashboardUsuarios`, ya que ese estado no se actualiza solo con lo que pasa acá adentro).

El botón guarda el usuario clicado en el estado `usuarioSeleccionado`, lo que abre `<ConfirmarAdminModal>`. Al confirmar, `confirmarCambioAdmin`:
1. Decide el endpoint según el estado actual: `PATCH /api/User/AddAdmin/{id}` o `PATCH /api/User/RevokeAdmin/{id}`.
2. Si responde OK, actualiza solo esa fila en el array `usuarios` (ver sección 7 arriba) — no vuelve a pedir toda la lista.
3. Cierra el modal y muestra un toast.

### `ConfirmarAdminModal({ usuario, procesando, onConfirmar, onCancelar })`

Modal de confirmación reutilizado tanto para dar como para quitar admin (el texto cambia solo, según `!usuario.isAdmin`). Si `usuario` es `null`, el componente regresa `null` y no se pinta nada — así es como `App` lo mantiene "siempre montado" en el JSX pero invisible cuando no hay ninguna acción pendiente. Ver la sección 9 arriba para cómo evita cerrarse solo cuando haces clic dentro de la caja.

### `DashboardUsuarios()` — el componente raíz, donde se junta todo

Dueño de 3 variables de estado:

- `usuarioActual` — `null` si nadie inició sesión, o el objeto `user` devuelto por el login.
- `vista` — `'login'` o `'registro'` (solo importa cuando `usuarioActual` es `null`).
- `recargar` — booleano que solo se usa como "señal de cambio" para el `useEffect` de `App`.

Lógica de renderizado, como máquina de estados:

```
usuarioActual == null?
 ├─ sí, vista == 'registro' → <FormUser titulo="Crear cuenta" onCancelar={...} .../>
 └─ sí, vista == 'login'    → <Login onSwitchToRegister={...} .../>
usuarioActual != null:
 → { usuarioActual.isAdmin && <FormUser titulo="Registrar nuevo usuario" .../> }
 → <App actualizarUsuarios={recargar} usuarioActual={usuarioActual} />   (siempre se muestra)
```

Flujo completo de ejemplo (clic por clic):

1. Carga inicial: `usuarioActual = null`, `vista = 'login'` → se ve `<Login>`.
2. Clic en "Regístrate" → `onSwitchToRegister()` → `setVista('registro')` → re-render → ahora se ve `<FormUser>` (registro público).
3. Clic en "Iniciar sesión" (dentro de ese FormUser) → `onCancelar()` → `setVista('login')` → vuelve a `<Login>`.
4. Login exitoso → `onLoginSuccess(user)` → `setUsuarioActual(user)` → re-render → ya no es `null` → entra al bloque del dashboard.
5. Si `usuarioActual.isAdmin` es `true`, aparece también el `<FormUser>` de "Registrar nuevo usuario" arriba de la tabla.
6. Admin crea un usuario → `onUserCreated()` → `manejarNuevoUsuario()` → `setRecargar(!recargar)` → `<App>` recibe una prop distinta → su `useEffect` nota el cambio → vuelve a pedir `GET /api/User` → la tabla se actualiza sola.
7. Admin hace clic en "Hacer admin" de otra fila → `setUsuarioSeleccionado(user)` → se abre `<ConfirmarAdminModal>` → clic en "Confirmar" → `confirmarCambioAdmin` hace el `PATCH` correspondiente → actualiza esa fila en `usuarios`, cierra el modal y muestra un toast (ver sección "Batching" arriba para el orden exacto de los re-renders).

---

## Cosas pendientes / a mejorar (notas para después)

- No hay persistencia de sesión (si recargas la página, `usuarioActual` vuelve a `null`) — hoy vive solo en memoria de React.
- No hay manejo de roles más allá de `isAdmin` en el formulario de registro del dashboard (cualquier campo `isAdmin` real se define en el backend, no aquí).
