import { NavLink, Outlet } from 'react-router'
import { useAuth } from '../context/AuthContext.jsx'

export default function Layout() {
  const { usuarioActual, cerrarSesion } = useAuth()

  return (
    <div>
      <div className="barra">
        <p>
          Sesión iniciada como <strong>{usuarioActual.name}</strong>
          {' '}({usuarioActual.isAdmin ? 'administrador' : 'usuario'})
        </p>
        <nav className="acciones">
          <NavLink to="/archivos" className="nav-link">Archivos</NavLink>
          {usuarioActual.isAdmin && <NavLink to="/usuarios" className="nav-link">Usuarios</NavLink>}
          <button type="button" onClick={cerrarSesion}>Cerrar sesión</button>
        </nav>
      </div>
      <Outlet />
    </div>
  )
}
