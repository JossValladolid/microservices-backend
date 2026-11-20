using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AuthService.Filters;
using AuthService.Models;
using Npgsql;

namespace AuthService.Controllers
{
    /// <summary>
    /// Controlador para gestionar usuarios (módulo de usuarios, solo administradores salvo el autoregistro).
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [ServiceFilter(typeof(ActiveUserFilter))]
    public class UserController : ControllerBase
    {
        private readonly AuthServiceContext _context;

        /// <summary>
        /// Constructor del controlador de usuarios.
        /// </summary>
        /// <param name="context">Contexto de la base de datos.</param>
        public UserController(AuthServiceContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId =>
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

        // GET: api/User?page=1&pageSize=10&name=jo&email=&birthDateFrom=&birthDateTo=&isAdmin=&hasRefreshToken=
        /// <summary>
        /// Consulta paginada de usuarios. Los filtros de texto se buscan como sub-cadenas sin distinguir mayúsculas.
        /// </summary>
        /// <param name="query">Filtros y paginación.</param>
        /// <returns>Página de usuarios.</returns>
        /// <response code="200">Devuelve la página de usuarios (puede venir vacía).</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<ActionResult<PagedResult<UserResponseDTO>>> GetUsers([FromQuery] UserQuery query)
        {
            var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
            var now = DateTime.UtcNow;
            var users = _context.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.Name))
            {
                var pattern = Paging.ContainsPattern(query.Name);
                users = users.Where(u => EF.Functions.ILike(u.Name, pattern) || EF.Functions.ILike(u.UserName, pattern));
            }

            if (!string.IsNullOrWhiteSpace(query.Email))
            {
                var pattern = Paging.ContainsPattern(query.Email);
                users = users.Where(u => EF.Functions.ILike(u.Email, pattern));
            }

            if (query.BirthDateFrom is { } from)
            {
                users = users.Where(u => u.BirthDate >= from);
            }

            if (query.BirthDateTo is { } to)
            {
                users = users.Where(u => u.BirthDate <= to);
            }

            if (query.IsAdmin is { } isAdmin)
            {
                users = users.Where(u => u.IsAdmin == isAdmin);
            }

            if (query.HasRefreshToken is { } hasToken)
            {
                users = hasToken
                    ? users.Where(u => u.Sessions.Any(s => s.ExpiryDate > now))
                    : users.Where(u => !u.Sessions.Any(s => s.ExpiryDate > now));
            }

            var total = await users.CountAsync();
            var items = await users
                .OrderByDescending(u => u.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ToAdminDto(now))
                .ToListAsync();

            return new PagedResult<UserResponseDTO> { Items = items, Total = total, Page = page, PageSize = pageSize };
        }

        /// <summary>
        /// Obtiene un usuario específico por su ID.
        /// </summary>
        /// <param name="id">El identificador único (GUID) del usuario.</param>
        /// <returns>El usuario solicitado en formato DTO.</returns>
        /// <response code="200">Devuelve el usuario encontrado.</response>
        /// <response code="404">No se encontró el usuario con el ID proporcionado.</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [Authorize(Roles = "Admin")]
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<UserResponseDTO>> GetUser(Guid id)
        {
            var user = await _context.Users
                .Where(u => u.Id == id)
                .Select(ToAdminDto(DateTime.UtcNow))
                .FirstOrDefaultAsync();

            return user is null ? NotFound() : user;
        }

        // POST: api/User
        /// <summary>
        /// Crea un usuario. Sin sesión de administrador es un autoregistro: queda desactivado y como usuario común.
        /// Si lo crea un administrador, se respeta el rol indicado y el usuario queda activo.
        /// </summary>
        /// <param name="createUserViewModel">Modelo con los datos necesarios para registrar al usuario.</param>
        /// <returns>El usuario recién creado.</returns>
        /// <response code="201">Devuelve el nuevo usuario creado.</response>
        /// <response code="400">Los datos enviados no son válidos (ej. contraseñas no coinciden).</response>
        /// <response code="409">Ya existe un usuario con ese correo o nombre de usuario.</response>
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [AllowAnonymous]
        [HttpPost]
        public async Task<ActionResult<UserResponseDTO>> PostUser(CreateUserViewModel createUserViewModel)
        {
            // El rol del token podría estar desactualizado: se confirma contra la base de datos.
            var createdByAdmin = User.IsInRole("Admin")
                && await _context.Users.AnyAsync(u => u.Id == CurrentUserId && u.IsAdmin && u.IsActive);

            var newUser = new User
            {
                Name = createUserViewModel.Name,
                UserName = UserNames.Normalize(createUserViewModel.UserName),
                Email = createUserViewModel.Email,
                Password = BCrypt.Net.BCrypt.HashPassword(createUserViewModel.Password!),
                BirthDate = createUserViewModel.BirthDate!.Value,
                IsAdmin = createdByAdmin && createUserViewModel.IsAdmin,
                IsActive = createdByAdmin
            };

            _context.Users.Add(newUser);

            if (newUser.IsActive)
            {
                _context.Folders.Add(new Folder { Owner = newUser, Name = newUser.UserName });
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Conflict(new { message = UniqueViolationMessage(ex) });
            }

            return CreatedAtAction(nameof(GetUser), new { id = newUser.Id }, ToDto(newUser));
        }

        // PUT: api/User/5
        /// <summary>
        /// Edita nombre, nombre de usuario, correo, fecha de nacimiento, contraseña (opcional) y rol de un usuario.
        /// Si se cambia la contraseña se revoca su Refresh Token. Un administrador no puede quitarse su propio rol.
        /// </summary>
        /// <param name="id">El ID del usuario a actualizar.</param>
        /// <param name="updateUserViewModel">Objeto con los nuevos datos del usuario.</param>
        /// <returns>El usuario actualizado.</returns>
        /// <response code="200">Usuario actualizado correctamente.</response>
        /// <response code="400">Datos inválidos o intento de quitarse el propio rol de administrador.</response>
        /// <response code="404">No se encontró el usuario a actualizar.</response>
        /// <response code="409">Ya existe un usuario con ese correo o nombre de usuario.</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [Authorize(Roles = "Admin")]
        [HttpPut("{id:guid}")]
        public async Task<ActionResult<UserResponseDTO>> PutUser(Guid id, [FromBody] UpdateUserViewModel updateUserViewModel)
        {
            var existingUser = await _context.Users.FindAsync(id);
            if (existingUser == null)
            {
                return NotFound();
            }

            if (id == CurrentUserId && existingUser.IsAdmin && !updateUserViewModel.IsAdmin)
            {
                return BadRequest(new { message = "No puedes quitarte tu propio rol de administrador." });
            }

            var newUserName = UserNames.Normalize(updateUserViewModel.UserName);
            if (newUserName != existingUser.UserName)
            {
                if (!UserNames.IsValid(newUserName))
                {
                    return BadRequest(new { message = "El nombre de usuario debe tener de 3 a 30 caracteres (a-z, 0-9, - y _) y no ser reservado" });
                }

                // La carpeta home lleva el nombre de usuario.
                var home = await _context.Folders.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(f => f.OwnerId == id && f.ParentId == null);
                if (home is not null)
                {
                    home.Name = newUserName;
                    home.UpdatedAt = DateTime.UtcNow;
                }
            }

            existingUser.Name = updateUserViewModel.Name!.Trim();
            existingUser.UserName = newUserName;
            existingUser.Email = updateUserViewModel.Email!.Trim();
            existingUser.BirthDate = updateUserViewModel.BirthDate!.Value;
            existingUser.IsAdmin = updateUserViewModel.IsAdmin;
            existingUser.UpdatedAt = DateTime.UtcNow;

            var passwordChanged = !string.IsNullOrEmpty(updateUserViewModel.Password);
            if (passwordChanged)
            {
                existingUser.Password = BCrypt.Net.BCrypt.HashPassword(updateUserViewModel.Password);
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Conflict(new { message = UniqueViolationMessage(ex) });
            }

            if (passwordChanged)
            {
                await RevokeSessions(id);
            }

            return await GetUser(id);
        }

        /// <summary>
        /// Otorga privilegios de administrador a un usuario.
        /// </summary>
        /// <param name="id">El ID del usuario a actualizar.</param>
        /// <returns>Usuario actualizado con rol de administrador.</returns>
        /// <response code="200">Devuelve el usuario actualizado.</response>
        /// <response code="404">No se encontró el usuario.</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [Authorize(Roles = "Admin")]
        [HttpPatch("AddAdmin/{id:guid}")]
        public async Task<ActionResult<UserResponseDTO>> AddAdmin(Guid id)
        {
            var existingUser = await _context.Users.FindAsync(id);
            if (existingUser == null)
            {
                return NotFound();
            }

            existingUser.IsAdmin = true;
            existingUser.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return ToDto(existingUser);
        }

        /// <summary>
        /// Revoca los privilegios de administrador de un usuario. Un administrador no puede revocarse a sí mismo.
        /// </summary>
        /// <param name="id">El ID del usuario a actualizar.</param>
        /// <returns>Usuario actualizado sin rol de administrador.</returns>
        /// <response code="200">Devuelve el usuario actualizado.</response>
        /// <response code="400">Intento de quitarse el propio rol.</response>
        /// <response code="404">No se encontró el usuario.</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [Authorize(Roles = "Admin")]
        [HttpPatch("RevokeAdmin/{id:guid}")]
        public async Task<ActionResult<UserResponseDTO>> RevokeAdmin(Guid id)
        {
            if (id == CurrentUserId)
            {
                return BadRequest(new { message = "No puedes quitarte tu propio rol de administrador." });
            }

            var existingUser = await _context.Users.FindAsync(id);
            if (existingUser == null)
            {
                return NotFound();
            }

            existingUser.IsAdmin = false;
            existingUser.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return ToDto(existingUser);
        }

        /// <summary>
        /// Activa un usuario específico por su ID (y le crea su carpeta home si no la tiene).
        /// </summary>
        /// <param name="id">El ID del usuario a activar.</param>
        /// <returns>Usuario actualizado.</returns>
        /// <response code="200">Devuelve el usuario actualizado.</response>
        /// <response code="404">No se encontró el usuario.</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [Authorize(Roles = "Admin")]
        [HttpPatch("Activate/{id:guid}")]
        public async Task<ActionResult<UserResponseDTO>> ActivateUser(Guid id)
        {
            var existingUser = await _context.Users.FindAsync(id);
            if (existingUser == null)
            {
                return NotFound();
            }

            existingUser.IsActive = true;
            existingUser.UpdatedAt = DateTime.UtcNow;

            if (!await _context.Folders.IgnoreQueryFilters().AnyAsync(f => f.OwnerId == id && f.ParentId == null))
            {
                _context.Folders.Add(new Folder { OwnerId = id, Name = existingUser.UserName });
            }

            await _context.SaveChangesAsync();
            return ToDto(existingUser);
        }

        /// <summary>
        /// Desactiva un usuario y revoca su Refresh Token. Un administrador no puede desactivarse a sí mismo.
        /// </summary>
        /// <param name="id">El ID del usuario a desactivar.</param>
        /// <returns>Usuario desactivado.</returns>
        /// <response code="200">Devuelve el usuario desactivado.</response>
        /// <response code="400">Intento de desactivarse a sí mismo.</response>
        /// <response code="404">No se encontró el usuario.</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [Authorize(Roles = "Admin")]
        [HttpPatch("Deactivate/{id:guid}")]
        public async Task<ActionResult<UserResponseDTO>> DeactivateUser(Guid id)
        {
            if (id == CurrentUserId)
            {
                return BadRequest(new { message = "No puedes desactivarte a ti mismo." });
            }

            var existingUser = await _context.Users.FindAsync(id);
            if (existingUser == null)
            {
                return NotFound();
            }

            existingUser.IsActive = false;
            existingUser.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await RevokeSessions(id);

            return ToDto(existingUser);
        }

        // DELETE: api/User/5/refresh-token
        /// <summary>
        /// Revoca el Refresh Token de un usuario. Un administrador no puede revocar el suyo.
        /// </summary>
        /// <param name="id">El ID del usuario.</param>
        /// <response code="204">Refresh Token revocado (o el usuario no tenía).</response>
        /// <response code="400">Intento de revocar el propio Refresh Token.</response>
        /// <response code="404">No se encontró el usuario.</response>
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id:guid}/refresh-token")]
        public async Task<IActionResult> RevokeRefreshToken(Guid id)
        {
            if (id == CurrentUserId)
            {
                return BadRequest(new { message = "No puedes revocar tu propio Refresh Token." });
            }

            if (!await _context.Users.AnyAsync(u => u.Id == id))
            {
                return NotFound();
            }

            await RevokeSessions(id);
            return NoContent();
        }

        // DELETE: api/User/5
        /// <summary>
        /// Elimina un usuario de la base de datos. Un administrador no puede eliminarse a sí mismo.
        /// </summary>
        /// <param name="id">El ID del usuario a eliminar.</param>
        /// <returns>No devuelve contenido si la eliminación fue exitosa.</returns>
        /// <response code="204">Usuario eliminado correctamente.</response>
        /// <response code="400">Intento de eliminarse a sí mismo.</response>
        /// <response code="404">No se encontró el usuario a eliminar.</response>
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            if (id == CurrentUserId)
            {
                return BadRequest(new { message = "No puedes eliminarte a ti mismo." });
            }

            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private Task<int> RevokeSessions(Guid userId) =>
            _context.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync();

        private static Expression<Func<User, UserResponseDTO>> ToAdminDto(DateTime now) => user => new UserResponseDTO
        {
            Id = user.Id,
            Name = user.Name,
            UserName = user.UserName,
            Email = user.Email,
            BirthDate = user.BirthDate,
            IsAdmin = user.IsAdmin,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            HasRefreshToken = user.Sessions.Any(s => s.ExpiryDate > now)
        };

        private static UserResponseDTO ToDto(User user) => new()
        {
            Id = user.Id,
            Name = user.Name,
            UserName = user.UserName,
            Email = user.Email,
            BirthDate = user.BirthDate,
            IsAdmin = user.IsAdmin,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };

        private static bool IsUniqueViolation(DbUpdateException ex)
        {
            return ex.InnerException is PostgresException pgEx
                && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
        }

        private static string UniqueViolationMessage(DbUpdateException ex)
        {
            return ex.InnerException is PostgresException { ConstraintName: var constraint } && constraint?.Contains("UserName") == true
                ? "Ya existe un usuario con ese nombre de usuario"
                : "Ya existe un usuario con ese email";
        }
    }
}
