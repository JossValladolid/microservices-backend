import { useState } from 'react'
import { apiFetch } from '../api.js'
import { sha256Hex } from '../utils/sha256.js'

export function useUploadFiles() {
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState(null)
  const [uploadedFile, setUploadedFile] = useState(null)

  const uploadFile = async (file, folderId) => {
    if (!file) return null

    setUploading(true)
    setError(null)

    try {
      const contentType = file.type || 'application/octet-stream'
      const sha256 = await sha256Hex(file)
      const params = new URLSearchParams({
        fileName: file.name,
        contentType: contentType,
        sha256: sha256,
        folderId: folderId
      })

      const response = await apiFetch(`/api/files/upload-url?${params.toString()}`)
      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}))
        throw new Error(errorData.message || 'No se pudo obtener la URL de subida')
      }

      const { uploadUrl, fileId, uploadHeaders } = await response.json()

      const s3Response = await fetch(uploadUrl, {
        method: 'PUT',
        headers: uploadHeaders,
        body: file
      })

      if (!s3Response.ok) {
        throw new Error(`Error al subir el archivo al almacenamiento (${s3Response.statusText || s3Response.status})`)
      }

      const confirmResponse = await apiFetch(`/api/files/${fileId}/confirm`, { method: 'POST' })
      if (!confirmResponse.ok) {
        const errorData = await confirmResponse.json().catch(() => ({}))
        throw new Error(errorData.message || 'No se pudo confirmar la subida')
      }

      const result = { fileId, fileName: file.name }
      setUploadedFile(result)
      return result
    } catch (err) {
      setError(err)
      throw err
    } finally {
      setUploading(false)
    }
  }

  return { uploadFile, uploading, error, uploadedFile }
}

export { useUploadFiles as uploadFiles }
export default useUploadFiles