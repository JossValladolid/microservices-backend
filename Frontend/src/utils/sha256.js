// crypto.subtle solo existe en contextos seguros (https o localhost). El archivo se carga completo en memoria.
export async function sha256Hex(blob) {
  if (!globalThis.crypto?.subtle) {
    throw new Error('El navegador no puede calcular el hash: abre la app por HTTPS')
  }
  const digest = await crypto.subtle.digest('SHA-256', await blob.arrayBuffer())
  return Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, '0')).join('')
}
