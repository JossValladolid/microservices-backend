import { useState } from 'react'
import { useNavigate, Link } from 'react-router'
import { Toaster, toast } from 'react-hot-toast'
import { useAuth } from '../context/AuthContext.jsx'

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
      const response = await fetch('/api/Auth/login', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({ email, password })
      })

      if (response.ok) {
        const usuario = await response.json()
        toast.success(`Hola, ${usuario.name}`)
        iniciarSesion(usuario)
        navigate('/')
      } else {
        toast.error('Credenciales inválidas')
      }
    } catch (error) {
      console.error('Error al iniciar sesión:', error)
      toast.error('Ocurrió un error al iniciar sesión')
    } finally {
      setCargando(false)
    }
  }

  return (
    <>
      <Toaster position="top-center" />

      <form onSubmit={handleSubmit}>
        <h2>Iniciar sesión</h2>
        <div>
          <label htmlFor="loginEmail">Correo electrónico:</label>
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
          {cargando ? 'Iniciando sesión...' : 'Iniciar sesión'}
        </button>
      </form>
      <p>
        ¿No tienes cuenta?{' '}
        <Link to="/registro">Regístrate</Link>
      </p>
    </>
  )
}
