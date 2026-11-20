import { createContext, useContext, useEffect, useState } from 'react'
import { api, alExpirarSesion } from '../api.js'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [usuarioActual, setUsuarioActual] = useState(null)
  const [verificando, setVerificando] = useState(true)

  useEffect(() => {
    alExpirarSesion(() => setUsuarioActual(null))

    api.get('/Auth/me')
      .then(response => setUsuarioActual(response.data))
      .catch(() => setUsuarioActual(null))
      .finally(() => setVerificando(false))
  }, [])

  // usuario: respuesta de /api/Auth/login (el backend ya dejó los tokens en cookies)
  const iniciarSesion = (usuario) => setUsuarioActual(usuario)

  const cerrarSesion = async () => {
    try {
      await api.post('/Auth/logout')
    } catch {
      // Aunque falle, la sesión local se cierra.
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
