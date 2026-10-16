let alPerderSesion = () => {}
let refrescoEnCurso = null

export function alExpirarSesion(callback) {
  alPerderSesion = callback
}

function refrescarTokens() {
  if (!refrescoEnCurso) {
    refrescoEnCurso = fetch('/api/Auth/refresh', { method: 'POST' })
      .then(response => response.ok)
      .catch(() => false)
      .finally(() => { refrescoEnCurso = null })
  }
  return refrescoEnCurso
}

export async function apiFetch(url, opciones) {
  const response = await fetch(url, opciones)
  if (response.status !== 401) return response

  if (await refrescarTokens()) return fetch(url, opciones)

  alPerderSesion()
  return response
}
