using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AuthService.Models;
using AuthService.Services;

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
        private readonly TokenService _tokenService;

        /// <summary>
        /// Constructor del controlador de autenticación.
        /// </summary>
        /// <param name="context">Contexto de la base de datos.</param>
        /// <param name="tokenService">Servicio que genera los tokens.</param>
        public AuthController(AuthServiceContext context, TokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }

        // POST: api/Auth/login
        /// <summary>
        /// Verifica las credenciales e inicia sesión. Los tokens se mandan como cookies HttpOnly.
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
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == loginViewModel.Email);

            if (user == null || !BCrypt.Net.BCrypt.Verify(loginViewModel.Password!, user.Password!))
            {
                return Unauthorized(new { message = "Credenciales inválidas" });
            }

            await _context.Sessions
                .Where(s => s.UserId == user.Id && s.ExpiryDate <= DateTime.UtcNow)
                .ExecuteDeleteAsync();

            var session = new Session { UserId = user.Id, User = user };
            _context.Sessions.Add(session);

            return await IssueTokens(session);
        }

        // POST: api/Auth/refresh
        /// <summary>
        /// Usa la cookie del refresh token para emitir un nuevo par de tokens (el refresh token anterior deja de servir).
        /// </summary>
        /// <returns>Los datos del usuario.</returns>
        /// <response code="200">Tokens renovados.</response>
        /// <response code="401">Refresh token inválido o expirado.</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [HttpPost("refresh")]
        public async Task<ActionResult<UserResponseDTO>> Refresh()
        {
            var refreshToken = Request.Cookies[TokenService.RefreshTokenCookie];

            var session = refreshToken == null ? null : await _context.Sessions
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.RefreshToken == refreshToken);

            if (session == null || session.ExpiryDate <= DateTime.UtcNow)
            {
                if (session != null)
                {
                    _context.Sessions.Remove(session);
                    await _context.SaveChangesAsync();
                }
                DeleteTokenCookies();
                return Unauthorized(new { message = "Sesión inválida o expirada. Inicia sesión nuevamente." });
            }

            return await IssueTokens(session);
        }

        // POST: api/Auth/logout
        /// <summary>
        /// Cierra la sesión: elimina el refresh token de la BD y borra las cookies.
        /// </summary>
        /// <response code="204">Sesión cerrada (o ya no existía).</response>
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var refreshToken = Request.Cookies[TokenService.RefreshTokenCookie];
            if (refreshToken != null)
            {
                await _context.Sessions
                    .Where(s => s.RefreshToken == refreshToken)
                    .ExecuteDeleteAsync();
            }

            DeleteTokenCookies();
            return NoContent();
        }

        // GET: api/Auth/me
        /// <summary>
        /// Devuelve el usuario de la sesión actual (el frontend no puede leer las cookies HttpOnly).
        /// </summary>
        /// <returns>Los datos del usuario autenticado.</returns>
        /// <response code="200">Usuario autenticado.</response>
        /// <response code="401">No hay sesión o el access token expiró.</response>
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [Authorize]
        [HttpGet("me")]
        public async Task<ActionResult<UserResponseDTO>> Me()
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Unauthorized();
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return Unauthorized();
            }

            return ToDto(user);
        }

        /// <summary>
        /// Asigna un refresh token nuevo a la sesión (rotación), la guarda y manda ambos tokens como cookies.
        /// </summary>
        private async Task<UserResponseDTO> IssueTokens(Session session)
        {
            var user = session.User!;

            session.RefreshToken = TokenService.GenerateRefreshToken();
            session.ExpiryDate = DateTime.UtcNow.Add(_tokenService.RefreshTokenLifetime);
            await _context.SaveChangesAsync();

            Response.Cookies.Append(TokenService.AccessTokenCookie, _tokenService.GenerateAccessToken(user),
                CookieOptions("/api", _tokenService.AccessTokenLifetime));
            // El refresh token solo se manda a /api/Auth (refresh y logout), no a todas las peticiones
            Response.Cookies.Append(TokenService.RefreshTokenCookie, session.RefreshToken,
                CookieOptions("/api/Auth", _tokenService.RefreshTokenLifetime));

            return ToDto(user);
        }

        private void DeleteTokenCookies()
        {
            Response.Cookies.Delete(TokenService.AccessTokenCookie, CookieOptions("/api", TimeSpan.Zero));
            Response.Cookies.Delete(TokenService.RefreshTokenCookie, CookieOptions("/api/Auth", TimeSpan.Zero));
        }

        // HttpOnly: JavaScript no puede leerla. SameSite=Strict: no se manda desde otros sitios (protege de CSRF).
        private static CookieOptions CookieOptions(string path, TimeSpan lifetime) => new()
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = path,
            MaxAge = lifetime
        };

        private static UserResponseDTO ToDto(User user) => new()
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            BirthDate = user.BirthDate,
            IsAdmin = user.IsAdmin
        };
    }
}
