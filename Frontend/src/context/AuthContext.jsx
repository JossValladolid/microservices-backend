import { createContext, useContext, useEffect, useState } from 'react'
import { apiFetch, alExpirarSesion } from '../api.js'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [usuarioActual, setUsuarioActual] = useState(null)
  const [verificando, setVerificando] = useState(true)

  useEffect(() => {
    alExpirarSesion(() => setUsuarioActual(null))

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
