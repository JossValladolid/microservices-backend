import { Navigate, Outlet } from 'react-router'
import { useAuth } from '../context/AuthContext.jsx'

// Se usa dentro de ProtectedRoute: el usuario ya está autenticado.
export default function AdminRoute() {
  const { usuarioActual } = useAuth()

  if (!usuarioActual?.isAdmin) {
    return <Navigate to="/archivos" replace />
  }

  return <Outlet />
}
