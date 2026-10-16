import { useEffect, useRef, useState } from 'react'
import { Toaster, toast } from 'react-hot-toast'
import { useAuth } from '../context/AuthContext.jsx'
import FormUser from '../components/FormUser.jsx'
import ConfirmarAdminModal from '../components/ConfirmarAdminModal.jsx'
import ConfirmarActivedUserModal from '../components/ConfirmarActivedUserModal.jsx'
import ArchivosModal from '../components/ArchivosModal.jsx'
import { apiFetch } from '../api.js'
import { useUploadFiles } from '../hooks/uploadFiles.jsx'

const formatDate = (stringDate) => {
  if (!stringDate) return 'N/A';
  return new Date(stringDate).toLocaleDateString('es-MX', {
    year: 'numeric', month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit'
  });
};

const formatBirthDate = (stringDate) => {
  if (!stringDate) return 'N/A';
  const [year, month, day] = stringDate.split('-').map(Number);
  return new Date(year, month - 1, day).toLocaleDateString('es-MX', {
    year: 'numeric', month: 'short', day: 'numeric'
  });
};

export default function Dashboard() {
  const { usuarioActual, cerrarSesion } = useAuth()
  const [recargar, setRecargar] = useState(false)
  const [usuarios, setUsuarios] = useState([])
  const [cargando, setCargando] = useState(true)
  const [usuarioSeleccionado, setUsuarioSeleccionado] = useState(null)
  const [usuarioActivacion, setUsuarioActivacion] = useState(null)
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

  const confirmarCambioActivo = async () => {
    if (!usuarioActivacion) return
    setProcesando(true)

    const accion = usuarioActivacion.isActive ? 'Deactivate' : 'Activate'

    try {
      const response = await apiFetch(`/api/User/${accion}/${usuarioActivacion.id}`, {
        method: 'PATCH'
      })

      if (response.ok) {
        const actualizado = await response.json()
        setUsuarios(prev => prev.map(u =>
          u.id === actualizado.id ? { ...u, isActive: actualizado.isActive, updatedAt: actualizado.updatedAt } : u
        ))
        toast.success(
          actualizado.isActive
            ? `${actualizado.name} fue activado`
            : `${actualizado.name} fue desactivado`
        )
        setUsuarioActivacion(null)
      } else {
        toast.error('No se pudo cambiar el estado del usuario')
      }
    } catch (error) {
      console.error('Error al cambiar el estado activo:', error)
      toast.error('Ocurrió un error al actualizar el usuario')
    } finally {
      setProcesando(false)
    }
  }

  const fileInputRef = useRef(null)
  const { uploadFile, uploading } = useUploadFiles()

  const subirArchivo = () => {
    fileInputRef.current?.click()
  }

  const manejarArchivoSeleccionado = async (e) => {
    const file = e.target.files?.[0]
    if (!file) return

    try {
      const result = await uploadFile(file)
      toast.success(`Archivo "${result.fileName}" subido exitosamente`)
    } catch (error) {
      console.error('Error al subir el archivo:', error)
      toast.error(error.message || 'Error al subir el archivo')
    } finally {
      e.target.value = ''
    }
  }

  const [mostrarArchivosModal, setMostrarArchivosModal] = useState(false)

  const verArchivos = () => {
    setMostrarArchivosModal(true)
  }

  return (
    <div>
      <Toaster position="top-center" />
      <div className="barra">
        <p>inición sesiada como {usuarioActual.name}</p>
        <div className="acciones">
          <input
            type="file"
            ref={fileInputRef}
            style={{ display: 'none' }}
            onChange={manejarArchivoSeleccionado}
          />
          <button type="button" onClick={subirArchivo} disabled={uploading}>
            {uploading ? 'Subiendo...' : 'Subir archivo'}
          </button>
          <button type="button" onClick={verArchivos}>Ver mis archivos</button>
          <button type="button" onClick={cerrarSesion}>Cerrar sesión</button>
        </div>
      </div>
      {
        esAdmin && (
          <FormUser titulo="Registrar nuevo usuario" onUserCreated={manejarNuevoUsuario} />
        )
      }
        <hr />

      {!esAdmin ? (
        <p>Imaginemos que aquí ya está el contenido para usuarios que no son admins xd.</p>
      ) : (
        <div>
        <h2>Usuarios</h2>
        {cargando ? (
          <p>Cargando usuarios...</p>
        ) : (
          <table className="tabla">
            <thead>
              <tr>
                <th>ID</th>
                <th>Nombre</th>
                <th>Email</th>
                <th>Nacimiento</th>
                <th>Fecha de Creación</th>
                <th>Fecha de Actualización</th>
                <th>Admin</th>
                <th>Activo</th>
                <th>Acciones</th>
              </tr>
            </thead>

            <tbody>
              {usuarios.map(user => (
                <tr key={user.id}>
                  <td>{user.id}</td>
                  <td>{user.name}</td>
                  <td>{user.email}</td>
                  <td>{formatBirthDate(user.birthDate)}</td>
                  <td>{formatDate(user.createdAt)}</td>
                  <td>{formatDate(user.updatedAt)}</td>
                  <td>{user.isAdmin ? 'Sí' : 'No'}</td>
                  <td>{user.isActive ? 'Sí' : 'No'}</td>
                  <td>
                    {user.id !== usuarioActual.id && (
                      <>
                        <button type="button" onClick={() => setUsuarioSeleccionado(user)}>
                          {user.isAdmin ? 'Quitar admin' : 'Hacer admin'}
                        </button>
                        <button type="button" onClick={() => setUsuarioActivacion(user)}>
                          {user.isActive ? 'Desactivar' : 'Activar'}
                        </button>
                      </>
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
      <ArchivosModal
        abierto={mostrarArchivosModal}
        onCerrar={() => setMostrarArchivosModal(false)}
      />
      <ConfirmarActivedUserModal
        usuario={usuarioActivacion}
        procesando={procesando}
        onConfirmar={confirmarCambioActivo}
        onCancelar={() => setUsuarioActivacion(null)}
      />
    </div>
  )
}
