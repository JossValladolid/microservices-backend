using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AuthService.Models;

/// <summary>
/// Representa un usuario en el sistema.
/// </summary>
public class User
{
    /// <summary>
    /// Identificador único del usuario.
    /// </summary>
    [Key]
    public Guid Id { get; set;}

    /// <summary>
    /// Nombre del usuario.
    /// </summary>
    public string? Name { get; set;}

    /// <summary>
    /// Email del usuario.
    /// </summary>
    public string? Email { get; set;}

    /// <summary>
    /// Contraseña del usuario.
    /// </summary>
    public string? Password { get; set;}

    /// <summary>
    /// Indica si el usuario es administrador.
    /// </summary>
    public bool IsAdmin { get; set;}

    /// <summary>
    /// Fecha de creación del usuario.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Fecha de actualización del usuario.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

}

/// <summary>
/// Modelo de vista para la creación de un usuario.
/// </summary>
public class CreateUserViewModel : IValidatableObject
{
    /// <summary>
    /// Nombre del usuario.
    /// </summary>
    [Required(ErrorMessage = "El nombre es obligatorio")]
    public string? Name { get; set;}

    /// <summary>
    /// Email del usuario.
    /// </summary>
    [Required(ErrorMessage = "El email es obligatorio")]
    [EmailAddress(ErrorMessage = "El email no es válido")]
    public string? Email { get; set;}

    /// <summary>
    /// Contraseña del usuario.
    /// </summary>
    [Required(ErrorMessage = "La contraseña es obligatoria")]
    public string? Password { get; set;}

    /// <summary>
    /// Confirma la contraseña del usuario.
    /// </summary>
    [Required(ErrorMessage = "Por favor confirma tu contraseña")]
    public string? PasswordConfirm { get; set; }

    /// <summary>
    /// Valida que la contraseña y la confirmación de contraseña coincidan.
    /// </summary>
    /// <param name="validationContext"></param>
    /// <returns></returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Password != PasswordConfirm)
        {
          yield return new ValidationResult("Las contraseñas deben coincidir", [nameof(PasswordConfirm)]);
        }
    }
}

/// <summary>
/// Modelo de respuesta para la información del usuario.
/// </summary>
public class UserResponseDTO
{
    /// <summary>
    /// Identificador único del usuario.
    /// </summary>
    public Guid Id { get; set; }
    /// <summary>
    /// Nombre del usuario.
    /// </summary>
    public string? Name { get; set; }
    /// <summary>
    /// Email del usuario.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Indica si el usuario es administrador.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsAdmin { get; set; }

    /// <summary>
    /// Fecha de creación del usuario.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Fecha de actualización del usuario.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Modelo de vista para la actualización de un usuario.
/// </summary>
public class UpdateUserViewModel
{
    /// <summary>
    /// Nombre del usuario.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Email del usuario.
    /// </summary>
    [EmailAddress(ErrorMessage = "El email no es válido")]
    public string? Email { get; set; }

}