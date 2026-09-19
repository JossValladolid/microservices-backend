using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AuthService.Models;
using Npgsql;

namespace AuthService.Controllers
{
    
    /// <summary>
    /// Controlador para gestionar usuarios.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
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

        // GET: api/User

        /// <summary>
        /// Obtiene todos los usuarios de la base de datos.
        /// </summary>
        /// <returns>Lista de usuarios.</returns>
        /// <response code="200">Devuelve la lista de usuarios.</response>
        /// <response code="404">No se encontraron usuarios.</response>
        /// <response code="500">Error interno del servidor.</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserResponseDTO>>> GetUsers()
        {
            var usr = await _context.Users.OrderByDescending(user => user.CreatedAt).Select(user => new UserResponseDTO
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                IsAdmin = user.IsAdmin,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt

            }).ToListAsync();

            if(!usr.Count.Equals(0))
            {
                return Ok(usr);
            }
            else
            {
                return NotFound();
            }
        }

        /// <summary>
        /// Otorga privilegios de administrador a un usuario.
        /// </summary>
        /// <param name="id">El ID del usuario a actualizar.</param>
        /// <returns>Usuario actualizado con rol de administrador.</returns>
        /// <response code="200">Devuelve el usuario actualizado.</response>
        /// <response code="404">No se encontró el usuario.</response>
        /// <response code="500">Error interno del servidor.</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [HttpPatch("AddAdmin/{id}")]
        public async Task<ActionResult<UserResponseDTO>> AddAdmin(Guid id)
        {
            var existingUser = await _context.Users.FindAsync(id);
            if (existingUser == null)
            {
                return NotFound();
            }

            // Actualizar a Admin
            existingUser.IsAdmin = true;
            existingUser.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UserExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return new UserResponseDTO
            {
                Id = existingUser.Id,
                Name = existingUser.Name,
                Email = existingUser.Email,
                IsAdmin = existingUser.IsAdmin,
                UpdatedAt = existingUser.UpdatedAt
            };
        }

        /// <summary>
        /// Revoca los privilegios de administrador de un usuario.
        /// </summary>
        /// <param name="id">El ID del usuario a actualizar.</param>
        /// <returns>Usuario actualizado sin rol de administrador.</returns>
        /// <response code="200">Devuelve el usuario actualizado.</response>
        /// <response code="404">No se encontró el usuario.</response>
        /// <response code="500">Error interno del servidor.</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [HttpPatch("RevokeAdmin/{id}")]
        public async Task<ActionResult<UserResponseDTO>> RevokeAdmin(Guid id)
        {
            var existingUser = await _context.Users.FindAsync(id);
            if (existingUser == null)
            {
                return NotFound();
            }

            // Quitar de Admin
            existingUser.IsAdmin = false;
            existingUser.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UserExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return new UserResponseDTO
            {
                Id = existingUser.Id,
                Name = existingUser.Name,
                Email = existingUser.Email,
                IsAdmin = existingUser.IsAdmin,
                UpdatedAt = existingUser.UpdatedAt
            };
        }

        // GET: api/User/5
        /// <summary>
        /// Obtiene un usuario específico por su ID.
        /// </summary>
        /// <param name="id">El identificador único (GUID) del usuario.</param>
        /// <returns>El usuario solicitado en formato DTO.</returns>
        /// <response code="200">Devuelve el usuario encontrado.</response>
        /// <response code="404">No se encontró el usuario con el ID proporcionado.</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [HttpGet("{id}")]
        public async Task<ActionResult<UserResponseDTO>> GetUser(Guid id)
        {
            var user = await _context.Users.FindAsync(id);

            if (user == null)
            {
                return NotFound();
            }

            return new UserResponseDTO
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                IsAdmin = user.IsAdmin
            };
        }

        // PUT: api/User/5
        /// <summary>
        /// Actualiza la información completa de un usuario existente.
        /// </summary>
        /// <param name="id">El ID del usuario a actualizar.</param>
        /// <param name="updateUserViewModel">Objeto con los nuevos datos del usuario.</param>
        /// <returns>No devuelve contenido si la actualización fue exitosa.</returns>
        /// <response code="204">Usuario actualizado correctamente.</response>
        /// <response code="400">El ID de la ruta no coincide con el ID del cuerpo de la petición.</response>
        /// <response code="404">No se encontró el usuario a actualizar.</response>
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [HttpPut("{id}")]
        public async Task<IActionResult> PutUser(Guid id, [FromBody] UpdateUserViewModel updateUserViewModel)
        {
            var existingUser = await _context.Users.FindAsync(id);

            if (existingUser == null)
            {
                return NotFound();
            }

            existingUser.Name = updateUserViewModel.Name;
            existingUser.Email = updateUserViewModel.Email;
            existingUser.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueEmailViolation(ex))
            {
                return Conflict(new { message = "Ya existe un usuario con ese email" });
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UserExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }
        

        // POST: api/User
        /// <summary>
        /// Crea un nuevo usuario en el sistema.
        /// </summary>
        /// <param name="createUserViewModel">Modelo con los datos necesarios para registrar al usuario.</param>
        /// <returns>El usuario recién creado.</returns>
        /// <response code="201">Devuelve el nuevo usuario creado.</response>
        /// <response code="400">Los datos enviados no son válidos (ej. contraseñas no coinciden).</response>
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [HttpPost]
        public async Task<ActionResult<UserResponseDTO>> PostUser(CreateUserViewModel createUserViewModel)
        {
            var hashed = new PasswordHasher<User>();
            var newUser = new User();

            string hashedPassword = hashed.HashPassword(newUser, createUserViewModel.Password!);

            newUser.Name = createUserViewModel.Name;
            newUser.Email = createUserViewModel.Email;
            newUser.Password = hashedPassword;
            newUser.IsAdmin = false;

            _context.Users.Add(newUser);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueEmailViolation(ex))
            {
                return Conflict(new { message = "Ya existe un usuario con ese email" });
            }

            var responseUser = new UserResponseDTO
            {
                Id = newUser.Id,
                Name = newUser.Name,
                Email = newUser.Email,
                CreatedAt = newUser.CreatedAt,
                UpdatedAt = newUser.UpdatedAt            
            };

            return CreatedAtAction("GetUser", new { id = newUser.Id }, responseUser);
        }

        // DELETE: api/User/5
        /// <summary>
        /// Elimina un usuario de la base de datos.
        /// </summary>
        /// <param name="id">El ID del usuario a eliminar.</param>
        /// <returns>No devuelve contenido si la eliminación fue exitosa.</returns>
        /// <response code="204">Usuario eliminado correctamente.</response>
        /// <response code="404">No se encontró el usuario a eliminar.</response>
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool UserExists(Guid id)
        {
            return _context.Users.Any(e => e.Id == id);
        }

        private static bool IsUniqueEmailViolation(DbUpdateException ex)
        {
            return ex.InnerException is PostgresException pgEx
                && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
        }
    }
}