import { useNavigate } from 'react-router'
import FormUser from '../components/FormUser.jsx'

export default function Register() {
  const navigate = useNavigate()

  return (
    <FormUser
      titulo="Crear cuenta"
      onUserCreated={() => navigate('/login')}
      onCancelar={() => navigate('/login')}
    />
  )
}
