using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using isp_report_api.Data;
using isp_report_api.Models;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MimeKit;

namespace isp_report_api.Services;

public class AuthService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;
    private readonly AppDbContext _db;

    public AuthService(IConfiguration configuration, ILogger<AuthService> logger, AppDbContext db)
    {
        _configuration = configuration;
        _logger = logger;
        _db = db;
    }

    public bool IsValidKtrnEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;
        return email.Trim().ToLowerInvariant().EndsWith("@ktrn.rw");
    }

    public string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(128 / 8);
        string hashed = Convert.ToBase64String(
            KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: 100000,
                numBytesRequested: 256 / 8
            )
        );
        return $"{Convert.ToBase64String(salt)}.{hashed}";
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 2)
            return false;
        var salt = Convert.FromBase64String(parts[0]);
        var storedPasswordHash = parts[1];
        string hashed = Convert.ToBase64String(
            KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: 100000,
                numBytesRequested: 256 / 8
            )
        );
        return storedPasswordHash == hashed;
    }

    public async Task<(User? User, string? Error)> RegisterAsync(
        string email,
        string password,
        string? name
    )
    {
        var normalizedEmail = email.ToLowerInvariant();
        var existingUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (existingUser != null)
            return (null, "An account with this email already exists.");

        var user = new User
        {
            Email = normalizedEmail,
            Name = name ?? normalizedEmail.Split('@')[0],
            PasswordHash = HashPassword(password),
            EmailVerified = false,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return (user, null);
    }

    public async Task<(User? User, string? Error, bool NeedsVerification)> LoginAsync(
        string email,
        string password
    )
    {
        var normalizedEmail = email.ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (user == null)
            return (null, "Invalid email or password.", false);
        if (!user.IsActive)
            return (null, "This account has been deactivated.", false);
        if (string.IsNullOrEmpty(user.PasswordHash) || !VerifyPassword(password, user.PasswordHash))
            return (null, "Invalid email or password.", false);
        if (!user.EmailVerified)
            return (user, null, true);

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (user, null, false);
    }

    public string GenerateOtpCode()
    {
        var random = new Random();
        return random.Next(100000, 999999).ToString();
    }

    public async Task<OtpCode> CreateVerificationOtpAsync(string email)
    {
        var existingOtps = await _db
            .OtpCodes.Where(o => o.Email == email.ToLowerInvariant() && !o.IsUsed)
            .ToListAsync();
        foreach (var otp in existingOtps)
            otp.IsUsed = true;

        var otpCode = new OtpCode
        {
            Email = email.ToLowerInvariant(),
            Code = GenerateOtpCode(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            IsUsed = false,
        };
        _db.OtpCodes.Add(otpCode);
        await _db.SaveChangesAsync();
        return otpCode;
    }

    public async Task SendVerificationEmailAsync(string email, string otpCode)
    {
        var host = _configuration["Smtp:Host"];
        var port = int.Parse(_configuration["Smtp:Port"] ?? "587");
        var username = _configuration["Smtp:Username"];
        var password = _configuration["Smtp:Password"];
        var enableSsl = bool.Parse(_configuration["Smtp:EnableSsl"] ?? "true");
        var fromEmail = _configuration["Smtp:FromEmail"];

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(fromEmail))
            return;

        using var client = new SmtpClient();
        await client.ConnectAsync(
            host,
            port,
            enableSsl
                ? MailKit.Security.SecureSocketOptions.StartTls
                : MailKit.Security.SecureSocketOptions.None
        );
        if (!string.IsNullOrEmpty(username))
            await client.AuthenticateAsync(username, password);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("KTRN ISP Report API", fromEmail));
        message.To.Add(new MailboxAddress("", email));
        message.Subject = "Verify Your Email";
        message.Body = new BodyBuilder
        {
            HtmlBody = $"Your verification code is: {otpCode}",
        }.ToMessageBody();

        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    public async Task<(bool Success, string? Error)> VerifyEmailAsync(string email, string code)
    {
        var normalizedEmail = email.ToLowerInvariant();
        var otpRecord = await _db
            .OtpCodes.Where(o =>
                o.Email == normalizedEmail
                && o.Code == code
                && !o.IsUsed
                && o.ExpiresAt > DateTime.UtcNow
            )
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otpRecord == null)
            return (false, "Invalid or expired verification code.");
        otpRecord.IsUsed = true;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (user == null)
            return (false, "User not found.");

        user.EmailVerified = true;
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant());
    }

    public string GenerateJwtToken(User user)
    {
        var jwtSecret = _configuration["Jwt:Secret"] ?? "KtrnIspReportApiDefaultSecretKey2026!@#";
        var jwtIssuer = _configuration["Jwt:Issuer"] ?? "isp-report-api";
        var jwtAudience = _configuration["Jwt:Audience"] ?? "isp-report-api-clients";
        var expiryHours = int.Parse(_configuration["Jwt:ExpiryHours"] ?? "24");

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.Name ?? user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expiryHours),
            signingCredentials: credentials
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
