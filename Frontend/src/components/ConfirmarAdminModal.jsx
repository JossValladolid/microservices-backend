export default function ConfirmarAdminModal({ usuario, procesando, onConfirmar, onCancelar }) {
  if (!usuario) return null

  const haceAdmin = !usuario.isAdmin

  return (
    <div className="modal-overlay" onClick={onCancelar}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <h3>{haceAdmin ? 'Hacer administrador' : 'Quitar administrador'}</h3>
        <p>
          {haceAdmin
            ? `¿Seguro que quieres darle privilegios de administrador a ${usuario.name}?`
            : `¿Seguro que quieres quitarle privilegios de administrador a ${usuario.name}?`}
        </p>
        <div className="modal-actions">
          <button type="button" onClick={onCancelar} disabled={procesando}>
            Cancelar
          </button>
          <button type="button" onClick={onConfirmar} disabled={procesando}>
            {procesando ? 'Guardando...' : 'Confirmar'}
          </button>
        </div>
      </div>
    </div>
  )
}
