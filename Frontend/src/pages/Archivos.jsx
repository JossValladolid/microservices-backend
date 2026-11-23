import { useEffect, useRef, useState } from 'react'
import { toast } from 'react-hot-toast'
import { api, mensajeError } from '../api.js'
import { sha256Hex } from '../utils/sha256.js'
import { formatDate, formatSize, extension, sinVacios } from '../utils/formato.js'
import { useAuth } from '../context/AuthContext.jsx'
import { useUploadFiles } from '../hooks/uploadFiles.jsx'
import Paginacion from '../components/Paginacion.jsx'

const FILTROS_VACIOS = { name: '', type: '', owner: '', dateFrom: '', dateTo: '' }

export default function Archivos() {
  const { usuarioActual } = useAuth()
  const esAdmin = !!usuarioActual?.isAdmin
  const { uploadFile, uploading } = useUploadFiles()
  const inputRef = useRef(null)

  const [carpetaId, setCarpetaId] = useState(null)
  const [filtros, setFiltros] = useState(FILTROS_VACIOS)
  const [borrador, setBorrador] = useState(FILTROS_VACIOS)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [version, setVersion] = useState(0)
  const [datos, setDatos] = useState({ clave: null, info: null, items: [], total: 0 })
  const [ocupadoId, setOcupadoId] = useState(null)

  const clave = JSON.stringify({ carpetaId, filtros, page, pageSize, version })
  const cargando = datos.clave !== clave

  useEffect(() => {
    let vigente = true
    const params = sinVacios({ parentId: carpetaId, page, pageSize, ...filtros })

    Promise.all([
      carpetaId ? api.get(`/folders/${carpetaId}`).then(r => r.data) : null,
      api.get('/folders/contents', { params }).then(r => r.data)
    ])
      .then(([info, contenido]) => {
        if (!vigente) return
        if (contenido.items.length === 0 && page > 1 && contenido.total > 0) {
          setPage(Math.ceil(contenido.total / pageSize))
          return
        }
        setDatos({ clave, info, items: contenido.items, total: contenido.total })
      })
      .catch(err => {
        if (!vigente) return
        toast.error(mensajeError(err, 'No se pudo cargar el contenido'))
        if (carpetaId) {
          setCarpetaId(null)
        } else {
          setDatos({ clave, info: null, items: [], total: 0 })
        }
      })

    return () => { vigente = false }
  }, [clave, carpetaId, filtros, page, pageSize])

  const recargar = () => setVersion(v => v + 1)

  const abrirCarpeta = (id) => {
    setCarpetaId(id)
    setPage(1)
    setFiltros(FILTROS_VACIOS)
    setBorrador(FILTROS_VACIOS)
  }

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

  const cambiarBorrador = (campo) => (e) => setBorrador(b => ({ ...b, [campo]: e.target.value }))

  const info = datos.info
  const ruta = info?.path ?? []
  const esPropia = !!carpetaId && !!info && info.ownerId === usuarioActual?.id
  const puedeSubir = esPropia && !esAdmin
  const puedeEscribir = !!carpetaId && !!info && (esPropia || esAdmin)
  const puedeModificar = (item) => item.ownerId === usuarioActual?.id || esAdmin

  const obtenerArchivoVerificado = async (id) => {
    const { data } = await api.get(`/files/${id}/download-url`)

    const fileRes = await fetch(data.downloadUrl)
    if (!fileRes.ok) throw new Error('No se pudo descargar el archivo')
    const blob = await fileRes.blob()

    if (await sha256Hex(blob) !== data.sha256) {
      throw new Error('La integridad del archivo no se pudo verificar: el contenido fue modificado')
    }
    return new Blob([blob], { type: data.contentType })
  }

  const verArchivo = async (item) => {
    setOcupadoId(item.id)
    try {
      const blob = await obtenerArchivoVerificado(item.id)
      window.open(URL.createObjectURL(blob), '_blank')
    } catch (err) {
      toast.error(mensajeError(err, 'Error al abrir el archivo'))
    } finally {
      setOcupadoId(null)
    }
  }

  const descargarArchivo = async (item) => {
    setOcupadoId(item.id)
    try {
      const blob = await obtenerArchivoVerificado(item.id)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = item.name
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(url)
    } catch (err) {
      toast.error(mensajeError(err, 'Error al descargar el archivo'))
    } finally {
      setOcupadoId(null)
    }
  }

  const renombrar = async (item) => {
    const esArchivo = item.kind === 'file'
    const ext = esArchivo ? extension(item.name) : ''
    const actual = ext ? item.name.slice(0, -ext.length) : item.name
    const mensaje = esArchivo && ext
      ? `Nuevo nombre para "${item.name}" (la extensión ${ext} se conserva)`
      : `Nuevo nombre para "${item.name}"`

    const nombre = window.prompt(mensaje, actual)
    if (nombre === null || nombre.trim() === '' || nombre.trim() === actual) return

    try {
      await api.patch(esArchivo ? `/files/${item.id}` : `/folders/${item.id}`, { name: nombre })
      toast.success('Nombre actualizado')
      recargar()
    } catch (err) {
      toast.error(mensajeError(err, 'No se pudo cambiar el nombre'))
    }
  }

  const eliminar = async (item) => {
    const aviso = item.kind === 'file'
      ? `¿Deseas eliminar el archivo "${item.name}"?`
      : esAdmin
        ? `¿Eliminar la carpeta "${item.name}" con TODO su contenido (subcarpetas y archivos)? No se puede deshacer.`
        : `¿Eliminar la carpeta "${item.name}"? Debe estar vacía.`
    if (!window.confirm(aviso)) return

    try {
      await api.delete(item.kind === 'file' ? `/files/${item.id}` : `/folders/${item.id}`)
      toast.success(item.kind === 'file' ? 'Archivo eliminado' : 'Carpeta eliminada')
      recargar()
    } catch (err) {
      toast.error(mensajeError(err, 'No se pudo eliminar'))
    }
  }

  const crearCarpeta = async () => {
    const name = window.prompt('Nombre de la nueva carpeta')
    if (!name?.trim()) return
    try {
      await api.post('/folders', { name, parentId: carpetaId })
      toast.success('Carpeta creada')
      recargar()
    } catch (err) {
      toast.error(mensajeError(err, 'No se pudo crear la carpeta'))
    }
  }

  const subirAqui = async (e) => {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    try {
      const result = await uploadFile(file, carpetaId)
      toast.success(`Archivo "${result.fileName}" subido`)
      recargar()
    } catch (err) {
      toast.error(err.message || 'Error al subir el archivo')
    }
  }

  const hayFiltros = Object.values(filtros).some(v => v !== '')

  return (
    <div>
      <div className="barra">
        <h2 className="titulo">Archivos</h2>
        <div className="acciones">
          {puedeSubir && (
            <>
              <input type="file" ref={inputRef} style={{ display: 'none' }} onChange={subirAqui} />
              <button type="button" className="btn-primario" onClick={() => inputRef.current?.click()} disabled={uploading}>
                {uploading ? 'Subiendo...' : 'Subir archivo (máx. 10 MB)'}
              </button>
            </>
          )}
          {puedeEscribir && (
            <button type="button" onClick={crearCarpeta}>Nueva carpeta</button>
          )}
          <button type="button" onClick={recargar} disabled={cargando}>Actualizar</button>
        </div>
      </div>

      <p className="ruta">
        <button type="button" onClick={() => abrirCarpeta(null)}>Inicio</button>
        {ruta.map((nivel) => (
          <span key={nivel.id}>
            {' / '}
            <button type="button" onClick={() => abrirCarpeta(nivel.id)}>{nivel.name}</button>
          </span>
        ))}
      </p>
      {!carpetaId && (
        <p className="nota">Cada usuario tiene una carpeta con su nombre de usuario. Los archivos se suben dentro de alguna carpeta.</p>
      )}

      <form className="filtros" onSubmit={aplicarFiltros}>
        <label>Nombre<input value={borrador.name} onChange={cambiarBorrador('name')} /></label>
        <label>Tipo<input value={borrador.type} onChange={cambiarBorrador('type')} placeholder="pdf, image..." /></label>
        <label>Propietario<input value={borrador.owner} onChange={cambiarBorrador('owner')} /></label>
        <label>Subido desde<input type="date" value={borrador.dateFrom} onChange={cambiarBorrador('dateFrom')} /></label>
        <label>Subido hasta<input type="date" value={borrador.dateTo} onChange={cambiarBorrador('dateTo')} /></label>
        <div className="acciones">
          <button type="submit">Filtrar</button>
          <button type="button" onClick={limpiarFiltros}>Limpiar</button>
        </div>
      </form>

      {cargando ? (
        <p>Cargando...</p>
      ) : datos.items.length === 0 ? (
        <p>{hayFiltros ? 'Ningún elemento coincide con los filtros.' : carpetaId ? 'Esta carpeta está vacía.' : 'No hay carpetas.'}</p>
      ) : (
        <table className="tabla">
          <thead>
            <tr>
              <th>Nombre</th>
              <th>Tipo</th>
              <th>Tamaño</th>
              <th>Propietario</th>
              <th>Fecha de subida</th>
              <th>Acciones</th>
            </tr>
          </thead>
          <tbody>
            {datos.items.map((item) => {
              const esCarpeta = item.kind === 'folder'
              const esHome = esCarpeta && !item.parentId
              return (
                <tr key={`${item.kind}-${item.id}`}>
                  <td style={{ wordBreak: 'break-all' }}>{esCarpeta ? '📁 ' : ''}{item.name}</td>
                  <td>{esCarpeta ? 'Carpeta' : item.contentType}</td>
                  <td>{esCarpeta ? '—' : formatSize(item.size)}</td>
                  <td>{item.ownerName}</td>
                  <td>{formatDate(item.createdAt)}</td>
                  <td>
                    <div className="acciones">
                      {esCarpeta ? (
                        <button type="button" onClick={() => abrirCarpeta(item.id)}>Abrir</button>
                      ) : (
                        <>
                          <button type="button" onClick={() => verArchivo(item)} disabled={ocupadoId === item.id}>Ver</button>
                          <button type="button" onClick={() => descargarArchivo(item)} disabled={ocupadoId === item.id}>Descargar</button>
                        </>
                      )}
                      {!esHome && puedeModificar(item) && (
                        <>
                          <button type="button" onClick={() => renombrar(item)}>Renombrar</button>
                          <button type="button" className="peligro" onClick={() => eliminar(item)}>Eliminar</button>
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
    </div>
  )
}
