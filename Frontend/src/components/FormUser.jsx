import { useState } from 'react'
import { toast } from 'react-hot-toast'
import { api, mensajeError } from '../api.js'

const VACIO = { name: '', userName: '', email: '', birthDate: '', password: '', passwordConfirm: '', isAdmin: false }

// permitirRol: solo en el módulo de usuarios (administradores). En el autoregistro el backend ignora el rol.
export default function FormUser({ onUserCreated, onCancelar, titulo = 'Crear Usuario', permitirRol = false }) {
  const [form, setForm] = useState(VACIO)
  const [cargando, setCargando] = useState(false)

  const cambiar = (campo) => (e) => {
    const valor = e.target.type === 'checkbox' ? e.target.checked : e.target.value
    setForm(f => ({ ...f, [campo]: campo === 'userName' ? valor.toLowerCase() : valor }))
  }

  const handleSubmit = async (e) => {
    e.preventDefault()

    if (form.password !== form.passwordConfirm) {
      toast.error('Las contraseñas no coinciden')
      return
    }

    setCargando(true)
    try {
      // el input type="date" ya entrega "YYYY-MM-DD", el formato que espera DateOnly
      await api.post('/User', { ...form, isAdmin: permitirRol && form.isAdmin })
      toast.success(permitirRol ? 'Usuario creado exitosamente' : 'Cuenta creada. Un administrador debe activarla antes de que puedas entrar.')
      setForm(VACIO)
      onUserCreated?.()
    } catch (error) {
      toast.error(mensajeError(error, 'Error al crear el usuario.'))
    } finally {
      setCargando(false)
    }
  }

  return (
    <>
      <form onSubmit={handleSubmit}>
        <h2>{titulo}</h2>
        <div>
          <label htmlFor="name">Nombre:</label>
          <input type="text" id="name" value={form.name} onChange={cambiar('name')} required />
        </div>
        <div>
          <label htmlFor="userName">Nombre de usuario (nombre de su carpeta):</label>
          <input
            type="text"
            id="userName"
            value={form.userName}
            onChange={cambiar('userName')}
            pattern="[a-z0-9_\-]{3,30}"
            title="De 3 a 30 caracteres: letras minúsculas, números, - y _"
            required
          />
        </div>
        <div>
          <label htmlFor="email">Correo electrónico:</label>
          <input type="email" id="email" value={form.email} onChange={cambiar('email')} required />
        </div>
        <div>
          <label htmlFor="birthDate">Fecha de nacimiento:</label>
          <input
            type="date"
            id="birthDate"
            value={form.birthDate}
            max={new Date().toLocaleDateString('en-CA')}
            onChange={cambiar('birthDate')}
            required
          />
        </div>
        <div>
          <label htmlFor="password">Contraseña:</label>
          <input type="password" id="password" value={form.password} onChange={cambiar('password')} required />
        </div>
        <div>
          <label htmlFor="passwordConfirm">Confirmar contraseña:</label>
          <input type="password" id="passwordConfirm" value={form.passwordConfirm} onChange={cambiar('passwordConfirm')} required />
        </div>
        {permitirRol && (
          <div>
            <label htmlFor="rol">Rol:</label>
            <select
              id="rol"
              value={form.isAdmin ? 'admin' : 'comun'}
              onChange={(e) => setForm(f => ({ ...f, isAdmin: e.target.value === 'admin' }))}
            >
              <option value="comun">Usuario común</option>
              <option value="admin">Administrador</option>
            </select>
          </div>
        )}
        <button type="submit" disabled={cargando}>
          {cargando ? 'Creando...' : 'Crear usuario'}
        </button>
      </form>
      {onCancelar && (
        <p>
          ¿Ya tienes cuenta?{' '}
          <button type="button" onClick={onCancelar}>
            Iniciar sesión
          </button>
        </p>
      )}
    </>
  )
}
