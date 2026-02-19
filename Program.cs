using System.IdentityModel.Tokens.Jwt;
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

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// ADD Swagger
builder.Services.AddSwaggerGen();

// Database Context
var dbConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySQL(
        dbConnectionString
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' not found."
            )
    )
);

builder.Services.AddScoped<AdAuthService>();
builder.Services.AddScoped<AuthService>();

builder.Services.AddSingleton<IOracleConnectionFactory, OracleConnectionFactory>();
builder.Services.AddScoped<IOracleRepository, OracleRepository>();
builder.Services.AddScoped<IIspReportRepository, IspReportRepository>();
builder.Services.AddScoped<IProductSalesRepository, ProductSalesRepository>();
builder.Services.AddScoped<ISalesRepository, SalesRepository>();
builder.Services.AddScoped<ICacheService, CacheService>();
builder.Services.AddScoped<IIspReportService, IspReportService>();
builder.Services.AddScoped<IIspReportPdfService, IspReportPdfService>();
builder.Services.AddScoped<IProductSalesReportService, ProductSalesReportService>();

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "KtrnIspReportApiDefaultSecretKey2026!@#";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "isp-report-api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "isp-report-api-clients";

builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    );
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

app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "ISP REPORTS API");
});

app.MapControllers();

app.MapGet("/", () => Results.Json(new { status = "welcome", timestamp = DateTime.UtcNow }))
    .WithName("homepage");

app.MapGet("/health", () => Results.Json(new { status = "healthy", timestamp = DateTime.UtcNow }))
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
app.MapPost(
    "/auth/register",
    async ([FromBody] RegisterRequest request, AuthService authService) =>
    {
        if (!authService.IsValidKtrnEmail(request.Email))
        {
            return Results.BadRequest(new { Error = "Only @ktrn.rw email addresses are allowed." });
        }

        try
        {
            var (user, error) = await authService.RegisterAsync(
                request.Email,
                request.Password,
                request.Name
            );
            if (error != null)
                return Results.BadRequest(new { Error = error });

            var otpCode = await authService.CreateVerificationOtpAsync(request.Email);
            await authService.SendVerificationEmailAsync(request.Email, otpCode.Code);

            return Results.Ok(
                new
                {
                    Message = "Registration successful. Please check your email to verify.",
                    NeedsVerification = true,
                }
            );
        }
        catch (Exception ex)
        {
            return Results.Problem($"Registration failed: {ex.Message}");
        }
    }
);

app.MapPost(
    "/auth/login",
    async ([FromBody] LoginRequest request, AuthService authService) =>
    {
        try
        {
            var (user, error, needsVerification) = await authService.LoginAsync(
                request.Email,
                request.Password
            );
            if (error != null)
                return Results.BadRequest(new { Error = error });

            if (needsVerification)
            {
                var otpCode = await authService.CreateVerificationOtpAsync(request.Email);
                await authService.SendVerificationEmailAsync(request.Email, otpCode.Code);
                return Results.Ok(
                    new
                    {
                        Message = "Email not verified. Code sent to email.",
                        NeedsVerification = true,
                    }
                );
            }

            var token = authService.GenerateJwtToken(user!);
            return Results.Ok(
                new
                {
                    Token = token,
                    User = new
                    {
                        user!.Id,
                        user.Email,
                        user.Name,
                    },
                }
            );
        }
        catch (Exception ex)
        {
            return Results.Problem($"Login failed: {ex.Message}");
        }
    }
);

app.MapPost(
    "/auth/verify-email",
    async ([FromBody] VerifyEmailRequest request, AuthService authService) =>
    {
        try
        {
            var (success, error) = await authService.VerifyEmailAsync(request.Email, request.Code);
            if (!success)
                return Results.BadRequest(new { Error = error });

            var user = await authService.GetUserByEmailAsync(request.Email);
            var token = authService.GenerateJwtToken(user!);
            return Results.Ok(
                new
                {
                    Message = "Email verified successfully.",
                    Token = token,
                    User = new
                    {
                        user!.Id,
                        user.Email,
                        user.Name,
                    },
                }
            );
        }
        catch (Exception ex)
        {
            return Results.Problem($"Verification failed: {ex.Message}");
        }
    }
);

app.MapPost(
        "/auth/ad-login",
        async (
            [FromBody] AdLoginRequest request,
            AdAuthService adAuthService,
            AppDbContext db
        ) =>
        {
            if (
                string.IsNullOrWhiteSpace(request.Username)
                || string.IsNullOrWhiteSpace(request.Password)
            )
            {
                return Results.BadRequest(new { Error = "Username and password are required." });
            }

            try
            {
                // First check if user is in local users table and is active
                var (allowedEmail, allowError) = await adAuthService.EnsureLocalUserAllowedAsync(
                    request.Username
                );
                if (allowError != null)
                {
                    return Results.BadRequest(new { Error = allowError });
                }

                // Authenticate against Active Directory
                var adResult = await adAuthService.AuthenticateAsync(
                    request.Username,
                    request.Password
                );

                if (!adResult.Success)
                {
                    return Results.BadRequest(new { Error = adResult.ErrorMessage });
                }

                // Get or create local user for the AD user
                var user = await adAuthService.GetOrCreateUserAsync(adResult);

                var userWithRole = await db.Users
                    .AsNoTracking()
                    .Include(u => u.Role)
                    .ThenInclude(r => r!.RolePages)
                    .FirstOrDefaultAsync(u => u.Id == user.Id);

                var roleName = userWithRole?.Role?.Name;
                var permissions =
                    userWithRole?.Role?.RolePages?.Select(rp => rp.PageKey).ToList()
                    ?? new List<string>();

                var token = adAuthService.GenerateJwtToken(user);

                return Results.Ok(
                    new
                    {
                        Token = token,
                        User = new
                        {
                            user.Id,
                            user.Email,
                            user.Name,
                            RoleId = user.RoleId,
                            RoleName = roleName,
                            Permissions = permissions,
                        },
                        AuthMethod = "ActiveDirectory",
                    }
                );
            }
            catch (Exception ex)
            {
                return Results.Problem($"AD Login failed: {ex.Message}");
            }
        }
    )
    .WithName("AdLogin");

app.MapGet(
        "/auth/me",
        async (HttpContext context, AppDbContext db) =>
        {
            var userIdClaim =
                context.User.FindFirst(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirst(JwtRegisteredClaimNames.Sub);

            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return Results.Unauthorized();
            }

            var user = await db.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .ThenInclude(r => r!.RolePages)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return Results.NotFound();
            }

            var permissions = user.Role?.RolePages?.Select(rp => rp.PageKey).ToList() ?? new List<string>();

            return Results.Ok(
                new
                {
                    user.Id,
                    user.Email,
                    user.Name,
                    RoleId = user.RoleId,
                    RoleName = user.Role?.Name,
                    Permissions = permissions,
                }
            );
        }
    )
    .RequireAuthorization()
    .WithName("GetCurrentUser");

var allowedPageKeys = new[]
{
    "dashboard",
    "dashboard/prepaid-sales",
    "dashboard/postpaid-sales",
    "dashboard/traffic",
    "dashboard/product-sales",
    "dashboard/purchases",
    "dashboard/settings",
};

app.MapGet("/api/pages", () => Results.Ok(allowedPageKeys)).RequireAuthorization();

app.MapGet(
        "/api/roles",
        async (AppDbContext db) =>
        {
            var roles = await db.Roles
                .AsNoTracking()
                .Include(r => r.RolePages)
                .OrderBy(r => r.Name)
                .Select(
                    r =>
                        new
                        {
                            r.Id,
                            r.Name,
                            PageKeys = r.RolePages.Select(rp => rp.PageKey).ToList(),
                        }
                )
                .ToListAsync();
            return Results.Ok(roles);
        }
    )
    .RequireAuthorization();

app.MapPost(
        "/api/roles",
        async ([FromBody] CreateRoleRequest request, AppDbContext db) =>
        {
            var invalidPages = request.PageKeys?.Except(allowedPageKeys).ToList() ?? new List<string>();
            if (invalidPages.Count > 0)
            {
                return Results.BadRequest(
                    new { Error = $"Invalid page keys: {string.Join(", ", invalidPages)}" }
                );
            }

            var role = new Role { Name = request.Name.Trim() };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            foreach (var key in request.PageKeys ?? new List<string>())
            {
                db.RolePages.Add(new RolePage { RoleId = role.Id, PageKey = key });
            }

            await db.SaveChangesAsync();

            return Results.Created(
                $"/api/roles/{role.Id}",
                new { role.Id, role.Name, PageKeys = request.PageKeys }
            );
        }
    )
    .RequireAuthorization();

app.MapPut(
        "/api/roles/{id:int}",
        async (int id, [FromBody] UpdateRoleRequest request, AppDbContext db) =>
        {
            var role = await db.Roles.FindAsync(id);
            if (role == null)
                return Results.NotFound();

            role.Name = request.Name.Trim();
            var existingPages = await db.RolePages.Where(rp => rp.RoleId == id).ToListAsync();
            db.RolePages.RemoveRange(existingPages);

            var pageKeys = request.PageKeys ?? new List<string>();
            var invalidPages = pageKeys.Except(allowedPageKeys).ToList();
            if (invalidPages.Count > 0)
            {
                return Results.BadRequest(
                    new { Error = $"Invalid page keys: {string.Join(", ", invalidPages)}" }
                );
            }

            foreach (var key in pageKeys)
            {
                db.RolePages.Add(new RolePage { RoleId = id, PageKey = key });
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { role.Id, role.Name, PageKeys = pageKeys });
        }
    )
    .RequireAuthorization();

app.MapDelete("/api/roles/{id:int}", async (int id, AppDbContext db) =>
{
    var role = await db.Roles.FindAsync(id);
    if (role == null)
        return Results.NotFound();
    var usersWithRole = await db.Users.AnyAsync(u => u.RoleId == id);
    if (usersWithRole)
        return Results.BadRequest(new { Error = "Cannot delete role that is assigned to users." });
    db.Roles.Remove(role);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet(
        "/api/users/ad-search",
        async ([FromQuery] string? q, AdAuthService adAuthService) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            {
                return Results.Ok(Array.Empty<object>());
            }

            var adUsers = await adAuthService.SearchUsersAsync(q);
            var result = adUsers.Select(u => new
            {
                displayName = u.DisplayName,
                email = u.Email,
                department = u.Department,
            });
            return Results.Ok(result);
        }
    )
    .RequireAuthorization();

app.MapGet(
        "/api/users",
        async (AppDbContext db) =>
        {
            var users = await db.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .OrderBy(u => u.Email)
                .Select(
                    u =>
                        new
                        {
                            u.Id,
                            u.Email,
                            u.Name,
                            u.IsActive,
                            RoleId = u.RoleId,
                            RoleName = u.Role != null ? u.Role.Name : null,
                        }
                )
                .ToListAsync();
            return Results.Ok(users);
        }
    )
    .RequireAuthorization();

app.MapPost(
        "/api/users",
        async ([FromBody] CreateUserRequest request, AppDbContext db) =>
        {
            var email = request.Email.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(email))
                return Results.BadRequest(new { Error = "Email is required." });

            var exists = await db.Users.AnyAsync(u => u.Email == email);
            if (exists)
                return Results.BadRequest(new { Error = "A user with this email already exists." });

            if (request.RoleId.HasValue)
            {
                var roleExists = await db.Roles.AnyAsync(r => r.Id == request.RoleId.Value);
                if (!roleExists)
                    return Results.BadRequest(new { Error = "Invalid role." });
            }

            var user = new User
            {
                Email = email,
                Name = request.Name?.Trim(),
                RoleId = request.RoleId,
                IsActive = true,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return Results.Created(
                $"/api/users/{user.Id}",
                new
                {
                    user.Id,
                    user.Email,
                    user.Name,
                    user.IsActive,
                    user.RoleId,
                }
            );
        }
    )
    .RequireAuthorization();

app.MapPut(
        "/api/users/{id:int}",
        async (int id, [FromBody] UpdateUserRequest request, AppDbContext db) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user == null)
                return Results.NotFound();

            var email = request.Email?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(email) && email != user.Email)
            {
                var exists = await db.Users.AnyAsync(u => u.Email == email && u.Id != id);
                if (exists)
                    return Results.BadRequest(new { Error = "A user with this email already exists." });
                user.Email = email;
            }

            if (request.Name != null)
                user.Name = request.Name.Trim();
            if (request.RoleId != null)
            {
                if (request.RoleId.Value == 0)
                    user.RoleId = null;
                else
                {
                    var roleExists = await db.Roles.AnyAsync(r => r.Id == request.RoleId.Value);
                    if (!roleExists)
                        return Results.BadRequest(new { Error = "Invalid role." });
                    user.RoleId = request.RoleId;
                }
            }

            if (request.IsActive.HasValue)
                user.IsActive = request.IsActive.Value;

            await db.SaveChangesAsync();
            return Results.Ok(
                new
                {
                    user.Id,
                    user.Email,
                    user.Name,
                    user.IsActive,
                    user.RoleId,
                }
            );
        }
    )
    .RequireAuthorization();

app.MapDelete("/api/users/{id:int}", async (int id, AppDbContext db) =>
{
    var user = await db.Users.FindAsync(id);
    if (user == null)
        return Results.NotFound();
    db.Users.Remove(user);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

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

// Start background cache cleanup service
var cacheCleanupInterval = TimeSpan.FromHours(
    builder.Configuration.GetValue<int>("Cache:CleanupIntervalHours", 1)
);
_ = Task.Run(async () =>
{
    await Task.Delay(TimeSpan.FromMinutes(5)); // Wait 5 minutes after startup
    while (true)
    {
        try
        {
            await Task.Delay(cacheCleanupInterval);
            using var scope = app.Services.CreateScope();
            var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();
            await cacheService.ClearExpiredAsync();
        }
        catch (Exception ex)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Error in cache cleanup background task");
        }
    }
});

app.Run();

public record AdLoginRequest(string Username, string Password);

public record LoginRequest(string Email, string Password);

public record RegisterRequest(string Email, string Password, string? Name);

public record VerifyEmailRequest(string Email, string Code);

public record CreateRoleRequest(string Name, List<string>? PageKeys);

public record UpdateRoleRequest(string Name, List<string>? PageKeys);

public record CreateUserRequest(string Email, string? Name, int? RoleId);

public record UpdateUserRequest(string? Email, string? Name, int? RoleId, bool? IsActive);
