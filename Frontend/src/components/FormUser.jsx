import { useState } from 'react'
import { Toaster, toast } from 'react-hot-toast'

export default function FormUser({ onUserCreated, onCancelar, titulo = 'Crear Usuario' }) {
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [birthDate, setBirthDate] = useState('')
  const [password, setPassword] = useState('')
  const [passwordConfirm, setPasswordConfirm] = useState('')
  const [cargando, setCargando] = useState(false)

  const handleSubmit = async (e) => {
    e.preventDefault()
    setCargando(true)

    if (password !== passwordConfirm) {
      toast.error('Las contraseñas no coinciden')
      setCargando(false)
      return
    }

    // el input type="date" ya entrega "YYYY-MM-DD", el formato que espera DateOnly
    const userData = { name, email, birthDate, password, passwordConfirm }

    try {
      const response = await fetch('/api/User', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(userData)
      })

      if (response.ok) {
        toast.success('Usuario creado exitosamente')
        setName('')
        setEmail('')
        setBirthDate('')
        setPassword('')
        setPasswordConfirm('')

        onUserCreated?.()

      } else {
        toast.error(`Error al crear el usuario.`)
      }
    } catch (error) {
      console.error('Error al crear el usuario:', error)
      toast.error('Ocurrió un error al crear el usuario')
    } finally {
      setCargando(false)
    }
  }

  return (
    <>
    <Toaster position="top-center" />

    <form onSubmit={handleSubmit}>
      <h2>{titulo}</h2>
      <div>
        <label htmlFor="name">Nombre:</label>
        <input
          type="text"
          id="name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          required
        />
      </div>
      <div>
        <label htmlFor="email">Correo electrónico:</label>
        <input
          type="email"
          id="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />
      </div>
      <div>
        <label htmlFor="birthDate">Fecha de nacimiento:</label>
        <input
          type="date"
          id="birthDate"
          value={birthDate}
          max={new Date().toLocaleDateString('en-CA')}
          onChange={(e) => setBirthDate(e.target.value)}
          required
        />
      </div>
      <div>
        <label htmlFor="password">Contraseña:</label>
        <input
          type="password"
          id="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
      </div>
      <div>
        <label htmlFor="passwordConfirm">Confirmar Contraseña:</label>
        <input
          type="password"
          id="passwordConfirm"
          value={passwordConfirm}
          onChange={(e) => setPasswordConfirm(e.target.value)}
          required
        />
      </div>
      <button type="submit" disabled={cargando}>
        {cargando ? 'Creando...' : 'Crear Usuario'}
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
