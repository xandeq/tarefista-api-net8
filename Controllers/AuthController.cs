using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using TarefistaApi.DTOs.Auth;
using Tarefista.Api.Services;

[ApiController]
[Route("api/Auth")]
public class AuthController : ControllerBase
{
    private readonly FirestoreDb _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;

    public AuthController(FirebaseService firebaseService, IConfiguration config, ILogger<AuthController> logger)
    {
        _db = firebaseService.GetFirestoreDb();
        _config = config;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto request)
    {
        try
        {
            _logger.LogInformation("Iniciando processo de registro...");

            // Hash da senha
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

            // Criar usuário no Firebase Authentication
            var userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(new UserRecordArgs
            {
                Email = request.Email,
                Password = request.Password,
                DisplayName = request.DisplayName
            });

            // Salvar no Firestore
            var userDoc = _db.Collection("users").Document(userRecord.Uid);
            await userDoc.SetAsync(new
            {
                email = request.Email,
                displayName = request.DisplayName,
                password = hashedPassword,
                createdAt = Timestamp.GetCurrentTimestamp()
            });

            return Created("", new { message = "User registered successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao registrar usuário");
            throw;
        }

    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto request)
    {
        try
        {
            var snapshot = await _db.Collection("users")
                                    .WhereEqualTo("email", request.Email)
                                    .GetSnapshotAsync();

            if (snapshot.Count == 0)
                return NotFound(new { message = "User not found" });

            var userData = snapshot.Documents[0].ToDictionary();
            string storedHash = userData["password"].ToString();

            if (!BCrypt.Net.BCrypt.Verify(request.Password, storedHash))
                return Unauthorized(new { message = "Invalid credentials" });

            // 🔹 Gerar JWT
            var key = Encoding.UTF8.GetBytes(_config["JWT:Secret"]);
            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim("userId", snapshot.Documents[0].Id)
                }),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature
                )
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return Ok(new
            {
                message = "Login successful",
                token = tokenHandler.WriteToken(token),
                userId = snapshot.Documents[0].Id, // ADD
                user = userData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao logar usuário");
            throw;
        }
    }

    [HttpPost("logout")]
    public IActionResult Logout([FromServices] TokenBlacklistService blacklistService)
    {
        try
        {
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                _logger.LogWarning("Logout sem Authorization header. Nada a invalidar.");
                // still OK: idempotente (cliente vai limpar sessão local)
                return Ok(new { message = "Logout successful" });
            }

            var token = authHeader.Replace("Bearer ", "");
            blacklistService.BlacklistToken(token);

            _logger.LogInformation("Token invalidado em {Time}", DateTime.UtcNow);
            return Ok(new { message = "Logout successful, token invalidated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no logout");
            return StatusCode(500, new
            {
                message = "Error logging out user",
                error = new { ex.Message, ex.StackTrace }
            });
        }
    }

    [HttpGet("userId")]
    public IActionResult GetUserId([FromServices] TokenBlacklistService tokenBlacklistService)
    {
        try
        {
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (authHeader == null || !authHeader.StartsWith("Bearer "))
                return Unauthorized(new { message = "Unauthorized" });

            var token = authHeader.Replace("Bearer ", "");
            var tokenHandler = new JwtSecurityTokenHandler();
            var secret = _config["JWT:Secret"];
            if (string.IsNullOrWhiteSpace(secret))
            {
                _logger.LogError("JWT:Secret ausente na configuração.");
                return StatusCode(500, new { message = "Server misconfiguration: JWT secret missing" });
            }
            var key = Encoding.UTF8.GetBytes(secret);

            try
            {
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2) // tolerância p/ pequenos desvios de relógio
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                var userId = jwtToken.Claims.First(x => x.Type == "userId").Value;

                if (string.IsNullOrEmpty(userId))
                    return BadRequest(new { message = "userId claim not found" });

                return Ok(new { userId });
            }
            catch (SecurityTokenExpiredException)
            {
                return Unauthorized(new { message = "Token expired" });
            }

        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = "Error getting userId",
                error = new
                {
                    ex.Message,
                    ex.StackTrace,
                    ex.Source
                }
            });
        }
    }

    [HttpGet("tempUserId")]
    public IActionResult GetTempUserId()
    {
        try
        {
            var tempUserId = Guid.NewGuid().ToString("N");
            _logger.LogInformation("TempUserId gerado: {TempUserId}", tempUserId);
            return Ok(new { tempUserId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao gerar tempUserId");
            return StatusCode(500, new { message = "Error generating tempUserId" });
        }
    }

}
