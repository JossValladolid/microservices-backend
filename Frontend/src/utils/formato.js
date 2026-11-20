export const formatDate = (stringDate) => {
  if (!stringDate) return 'N/A'
  return new Date(stringDate).toLocaleDateString('es-MX', {
    year: 'numeric', month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit'
  })
}

// DateOnly llega como "YYYY-MM-DD": se arma en hora local para que no se recorra un día.
export const formatBirthDate = (stringDate) => {
  if (!stringDate) return 'N/A'
  const [year, month, day] = stringDate.split('-').map(Number)
  return new Date(year, month - 1, day).toLocaleDateString('es-MX', {
    year: 'numeric', month: 'short', day: 'numeric'
  })
}

export const formatSize = (bytes) => {
  if (!bytes) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`
}

// Igual que Path.GetExtension de .NET: desde el último punto (si no es el último carácter).
export const extension = (nombre) => {
  const i = nombre.lastIndexOf('.')
  return i >= 0 && i < nombre.length - 1 ? nombre.slice(i) : ''
}

// Quita los parámetros vacíos para no mandarlos en la query.
export const sinVacios = (objeto) =>
  Object.fromEntries(Object.entries(objeto).filter(([, valor]) => valor !== '' && valor !== null && valor !== undefined))
