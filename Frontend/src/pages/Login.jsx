import { useState } from 'react'
import { useNavigate, Link } from 'react-router'
import { toast } from 'react-hot-toast'
import { useAuth } from '../context/AuthContext.jsx'
import { api, mensajeError } from '../api.js'

export default function Login() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [cargando, setCargando] = useState(false)
  const { iniciarSesion } = useAuth()
  const navigate = useNavigate()

  const handleSubmit = async (e) => {
    e.preventDefault()
    setCargando(true)

    try {
      const { data: usuario } = await api.post('/Auth/login', { email, password })
      toast.success(`Hola, ${usuario.name}`)
      iniciarSesion(usuario)
      navigate('/archivos')
    } catch (error) {
      const status = error.response?.status
      if (status === 403) {
        toast.error(mensajeError(error, 'Tu cuenta está pendiente de activación'))
      } else if (status === 401) {
        toast.error('Credenciales inválidas')
      } else {
        toast.error(mensajeError(error, 'Ocurrió un error al iniciar sesión'))
      }
    } finally {
      setCargando(false)
    }
  }

  return (
    <div className="auth">
      <form onSubmit={handleSubmit}>
        <h2>Iniciar sesión</h2>
        <div>
          <label htmlFor="loginEmail">E-mail:</label>
          <input
            type="email"
            id="loginEmail"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
        </div>
        <div>
          <label htmlFor="loginPassword">Contraseña:</label>
          <input
            type="password"
            id="loginPassword"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </div>
        <button type="submit" disabled={cargando}>
          {cargando ? 'Ingresando...' : 'Ingresar'}
        </button>
      </form>
      <p>
        ¿No tienes cuenta?{' '}
        <Link to="/registro">Regístrate</Link>
      </p>
    </div>
  )
}
