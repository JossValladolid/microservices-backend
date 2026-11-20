import { useEffect, useState } from 'react'
import { toast } from 'react-hot-toast'
import { api, mensajeError } from '../api.js'
import { formatDate, formatBirthDate, sinVacios } from '../utils/formato.js'
import { useAuth } from '../context/AuthContext.jsx'
import FormUser from '../components/FormUser.jsx'
import Paginacion from '../components/Paginacion.jsx'
import EditarUsuarioModal from '../components/EditarUsuarioModal.jsx'
import ConfirmarAdminModal from '../components/ConfirmarAdminModal.jsx'
import ConfirmarActivedUserModal from '../components/ConfirmarActivedUserModal.jsx'

const FILTROS_VACIOS = { name: '', email: '', birthDateFrom: '', birthDateTo: '', isAdmin: '', hasRefreshToken: '' }

export default function Usuarios() {
  const { usuarioActual } = useAuth()

  const [filtros, setFiltros] = useState(FILTROS_VACIOS)
  const [borrador, setBorrador] = useState(FILTROS_VACIOS)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [version, setVersion] = useState(0)
  const [datos, setDatos] = useState({ clave: null, items: [], total: 0 })

  const [mostrarFormulario, setMostrarFormulario] = useState(false)
  const [usuarioEditar, setUsuarioEditar] = useState(null)
  const [usuarioRol, setUsuarioRol] = useState(null)
  const [usuarioActivacion, setUsuarioActivacion] = useState(null)
  const [procesando, setProcesando] = useState(false)

  const clave = JSON.stringify({ filtros, page, pageSize, version })
  const cargando = datos.clave !== clave

  useEffect(() => {
    let vigente = true
    api.get('/User', { params: sinVacios({ page, pageSize, ...filtros }) })
      .then(({ data }) => {
        if (!vigente) return
        if (data.items.length === 0 && page > 1 && data.total > 0) {
          setPage(Math.ceil(data.total / pageSize))
          return
        }
        setDatos({ clave, items: data.items, total: data.total })
      })
      .catch(err => {
        if (!vigente) return
        toast.error(mensajeError(err, 'No se pudieron cargar los usuarios'))
        setDatos({ clave, items: [], total: 0 })
      })
    return () => { vigente = false }
  }, [clave, filtros, page, pageSize])

  const recargar = () => setVersion(v => v + 1)

  const cambiarBorrador = (campo) => (e) => setBorrador(b => ({ ...b, [campo]: e.target.value }))

  const aplicarFiltros = (e) => {
    e.preventDefault()
    setFiltros(borrador)
    setPage(1)
  }

  const limpiarFiltros = () => {
    setBorrador(FILTROS_VACIOS)
    setFiltros(FILTROS_VACIOS)
    setPage(1)
  }

  const ejecutar = async (peticion, exito, cerrar) => {
    setProcesando(true)
    try {
      await peticion()
      toast.success(exito)
      cerrar?.()
      recargar()
    } catch (err) {
      toast.error(mensajeError(err, 'No se pudo completar la acción'))
    } finally {
      setProcesando(false)
    }
  }

  const confirmarCambioRol = () => {
    const u = usuarioRol
    const accion = u.isAdmin ? 'RevokeAdmin' : 'AddAdmin'
    return ejecutar(
      () => api.patch(`/User/${accion}/${u.id}`),
      u.isAdmin ? `Se le quitó el rol de administrador a ${u.name}` : `${u.name} ahora es administrador`,
      () => setUsuarioRol(null)
    )
  }

  const confirmarCambioActivo = () => {
    const u = usuarioActivacion
    const accion = u.isActive ? 'Deactivate' : 'Activate'
    return ejecutar(
      () => api.patch(`/User/${accion}/${u.id}`),
      u.isActive ? `${u.name} fue desactivado y se revocó su Refresh Token` : `${u.name} fue activado`,
      () => setUsuarioActivacion(null)
    )
  }

  const revocarToken = (u) => {
    if (!window.confirm(`¿Revocar el Refresh Token de ${u.name}? Tendrá que iniciar sesión de nuevo.`)) return
    ejecutar(() => api.delete(`/User/${u.id}/refresh-token`), `Se revocó el Refresh Token de ${u.name}`)
  }

  return (
    <div>
      <div className="barra">
        <h2 className="titulo">Usuarios</h2>
        <button type="button" onClick={() => setMostrarFormulario(m => !m)}>
          {mostrarFormulario ? 'Ocultar formulario' : 'Nuevo usuario'}
        </button>
      </div>

      {mostrarFormulario && (
        <>
          <FormUser titulo="Registrar nuevo usuario" permitirRol onUserCreated={recargar} />
          <hr />
        </>
      )}

      <form className="filtros" onSubmit={aplicarFiltros}>
        <label>Nombre<input value={borrador.name} onChange={cambiarBorrador('name')} /></label>
        <label>Correo<input value={borrador.email} onChange={cambiarBorrador('email')} /></label>
        <label>Nacido desde<input type="date" value={borrador.birthDateFrom} onChange={cambiarBorrador('birthDateFrom')} /></label>
        <label>Nacido hasta<input type="date" value={borrador.birthDateTo} onChange={cambiarBorrador('birthDateTo')} /></label>
        <label>
          Rol
          <select value={borrador.isAdmin} onChange={cambiarBorrador('isAdmin')}>
            <option value="">Todos</option>
            <option value="true">Administrador</option>
            <option value="false">Usuario común</option>
          </select>
        </label>
        <label>
          Refresh Token
          <select value={borrador.hasRefreshToken} onChange={cambiarBorrador('hasRefreshToken')}>
            <option value="">Todos</option>
            <option value="true">Con token</option>
            <option value="false">Sin token</option>
          </select>
        </label>
        <div className="acciones">
          <button type="submit">Filtrar</button>
          <button type="button" onClick={limpiarFiltros}>Limpiar</button>
        </div>
      </form>

      {cargando ? (
        <p>Cargando usuarios...</p>
      ) : datos.items.length === 0 ? (
        <p>No se encontraron usuarios.</p>
      ) : (
        <table className="tabla">
          <thead>
            <tr>
              <th>Nombre</th>
              <th>Usuario</th>
              <th>Email</th>
              <th>Nacimiento</th>
              <th>Creación</th>
              <th>Actualización</th>
              <th>Admin</th>
              <th>Activo</th>
              <th>Refresh Token</th>
              <th>Acciones</th>
            </tr>
          </thead>
          <tbody>
            {datos.items.map(user => {
              const esPropio = user.id === usuarioActual.id
              return (
                <tr key={user.id}>
                  <td>{user.name}</td>
                  <td>{user.userName}</td>
                  <td>{user.email}</td>
                  <td>{formatBirthDate(user.birthDate)}</td>
                  <td>{formatDate(user.createdAt)}</td>
                  <td>{formatDate(user.updatedAt)}</td>
                  <td>{user.isAdmin ? 'Sí' : 'No'}</td>
                  <td>{user.isActive ? 'Sí' : 'No'}</td>
                  <td>{user.hasRefreshToken ? 'Asignado' : 'Sin token'}</td>
                  <td>
                    <div className="acciones">
                      <button type="button" onClick={() => setUsuarioEditar(user)}>Editar</button>
                      {!esPropio && (
                        <>
                          <button type="button" onClick={() => setUsuarioRol(user)}>
                            {user.isAdmin ? 'Quitar admin' : 'Hacer admin'}
                          </button>
                          <button type="button" onClick={() => setUsuarioActivacion(user)}>
                            {user.isActive ? 'Desactivar' : 'Activar'}
                          </button>
                          {user.hasRefreshToken && (
                            <button type="button" className="peligro" onClick={() => revocarToken(user)} disabled={procesando}>
                              Revocar token
                            </button>
                          )}
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}

      <Paginacion
        page={page}
        pageSize={pageSize}
        total={datos.total}
        onPage={setPage}
        onPageSize={(n) => { setPageSize(n); setPage(1) }}
      />

      {usuarioEditar && (
        <EditarUsuarioModal
          key={usuarioEditar.id}
          usuario={usuarioEditar}
          esPropio={usuarioEditar.id === usuarioActual.id}
          onGuardado={() => { setUsuarioEditar(null); recargar() }}
          onCancelar={() => setUsuarioEditar(null)}
        />
      )}
      <ConfirmarAdminModal
        usuario={usuarioRol}
        procesando={procesando}
        onConfirmar={confirmarCambioRol}
        onCancelar={() => setUsuarioRol(null)}
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
