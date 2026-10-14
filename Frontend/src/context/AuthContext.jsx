import { createContext, useContext, useEffect, useState } from 'react'
import { apiFetch, alExpirarSesion } from '../api.js'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [usuarioActual, setUsuarioActual] = useState(null)
  // Mientras preguntamos al backend si hay sesión, no sabemos si redirigir a /login
  const [verificando, setVerificando] = useState(true)

  useEffect(() => {
    alExpirarSesion(() => setUsuarioActual(null))

    // Las cookies son HttpOnly, así que al recargar la página le preguntamos al backend quién somos
    apiFetch('/api/Auth/me')
      .then(response => response.ok ? response.json() : null)
      .then(usuario => setUsuarioActual(usuario))
      .catch(() => setUsuarioActual(null))
      .finally(() => setVerificando(false))
  }, [])

  // usuario: respuesta de /api/Auth/login (el backend ya dejó los tokens en cookies)
  const iniciarSesion = (usuario) => setUsuarioActual(usuario)

  const cerrarSesion = async () => {
    try {
      await fetch('/api/Auth/logout', { method: 'POST' })
    } finally {
      setUsuarioActual(null)
    }
  }

  return (
    <AuthContext.Provider value={{ usuarioActual, verificando, iniciarSesion, cerrarSesion }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth debe usarse dentro de un AuthProvider')
  }
  return context
}
