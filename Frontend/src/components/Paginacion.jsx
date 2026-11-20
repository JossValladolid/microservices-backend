const TAMANOS = [5, 10, 20, 50, 100]

export default function Paginacion({ page, pageSize, total, onPage, onPageSize }) {
  const paginas = Math.max(1, Math.ceil(total / pageSize))

  return (
    <div className="paginacion">
      <span>{total} resultado{total === 1 ? '' : 's'}</span>
      <label>
        Por página:{' '}
        <select value={pageSize} onChange={(e) => onPageSize(Number(e.target.value))}>
          {TAMANOS.map(n => <option key={n} value={n}>{n}</option>)}
        </select>
      </label>
      <div className="acciones">
        <button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)}>Anterior</button>
        <span>Página {page} de {paginas}</span>
        <button type="button" disabled={page >= paginas} onClick={() => onPage(page + 1)}>Siguiente</button>
      </div>
    </div>
  )
}
