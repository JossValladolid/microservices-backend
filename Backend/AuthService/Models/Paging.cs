namespace AuthService.Models;

/// <summary>
/// Página de resultados de una consulta paginada.
/// </summary>
public class PagedResult<T>
{
    /// <summary>Elementos de la página.</summary>
    public IReadOnlyList<T> Items { get; set; } = [];

    /// <summary>Total de elementos que cumplen los filtros.</summary>
    public int Total { get; set; }

    /// <summary>Página actual (empieza en 1).</summary>
    public int Page { get; set; }

    /// <summary>Tamaño de página.</summary>
    public int PageSize { get; set; }
}

/// <summary>
/// Utilidades para paginar y filtrar consultas.
/// </summary>
public static class Paging
{
    /// <summary>Tamaño máximo de página permitido.</summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// Normaliza la página y su tamaño a valores válidos.
    /// </summary>
    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));

    /// <summary>
    /// Patrón ILIKE para buscar <paramref name="value"/> como sub-cadena (escapa los comodines de LIKE).
    /// </summary>
    public static string ContainsPattern(string value) => $"%{Escape(value.Trim())}%";

    /// <summary>
    /// Escapa los caracteres especiales de LIKE (\, % y _) con la barra invertida, el escape por defecto de PostgreSQL.
    /// </summary>
    public static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
