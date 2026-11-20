import { useState } from 'react'
import { api, mensajeError } from '../api.js'
import { sha256Hex } from '../utils/sha256.js'

export const MAX_FILE_SIZE = 10 * 1024 * 1024

export function useUploadFiles() {
  const [uploading, setUploading] = useState(false)

  const uploadFile = async (file, folderId) => {
    if (!file) return null

    if (file.size > MAX_FILE_SIZE) {
      throw new Error('Los archivos no pueden pesar más de 10 MB')
    }

    setUploading(true)
    try {
      const contentType = file.type || 'application/octet-stream'
      const sha256 = await sha256Hex(file)

      let datosSubida
      try {
        const response = await api.get('/files/upload-url', {
          params: { fileName: file.name, contentType, sha256, folderId, size: file.size }
        })
        datosSubida = response.data
      } catch (err) {
        throw new Error(mensajeError(err, 'No se pudo obtener la URL de subida'), { cause: err })
      }

      const { uploadUrl, fileId, uploadHeaders } = datosSubida

      // La subida va directo a MinIO con la URL prefirmada (no es un request a la API).
      const s3Response = await fetch(uploadUrl, {
        method: 'PUT',
        headers: uploadHeaders,
        body: file
      })

      if (!s3Response.ok) {
        throw new Error(`Error al subir el archivo al almacenamiento (${s3Response.statusText || s3Response.status})`)
      }

      try {
        await api.post(`/files/${fileId}/confirm`)
      } catch (err) {
        throw new Error(mensajeError(err, 'No se pudo confirmar la subida'), { cause: err })
      }

      return { fileId, fileName: file.name }
    } finally {
      setUploading(false)
    }
  }

  return { uploadFile, uploading }
}

export default useUploadFiles
