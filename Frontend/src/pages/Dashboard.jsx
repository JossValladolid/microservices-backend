import { useEffect, useState } from 'react'
import { Toaster, toast } from 'react-hot-toast'
import { useAuth } from '../context/AuthContext.jsx'
import FormUser from '../components/FormUser.jsx'
import ConfirmarAdminModal from '../components/ConfirmarAdminModal.jsx'
import { apiFetch } from '../api.js'

const formatDate = (stringDate) => {
  if (!stringDate) return 'N/A';
  return new Date(stringDate).toLocaleDateString('es-MX', {
    year: 'numeric', month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit'
  });
};

export default function Dashboard() {
  const { usuarioActual, cerrarSesion } = useAuth()
  const [recargar, setRecargar] = useState(false)
  const [usuarios, setUsuarios] = useState([])
  const [cargando, setCargando] = useState(true)
  const [usuarioSeleccionado, setUsuarioSeleccionado] = useState(null)
  const [procesando, setProcesando] = useState(false)

  const esAdmin = usuarioActual?.isAdmin

  useEffect(() => {
    if (!esAdmin) return

    const cargarUsuarios = async () => {
      try {
        const response = await apiFetch('/api/User')
        if (response.ok) {
          setUsuarios(await response.json())
        } else if (response.status === 404) {
          setUsuarios([])
        } else {
          toast.error('No se pudieron cargar los usuarios')
        }
      } catch (error) {
        console.error('Error al obtener los usuarios:', error)
      } finally {
        setCargando(false)
      }
    }
    cargarUsuarios()
  }, [recargar, esAdmin])

  const manejarNuevoUsuario = () => {
    setRecargar(!recargar)
  }

  const confirmarCambioAdmin = async () => {
    if (!usuarioSeleccionado) return
    setProcesando(true)

    const accion = usuarioSeleccionado.isAdmin ? 'RevokeAdmin' : 'AddAdmin'

    try {
      const response = await apiFetch(`/api/User/${accion}/${usuarioSeleccionado.id}`, {
        method: 'PATCH'
      })

      if (response.ok) {
        const actualizado = await response.json()
        setUsuarios(prev => prev.map(u =>
          u.id === actualizado.id ? { ...u, isAdmin: actualizado.isAdmin, updatedAt: actualizado.updatedAt } : u
        ))
        toast.success(
          actualizado.isAdmin
            ? `${actualizado.name} ahora es administrador`
            : `Se le quitó el rol de administrador a ${actualizado.name}`
        )
        setUsuarioSeleccionado(null)
      } else {
        toast.error('No se pudo actualizar el rol del usuario')
      }
    } catch (error) {
      console.error('Error al cambiar el rol de admin:', error)
      toast.error('Ocurrió un error al actualizar el usuario')
    } finally {
      setProcesando(false)
    }
  }

  

  return (
    <div>
      <Toaster position="top-center" />
      <p>
        Sesión iniciada como {usuarioActual.name}
        <button type="button" onClick={cerrarSesion}>Cerrar sesión</button>
      </p>
      {esAdmin && (
        <FormUser titulo="Registrar nuevo usuario" onUserCreated={manejarNuevoUsuario} />
      )}
        <hr />

      {!esAdmin ? (
        <p>Imaginemos que aquí ya está el contenido para usuarios que no son admins xd.</p>
      ) : (
        <div>
        <h2>Usuarios</h2>
        {cargando ? (
          <p>Cargando usuarios...</p>
        ) : (
          <table border="1" cellPadding="10">
            <thead>
              <tr>
                <th>ID</th>
                <th>Nombre</th>
                <th>Email</th>
                <th>Fecha de Creación</th>
                <th>Fecha de Actualización</th>
                <th>Admin</th>
                <th>Acciones</th>
              </tr>
            </thead>

            <tbody>
              {usuarios.map(user => (
                <tr key={user.id}>
                  <td>{user.id}</td>
                  <td>{user.name}</td>
                  <td>{user.email}</td>
                  <td>{formatDate(user.createdAt)}</td>
                  <td>{formatDate(user.updatedAt)}</td>
                  <td>{user.isAdmin ? 'Sí' : 'No'}</td>
                  <td>
                    {user.id !== usuarioActual.id && (
                      <button type="button" onClick={() => setUsuarioSeleccionado(user)}>
                        {user.isAdmin ? 'Quitar admin' : 'Hacer admin'}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        </div>
      )}
      <ConfirmarAdminModal
        usuario={usuarioSeleccionado}
        procesando={procesando}
        onConfirmar={confirmarCambioAdmin}
        onCancelar={() => setUsuarioSeleccionado(null)}
      />
    </div>
  )
}
