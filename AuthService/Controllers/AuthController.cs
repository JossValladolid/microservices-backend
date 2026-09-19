using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AuthService.Models;

namespace AuthService.Controllers
{
    /// <summary>
    /// Controlador para autenticación de usuarios.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AuthServiceContext _context;

        /// <summary>
        /// Constructor del controlador de autenticación.
        /// </summary>
        /// <param name="context">Contexto de la base de datos.</param>
        public AuthController(AuthServiceContext context)
        {
            _context = context;
        }

        // POST: api/Auth/login
        /// <summary>
        /// Verifica las credenciales de un usuario e inicia sesión.
        /// </summary>
        /// <param name="loginViewModel">Credenciales del usuario.</param>
        /// <returns>Los datos del usuario autenticado.</returns>
        /// <response code="200">Credenciales válidas.</response>
        /// <response code="401">Credenciales inválidas.</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [HttpPost("login")]
        public async Task<ActionResult<UserResponseDTO>> Login(LoginViewModel loginViewModel)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == loginViewModel.Email);

            if (user == null)
            {
                return Unauthorized(new { message = "Credenciales inválidas" });
            }

            var hasher = new PasswordHasher<User>();
            var result = hasher.VerifyHashedPassword(user, user.Password!, loginViewModel.Password!);

            if (result == PasswordVerificationResult.Failed)
            {
                return Unauthorized(new { message = "Credenciales inválidas" });
            }

            return Ok(new UserResponseDTO
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                IsAdmin = user.IsAdmin
            });
        }
    }
}
