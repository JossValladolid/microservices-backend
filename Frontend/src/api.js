import axios from 'axios'

// Los tokens viajan en cookies HttpOnly (no en localStorage): con withCredentials el navegador
// anexa automáticamente el Access Token a cada request a la API.
export const api = axios.create({
  baseURL: '/api',
  withCredentials: true
})

let alPerderSesion = () => {}
let refrescoEnCurso = null

export function alExpirarSesion(callback) {
  alPerderSesion = callback
}

// Rutas en las que un 401 no significa "access token expirado".
const SIN_REFRESCO = ['/Auth/login', '/Auth/refresh', '/Auth/logout']

// Si varios requests fallan a la vez, todos esperan el mismo refresh.
function refrescarTokens() {
  if (!refrescoEnCurso) {
    refrescoEnCurso = api.post('/Auth/refresh')
      .then(() => true)
      .catch(() => false)
      .finally(() => { refrescoEnCurso = null })
  }
  return refrescoEnCurso
}

// Interceptor: si el Access Token expiró (401), pide un nuevo par de tokens con el Refresh Token
// y reintenta una vez el request. Si el Refresh Token también expiró, se cierra la sesión (→ Login).
api.interceptors.response.use(
  response => response,
  async error => {
    const { config, response } = error
    const omitir = !config || config._reintentado || SIN_REFRESCO.some(ruta => config.url?.startsWith(ruta))

    if (response?.status !== 401 || omitir) {
      return Promise.reject(error)
    }

    config._reintentado = true
    if (await refrescarTokens()) {
      return api(config)
    }

    alPerderSesion()
    return Promise.reject(error)
  }
)

// Extrae el mensaje de error que manda el backend ({ message } o errores de validación).
export function mensajeError(error, porDefecto) {
  const data = error?.response?.data
  return data?.message || Object.values(data?.errors ?? {}).flat()[0] || error?.message || porDefecto
}
