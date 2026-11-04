import { useEffect, useRef, useState } from 'react'
import { toast } from 'react-hot-toast'
import { apiFetch } from '../api.js'
import { sha256Hex } from '../utils/sha256.js'
import { useAuth } from '../context/AuthContext.jsx'
import { useUploadFiles } from '../hooks/uploadFiles.jsx'

const formatSize = (bytes) => {
  if (bytes === 0 || !bytes) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`
}

const formatDate = (stringDate) => {
  if (!stringDate) return 'N/A'
  return new Date(stringDate).toLocaleDateString('es-MX', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  })
}

const leerJson = async (res, porDefecto) => {
  if (res.ok) return res.status === 204 ? null : res.json()
  const errorData = await res.json().catch(() => ({}))
  throw new Error(errorData.message || porDefecto)
}

export default function ArchivosModal({ abierto, onCerrar }) {
  const { usuarioActual } = useAuth()
  const { uploadFile, uploading } = useUploadFiles()
  const inputRef = useRef(null)

  const [carpetaId, setCarpetaId] = useState(null)
  const [datos, setDatos] = useState({ para: undefined, info: null, carpetas: [], archivos: [] })
  const [abriendoKey, setAbriendoKey] = useState(null)

  const esAdmin = !!usuarioActual?.isAdmin
  const cargando = datos.para !== carpetaId

  const cargarContenido = async (id) => {
    const query = id ? `?parentId=${id}` : ''
    const [info, carpetas, archivos] = await Promise.all([
      id ? apiFetch(`/api/folders/${id}`).then(r => leerJson(r, 'No se pudo cargar la carpeta')) : null,
      apiFetch(`/api/folders${query}`).then(r => leerJson(r, 'No se pudieron cargar las carpetas')),
      id ? apiFetch(`/api/files?folderId=${id}`).then(r => leerJson(r, 'No se pudieron cargar los archivos')) : []
    ])
    return { para: id, info, carpetas, archivos }
  }

  const recargar = async () => {
    try {
      setDatos(await cargarContenido(carpetaId))
    } catch (err) {
      toast.error(err.message || 'Error al conectar con el servidor')
    }
  }

  useEffect(() => {
    if (!abierto) return
    let vigente = true
    cargarContenido(carpetaId)
      .then(resultado => { if (vigente) setDatos(resultado) })
      .catch(err => {
        if (!vigente) return
        toast.error(err.message || 'Error al conectar con el servidor')
        setCarpetaId(null)
      })
    return () => { vigente = false }
  }, [abierto, carpetaId])

  if (!abierto) return null

  const esPropia = datos.info?.ownerId === usuarioActual?.id
  const ruta = datos.info?.path ?? []

  const obtenerArchivoVerificado = async (id) => {
    const { downloadUrl, sha256, contentType } = await leerJson(
      await apiFetch(`/api/files/${id}/download-url`),
      'No se pudo obtener el enlace'
    )

    const fileRes = await fetch(downloadUrl)
    if (!fileRes.ok) throw new Error('No se pudo descargar el archivo')
    const blob = await fileRes.blob()

    if (await sha256Hex(blob) !== sha256) {
      throw new Error('La integridad del archivo no se pudo verificar: el contenido fue modificado')
    }
    return new Blob([blob], { type: contentType })
  }

  const verArchivo = async (id) => {
    setAbriendoKey(id)
    try {
      const blob = await obtenerArchivoVerificado(id)
      window.open(URL.createObjectURL(blob), '_blank')
    } catch (err) {
      toast.error(err.message || 'Error al abrir el archivo')
    } finally {
      setAbriendoKey(null)
    }
  }

  const descargarArchivo = async (id, fileName) => {
    setAbriendoKey(id)
    try {
      const blob = await obtenerArchivoVerificado(id)
      const url = URL.createObjectURL(blob)

      const a = document.createElement('a')
      a.href = url
      a.download = fileName
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(url)
    } catch (err) {
      toast.error(err.message || 'Error al descargar el archivo')
    } finally {
      setAbriendoKey(null)
    }
  }

  const eliminarArchivo = async (id) => {
    if (!window.confirm('¿Deseas eliminar este archivo?')) return
    try {
      await leerJson(await apiFetch(`/api/files/${id}`, { method: 'DELETE' }), 'Error al eliminar archivo')
      toast.success('Archivo eliminado')
      recargar()
    } catch (err) {
      toast.error(err.message || 'No se pudo eliminar el archivo')
    }
  }

  const crearCarpeta = async () => {
    const name = window.prompt('Nombre de la nueva carpeta')
    if (!name) return
    try {
      await leerJson(await apiFetch('/api/folders', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, parentId: carpetaId })
      }), 'No se pudo crear la carpeta')
      recargar()
    } catch (err) {
      toast.error(err.message || 'No se pudo crear la carpeta')
    }
  }

  const eliminarCarpeta = async (carpeta) => {
    const aviso = esAdmin
      ? `¿Eliminar la carpeta "${carpeta.name}" con TODO su contenido (subcarpetas y archivos)? No se puede deshacer.`
      : `¿Eliminar la carpeta "${carpeta.name}"? Debe estar vacía.`
    if (!window.confirm(aviso)) return
    try {
      await leerJson(await apiFetch(`/api/folders/${carpeta.id}`, { method: 'DELETE' }), 'No se pudo eliminar la carpeta')
      toast.success('Carpeta eliminada')
      recargar()
    } catch (err) {
      toast.error(err.message || 'No se pudo eliminar la carpeta')
    }
  }

  const subirAqui = async (e) => {
    const file = e.target.files?.[0]
    if (!file) return
    try {
      const result = await uploadFile(file, carpetaId)
      toast.success(`Archivo "${result.fileName}" subido`)
      recargar()
    } catch (err) {
      toast.error(err.message || 'Error al subir el archivo')
    } finally {
      e.target.value = ''
    }
  }

  const vacio = datos.carpetas.length === 0 && datos.archivos.length === 0

  return (
    <div className="modal-overlay" onClick={onCerrar}>
      <div className="modal modal-lg" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h3>Archivos</h3>
          <div style={{ display: 'flex', gap: '6px' }}>
            {esPropia && (
              <>
                <input type="file" ref={inputRef} style={{ display: 'none' }} onChange={subirAqui} />
                <button type="button" onClick={() => inputRef.current?.click()} disabled={uploading}>
                  {uploading ? 'Subiendo...' : 'Subir aquí'}
                </button>
                <button type="button" onClick={crearCarpeta}>Nueva carpeta</button>
              </>
            )}
            <button type="button" onClick={recargar} disabled={cargando}>Actualizar</button>
          </div>
        </div>

        <p>
          <button type="button" onClick={() => setCarpetaId(null)}>Inicio</button>
          {ruta.map((nivel) => (
            <span key={nivel.id}>
              {' / '}
              <button type="button" onClick={() => setCarpetaId(nivel.id)}>{nivel.name}</button>
            </span>
          ))}
        </p>

        {cargando ? (
          <p>Cargando...</p>
        ) : vacio ? (
          <p>{carpetaId ? 'Esta carpeta está vacía.' : 'No hay carpetas.'}</p>
        ) : (
          <table className="tabla">
            <thead>
              <tr>
                <th>Nombre</th>
                <th>Tamaño</th>
                <th>Fecha</th>
                <th>Acciones</th>
              </tr>
            </thead>
            <tbody>
              {datos.carpetas.map((carpeta) => (
                <tr key={carpeta.id}>
                  <td style={{ wordBreak: 'break-all' }}>📁 {carpeta.name}</td>
                  <td>—</td>
                  <td>{formatDate(carpeta.createdAt)}</td>
                  <td>
                    <div style={{ display: 'flex', gap: '6px' }}>
                      <button type="button" onClick={() => setCarpetaId(carpeta.id)}>Abrir</button>
                      {carpeta.parentId && (carpeta.ownerId === usuarioActual?.id || esAdmin) && (
                        <button
                          type="button"
                          onClick={() => eliminarCarpeta(carpeta)}
                          style={{ color: '#c00', borderColor: '#c00' }}
                        >
                          Eliminar
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
              {datos.archivos.map((archivo) => (
                <tr key={archivo.id}>
                  <td title={archivo.fileName} style={{ wordBreak: 'break-all' }}>
                    {archivo.fileName}
                  </td>
                  <td>{formatSize(archivo.size)}</td>
                  <td>{formatDate(archivo.lastModified)}</td>
                  <td>
                    <div style={{ display: 'flex', gap: '6px' }}>
                      <button
                        type="button"
                        onClick={() => verArchivo(archivo.id)}
                        disabled={abriendoKey === archivo.id}
                      >
                        Ver
                      </button>
                      <button
                        type="button"
                        onClick={() => descargarArchivo(archivo.id, archivo.fileName)}
                        disabled={abriendoKey === archivo.id}
                      >
                        Descargar
                      </button>
                      {(archivo.ownerId === usuarioActual?.id || esAdmin) && (
                        <button
                          type="button"
                          onClick={() => eliminarArchivo(archivo.id)}
                          style={{ color: '#c00', borderColor: '#c00' }}
                        >
                          Eliminar
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        <div className="modal-actions">
          <button type="button" onClick={onCerrar}>
            Cerrar
          </button>
        </div>
      </div>
    </div>
  )
}
