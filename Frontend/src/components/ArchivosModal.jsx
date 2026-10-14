import { useEffect, useState } from 'react'
import { toast } from 'react-hot-toast'
import { apiFetch } from '../api.js'

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

export default function ArchivosModal({ abierto, onCerrar }) {
  const [archivos, setArchivos] = useState([])
  const [cargando, setCargando] = useState(false)
  const [abriendoKey, setAbriendoKey] = useState(null)

  const cargarArchivos = async () => {
    setCargando(true)
    try {
      const res = await apiFetch('/api/files')
      if (res.ok) {
        const data = await res.json()
        setArchivos(data)
      } else {
        toast.error('No se pudieron cargar los archivos')
      }
    } catch (err) {
      console.error('Error al cargar archivos:', err)
      toast.error('Error al conectar con el servidor')
    } finally {
      setCargando(false)
    }
  }

  useEffect(() => {
    if (abierto) {
      cargarArchivos()
    }
  }, [abierto])

  if (!abierto) return null

  const verArchivo = async (fileKey) => {
    setAbriendoKey(fileKey)
    try {
      const res = await apiFetch(`/api/files/download-url?fileKey=${encodeURIComponent(fileKey)}`)
      if (!res.ok) throw new Error('No se pudo obtener el enlace')
      const { downloadUrl } = await res.json()
      window.open(downloadUrl, '_blank')
    } catch (err) {
      toast.error(err.message || 'Error al abrir el archivo')
    } finally {
      setAbriendoKey(null)
    }
  }

  const descargarArchivo = async (fileKey, fileName) => {
    setAbriendoKey(fileKey)
    try {
      const res = await apiFetch(
        `/api/files/download-url?fileKey=${encodeURIComponent(fileKey)}&downloadName=${encodeURIComponent(fileName)}`
      )
      if (!res.ok) throw new Error('No se pudo generar la descarga')
      const { downloadUrl } = await res.json()

      const a = document.createElement('a')
      a.href = downloadUrl
      a.download = fileName
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
    } catch (err) {
      toast.error(err.message || 'Error al descargar el archivo')
    } finally {
      setAbriendoKey(null)
    }
  }

  const eliminarArchivo = async (fileKey) => {
    if (!window.confirm('¿Deseas eliminar este archivo?')) return
    try {
      const res = await apiFetch(`/api/files?fileKey=${encodeURIComponent(fileKey)}`, {
        method: 'DELETE'
      })
      if (!res.ok) throw new Error('Error al eliminar archivo')
      toast.success('Archivo eliminado')
      cargarArchivos()
    } catch (err) {
      toast.error(err.message || 'No se pudo eliminar el archivo')
    }
  }

  return (
    <div className="modal-overlay" onClick={onCerrar}>
      <div className="modal modal-lg" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h3>Mis Archivos</h3>
          <button type="button" onClick={cargarArchivos} disabled={cargando}>
            {cargando ? 'Cargando...' : 'Actualizar'}
          </button>
        </div>

        {cargando && archivos.length === 0 ? (
          <p>Cargando lista de archivos...</p>
        ) : archivos.length === 0 ? (
          <p>No hay archivos subidos todavía.</p>
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
              {archivos.map((archivo) => (
                <tr key={archivo.key}>
                  <td title={archivo.key} style={{ wordBreak: 'break-all' }}>
                    {archivo.fileName}
                  </td>
                  <td>{formatSize(archivo.size)}</td>
                  <td>{formatDate(archivo.lastModified)}</td>
                  <td>
                    <div style={{ display: 'flex', gap: '6px' }}>
                      <button
                        type="button"
                        onClick={() => verArchivo(archivo.key)}
                        disabled={abriendoKey === archivo.key}
                      >
                        Ver
                      </button>
                      <button
                        type="button"
                        onClick={() => descargarArchivo(archivo.key, archivo.fileName)}
                        disabled={abriendoKey === archivo.key}
                      >
                        Descargar
                      </button>
                      <button
                        type="button"
                        onClick={() => eliminarArchivo(archivo.key)}
                        style={{ color: '#c00', borderColor: '#c00' }}
                      >
                        Eliminar
                      </button>
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
