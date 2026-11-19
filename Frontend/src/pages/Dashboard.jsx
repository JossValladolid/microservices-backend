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
  const [mostrarArchivosModal, setMostrarArchivosModal] = useState(false)
  const fileInputRef = useRef(null)
  const { uploadFile, uploading } = useUploadFiles()

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

  if (!usuarioActual) return null

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

  const subirArchivo = () => {
    fileInputRef.current?.click()
  }

  const manejarArchivoSeleccionado = async (e) => {
    const file = e.target.files?.[0]
    if (!file) return

    try {
      const homeResponse = await apiFetch('/api/folders/home')
      if (!homeResponse.ok) throw new Error('No se pudo obtener tu carpeta home')
      const home = await homeResponse.json()

      const result = await uploadFile(file, home.id)
      toast.success(`Archivo "${result.fileName}" subido a tu carpeta home`)
    } catch (error) {
      console.error('Error al subir el archivo:', error)
      toast.error(error.message || 'Error al subir el archivo')
    } finally {
      e.target.value = ''
    }
  }

  const verArchivos = () => {
    setMostrarArchivosModal(true)
  }

  return (
    <div>
      <Toaster position="top-center" />
      <header className="barra">
        <div className="usuario">
          <span className="avatar" aria-hidden="true">
            {usuarioActual.name?.charAt(0).toUpperCase()}
          </span>
          <p>inición sesiada como <strong>{usuarioActual.name}</strong></p>
        </div>
        <div className="acciones">         
            
          <input
            type="file"
            ref={fileInputRef}
            style={{ display: 'none' }}
            onChange={manejarArchivoSeleccionado}
          />
          <button type="button" className="btn-primario" onClick={subirArchivo} disabled={uploading}>
            {uploading ? 'Subiendo...' : 'Subir archivo'}
          </button>
          <button type="button" onClick={verArchivos}>Ver mis archivos</button>
        
          <button type="button" onClick={cerrarSesion}>Cerrar sesión</button>
        </div>
      </header>

      {esAdmin && (
        <section className="panel panel-form">
          <FormUser titulo="Registrar nuevo usuario" onUserCreated={manejarNuevoUsuario} />
        </section>
      )}

      {!esAdmin ? (
        <section className="panel">
          <p className="vacio">Imaginemos que aquí ya está el contenido para usuarios que no son admins xd.</p>
        </section>
      ) : (
        <section className="panel">
          <div className="panel-encabezado">
            <h2>Usuarios</h2>
            {!cargando && <span className="contador">{usuarios.length} en total</span>}
          </div>
          {cargando ? (
            <p className="vacio">Cargando usuarios...</p>
          ) : usuarios.length === 0 ? (
            <p className="vacio">Todavía no hay usuarios registrados.</p>
          ) : (
            <div className="tabla-scroll">
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
                    <tr key={user.id} className={user.isActive ? '' : 'pendiente'}>
                      <td className="id" title={user.id}>{user.id.slice(0, 8)}</td>
                      <td>{user.name}</td>
                      <td>{user.email}</td>
                      <td>{formatBirthDate(user.birthDate)}</td>
                      <td>{formatDate(user.createdAt)}</td>
                      <td>{formatDate(user.updatedAt)}</td>
                      <td>
                        <span className={`badge ${user.isAdmin ? 'badge-si' : 'badge-no'}`}>
                          {user.isAdmin ? 'Sí' : 'No'}
                        </span>
                      </td>
                      <td>
                        <span className={`badge ${user.isActive ? 'badge-si' : 'badge-alerta'}`}>
                          {user.isActive ? 'Sí' : 'No'}
                        </span>
                      </td>
                      <td>
                        {user.id !== usuarioActual.id && (
                          <div className="acciones-fila">
                            <button type="button" className="btn-sm" onClick={() => setUsuarioSeleccionado(user)}>
                              {user.isAdmin ? 'Quitar admin' : 'Hacer admin'}
                            </button>
                            <button
                              type="button"
                              className={user.isActive ? 'btn-sm' : 'btn-sm btn-primario'}
                              onClick={() => setUsuarioActivacion(user)}
                            >
                              {user.isActive ? 'Desactivar' : 'Activar'}
                            </button>
                          </div>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
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
