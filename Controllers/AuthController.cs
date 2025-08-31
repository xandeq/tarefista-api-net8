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
    public IActionResult Logout()
    {
        // logout é client-side no JWT
        return Ok(new { message = "Logout successful" });
    }

    [HttpGet("userId")]
    public IActionResult GetUserId()
    {
        try
        {
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (authHeader == null || !authHeader.StartsWith("Bearer "))
                return Unauthorized(new { message = "Unauthorized" });

            var token = authHeader.Replace("Bearer ", "");
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_config["JWT_SECRET"]);

            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateLifetime = true
            }, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;
            var userId = jwtToken.Claims.First(x => x.Type == "userId").Value;

            return Ok(new { userId });
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
}
