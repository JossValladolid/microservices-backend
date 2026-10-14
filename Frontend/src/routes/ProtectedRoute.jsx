import { Navigate, Outlet } from 'react-router'
import { useAuth } from '../context/AuthContext.jsx'

export default function ProtectedRoute() {
  const { usuarioActual, verificando } = useAuth()

  if (verificando) {
    return <p>Cargando...</p>
  }

  if (!usuarioActual) {
    return <Navigate to="/login" replace />
  }

  return <Outlet />
}
