import { useState } from 'react'
import { apiFetch } from '../api.js'

export function useUploadFiles() {
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState(null)
  const [uploadedFile, setUploadedFile] = useState(null)

  const uploadFile = async (file) => {
    if (!file) return null

    setUploading(true)
    setError(null)

    try {
      const contentType = file.type || 'application/octet-stream'
      const params = new URLSearchParams({
        fileName: file.name,
        contentType: contentType
      })

      // 1. Fetch presigned upload URL from backend
      const response = await apiFetch(`/api/files/upload-url?${params.toString()}`)
      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}))
        throw new Error(errorData.message || 'No se pudo obtener la URL de subida')
      }

      const { uploadUrl, fileKey } = await response.json()

      // 2. Direct PUT to S3 / MinIO presigned URL using standard async fetch
      const s3Response = await fetch(uploadUrl, {
        method: 'PUT',
        headers: {
          'Content-Type': contentType
        },
        body: file
      })

      if (!s3Response.ok) {
        throw new Error(`Error al subir el archivo al almacenamiento (${s3Response.statusText || s3Response.status})`)
      }

      const result = { fileKey, fileName: file.name }
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