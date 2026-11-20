import { useState } from 'react'
import { toast } from 'react-hot-toast'
import { api, mensajeError } from '../api.js'

// Se monta con key={usuario.id} para que el formulario se reinicie al cambiar de usuario.
export default function EditarUsuarioModal({ usuario, esPropio, onGuardado, onCancelar }) {
  const [form, setForm] = useState({
    name: usuario.name ?? '',
    userName: usuario.userName ?? '',
    email: usuario.email ?? '',
    birthDate: usuario.birthDate ?? '',
    isAdmin: !!usuario.isAdmin,
    password: '',
    passwordConfirm: ''
  })
  const [guardando, setGuardando] = useState(false)

  const cambiar = (campo) => (e) => {
    const valor = e.target.value
    setForm(f => ({ ...f, [campo]: campo === 'userName' ? valor.toLowerCase() : valor }))
  }

  // Un administrador no puede quitarse su propio rol.
  const rolBloqueado = esPropio && usuario.isAdmin

  const guardar = async (e) => {
    e.preventDefault()

    if (form.password && form.password !== form.passwordConfirm) {
      toast.error('Las contraseñas no coinciden')
      return
    }

    setGuardando(true)
    try {
      const payload = { ...form }
      if (!payload.password) {
        delete payload.password
        delete payload.passwordConfirm
      }
      const { data } = await api.put(`/User/${usuario.id}`, payload)
      toast.success(form.password
        ? 'Usuario actualizado. Se cambió la contraseña y se revocó su Refresh Token.'
        : 'Usuario actualizado')
      onGuardado(data)
    } catch (err) {
      toast.error(mensajeError(err, 'No se pudo actualizar el usuario'))
    } finally {
      setGuardando(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={onCancelar}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <form onSubmit={guardar}>
          <h3>Editar usuario</h3>
          <div>
            <label htmlFor="editName">Nombre:</label>
            <input id="editName" value={form.name} onChange={cambiar('name')} required />
          </div>
          <div>
            <label htmlFor="editUserName">Nombre de usuario:</label>
            <input
              id="editUserName"
              value={form.userName}
              onChange={cambiar('userName')}
              pattern="[a-z0-9_\-]{3,30}"
              title="De 3 a 30 caracteres: letras minúsculas, números, - y _"
              required
            />
          </div>
          <div>
            <label htmlFor="editEmail">Correo electrónico:</label>
            <input id="editEmail" type="email" value={form.email} onChange={cambiar('email')} required />
          </div>
          <div>
            <label htmlFor="editBirthDate">Fecha de nacimiento:</label>
            <input
              id="editBirthDate"
              type="date"
              value={form.birthDate}
              max={new Date().toLocaleDateString('en-CA')}
              onChange={cambiar('birthDate')}
              required
            />
          </div>
          <div>
            <label htmlFor="editRol">Rol:</label>
            <select
              id="editRol"
              value={form.isAdmin ? 'admin' : 'comun'}
              onChange={(e) => setForm(f => ({ ...f, isAdmin: e.target.value === 'admin' }))}
              disabled={rolBloqueado}
              title={rolBloqueado ? 'No puedes quitarte tu propio rol de administrador' : undefined}
            >
              <option value="comun">Usuario común</option>
              <option value="admin">Administrador</option>
            </select>
          </div>
          <p className="nota">Deja la contraseña vacía para no cambiarla. Si la cambias, se revoca su Refresh Token.</p>
          <div>
            <label htmlFor="editPassword">Nueva contraseña:</label>
            <input id="editPassword" type="password" value={form.password} onChange={cambiar('password')} autoComplete="new-password" />
          </div>
          <div>
            <label htmlFor="editPasswordConfirm">Confirmar nueva contraseña:</label>
            <input
              id="editPasswordConfirm"
              type="password"
              value={form.passwordConfirm}
              onChange={cambiar('passwordConfirm')}
              required={!!form.password}
              autoComplete="new-password"
            />
          </div>
          <div className="modal-actions">
            <button type="button" onClick={onCancelar} disabled={guardando}>Cancelar</button>
            <button type="submit" disabled={guardando}>{guardando ? 'Guardando...' : 'Guardar'}</button>
          </div>
        </form>
      </div>
    </div>
  )
}
