using System.Security.Claims;
using System.Text;
using isp_report_api.Data;
using isp_report_api.Models;
using isp_report_api.Repository;
using isp_report_api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// ADD Swagger
builder.Services.AddSwaggerGen();

// Database Context
var dbConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySQL(dbConnectionString ??
                     throw new InvalidOperationException("Connection string 'DefaultConnection' not found.")));

builder.Services.AddScoped<AdAuthService>();
builder.Services.AddScoped<AuthService>();

builder.Services.AddSingleton<IOracleConnectionFactory, OracleConnectionFactory>();
builder.Services.AddScoped<IOracleRepository, OracleRepository>();
builder.Services.AddScoped<IIspReportRepository, IspReportRepository>();
builder.Services.AddScoped<IIspReportService, IspReportService>();


// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "KtrnIspReportApiDefaultSecretKey2026!@#";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "isp-report-api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "isp-report-api-clients";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();

app.UseSwaggerUI(options => { options.SwaggerEndpoint("/swagger/v1/swagger.json", "ISP REPORTS API"); });

app.MapControllers();

app.MapGet("/", () => Results.Json(new
{
    status = "welcome",
    timestamp = DateTime.UtcNow
})).WithName("homepage");

app.MapGet("/health", () => Results.Json(new
    {
        status = "healthy",
        timestamp = DateTime.UtcNow
    }))
    .WithName("HealthCheck");

app.MapGet(
        "/health/oracle",
        async (IOracleConnectionFactory connectionFactory) =>
        {
            try
            {
                using var connection = connectionFactory.CreateConnection();
                connection.Open();
                return Results.Json(
                    new
                    {
                        status = "connected",
                        database = "Oracle",
                        timestamp = DateTime.UtcNow,
                    }
                );
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new
                    {
                        status = "failed",
                        database = "Oracle",
                        error = ex.Message,
                        timestamp = DateTime.UtcNow,
                    },
                    statusCode: 500
                );
            }
        }
    )
    .WithName("OracleHealthCheck");

// Auth endpoints
app.MapPost("/auth/register", async ([FromBody] RegisterRequest request, AuthService authService) =>
{
    if (!authService.IsValidKtrnEmail(request.Email))
    {
        return Results.BadRequest(new { Error = "Only @ktrn.rw email addresses are allowed." });
    }

    try
    {
        var (user, error) = await authService.RegisterAsync(request.Email, request.Password, request.Name);
        if (error != null) return Results.BadRequest(new { Error = error });

        var otpCode = await authService.CreateVerificationOtpAsync(request.Email);
        await authService.SendVerificationEmailAsync(request.Email, otpCode.Code);

        return Results.Ok(new
            { Message = "Registration successful. Please check your email to verify.", NeedsVerification = true });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Registration failed: {ex.Message}");
    }
});

app.MapPost("/auth/login", async ([FromBody] LoginRequest request, AuthService authService) =>
{
    try
    {
        var (user, error, needsVerification) = await authService.LoginAsync(request.Email, request.Password);
        if (error != null) return Results.BadRequest(new { Error = error });

        if (needsVerification)
        {
            var otpCode = await authService.CreateVerificationOtpAsync(request.Email);
            await authService.SendVerificationEmailAsync(request.Email, otpCode.Code);
            return Results.Ok(new { Message = "Email not verified. Code sent to email.", NeedsVerification = true });
        }

        var token = authService.GenerateJwtToken(user!);
        return Results.Ok(new { Token = token, User = new { user!.Id, user.Email, user.Name } });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Login failed: {ex.Message}");
    }
});

app.MapPost("/auth/verify-email", async ([FromBody] VerifyEmailRequest request, AuthService authService) =>
{
    try
    {
        var (success, error) = await authService.VerifyEmailAsync(request.Email, request.Code);
        if (!success) return Results.BadRequest(new { Error = error });

        var user = await authService.GetUserByEmailAsync(request.Email);
        var token = authService.GenerateJwtToken(user!);
        return Results.Ok(new
        {
            Message = "Email verified successfully.", Token = token, User = new { user!.Id, user.Email, user.Name }
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Verification failed: {ex.Message}");
    }
});

app.MapPost("/auth/ad-login", async ([FromBody] AdLoginRequest request, AdAuthService adAuthService) =>

    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { Error = "Username and password are required." });
        }

        try
        {
            // Authenticate against Active Directory
            var adResult = await adAuthService.AuthenticateAsync(request.Username, request.Password);

            if (!adResult.Success)
            {
                return Results.BadRequest(new { Error = adResult.ErrorMessage });
            }

            // Get or create local user for the AD user
            var user = await adAuthService.GetOrCreateUserAsync(adResult);

            // Generate JWT token
            var token = adAuthService.GenerateJwtToken(user);

            return Results.Ok(new
            {
                Token = token,
                User = new
                {
                    user.Id,
                    user.Email,
                    user.Name
                },
                AuthMethod = "ActiveDirectory"
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"AD Login failed: {ex.Message}");
        }
    })
    .WithName("AdLogin");

app.MapGet("/auth/me", async (HttpContext context, AppDbContext db) =>
    {
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)
                          ?? context.User.FindFirst(JwtRegisteredClaimNames.Sub);

        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Results.Unauthorized();
        }

        var user = await db.Users.FindAsync(userId);
        if (user == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            user.Id,
            user.Email,
            user.Name
        });
    })
    .RequireAuthorization()
    .WithName("GetCurrentUser");

// Apply migrations automatically
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
    }
}

app.Run();

public record AdLoginRequest(string Username, string Password);

public record LoginRequest(string Email, string Password);

public record RegisterRequest(string Email, string Password, string? Name);

public record VerifyEmailRequest(string Email, string Code);