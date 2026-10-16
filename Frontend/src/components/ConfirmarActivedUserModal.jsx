export default function ConfirmarActivedUserModal({ usuario, procesando, onConfirmar, onCancelar }) {
  if (!usuario) return null

  const active = !usuario.isActive

  return (
    <div className="modal-overlay" onClick={onCancelar}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <h3>{active ? 'Activar cuenta' : 'Desactivar cuenta'}</h3>
        <p>
          {active
            ? `¿Seguro que quieres activar la cuenta de ${usuario.name}?`
            : `¿Seguro que quieres desactivar la cuenta de ${usuario.name}?`}
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
