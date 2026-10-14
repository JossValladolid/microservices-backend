// Fetch autenticado. Los tokens viajan en cookies HttpOnly que pone el backend:
// el navegador las manda solo y JavaScript nunca las ve.

let alPerderSesion = () => {}
let refrescoEnCurso = null

// AuthContext se registra aquí para enterarse cuando el refresh token ya no sirve
export function alExpirarSesion(callback) {
  alPerderSesion = callback
}

// Si varias peticiones fallan al mismo tiempo, todas esperan el mismo refresh
// (si no, la segunda usaría un refresh token que la primera ya rotó).
function refrescarTokens() {
  if (!refrescoEnCurso) {
    refrescoEnCurso = fetch('/api/Auth/refresh', { method: 'POST' })
      .then(response => response.ok)
      .catch(() => false)
      .finally(() => { refrescoEnCurso = null })
  }
  return refrescoEnCurso
}

// Igual que fetch, pero si el servidor responde 401 (access token expirado)
// renueva los tokens y reintenta una vez. Si no se puede renovar, la sesión terminó.
export async function apiFetch(url, opciones) {
  const response = await fetch(url, opciones)
  if (response.status !== 401) return response

  if (await refrescarTokens()) return fetch(url, opciones)

  alPerderSesion()
  return response
}
