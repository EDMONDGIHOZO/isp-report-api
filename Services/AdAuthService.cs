using System.DirectoryServices.Protocols;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using isp_report_api.Data;
using isp_report_api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace isp_report_api.Services;

public class AdAuthService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdAuthService> _logger;
    private readonly AppDbContext _db;

    public AdAuthService(
        IConfiguration configuration,
        ILogger<AdAuthService> logger,
        AppDbContext db
    )
    {
        _configuration = configuration;
        _logger = logger;
        _db = db;
    }

    /// <summary>
    /// Checks if a user exists in the local Users table and is active.
    /// Returns the normalized email if allowed, or (null, errorMessage) if not.
    /// </summary>
    public async Task<(string? Email, string? ErrorMessage)> EnsureLocalUserAllowedAsync(
        string usernameOrEmail
    )
    {
        var email = usernameOrEmail.Trim();
        if (email.Contains('@'))
        {
            email = email.ToLowerInvariant();
        }
        else
        {
            var domain = _configuration["ActiveDirectory:Domain"] ?? "ktrn.rw";
            email = $"{email}@{domain}".ToLowerInvariant();
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            return (null, "User is not allowed to access this application.");
        }

        if (!user.IsActive)
        {
            return (null, "User account is inactive.");
        }

        return (email, null);
    }

    /// <summary>
    /// Authenticates a user against Active Directory using LDAP.
    /// Accepts either a username (sAMAccountName) or work email address.
    /// </summary>
    public async Task<AdAuthResult> AuthenticateAsync(string usernameOrEmail, string password)
    {
        if (string.IsNullOrWhiteSpace(usernameOrEmail) || string.IsNullOrWhiteSpace(password))
        {
            return new AdAuthResult
            {
                Success = false,
                ErrorMessage = "Username/email and password are required.",
            };
        }

        var server = _configuration["ActiveDirectory:Server"];
        var portStr = _configuration["ActiveDirectory:Port"] ?? "389";
        var port = int.Parse(portStr);
        var baseDn = _configuration["ActiveDirectory:BaseDn"];
        var domain = _configuration["ActiveDirectory:Domain"];
        var useSSL = bool.Parse(_configuration["ActiveDirectory:UseSSL"] ?? "false");
        var serviceAccount = _configuration["ActiveDirectory:ServiceAccount"];
        var servicePassword = _configuration["ActiveDirectory:ServicePassword"];

        if (string.IsNullOrEmpty(server))
        {
            _logger.LogError("Active Directory server is not configured");
            return new AdAuthResult
            {
                Success = false,
                ErrorMessage = "Active Directory is not configured.",
            };
        }

        var ldapServer = server.Replace("ldap://", "").Replace("ldaps://", "");
        var ldapIdentifier = new LdapDirectoryIdentifier(ldapServer, port);

        // Determine if input is an email address
        var isEmail = usernameOrEmail.Contains('@');
        var username = usernameOrEmail;

        // If email provided, resolve to sAMAccountName first
        if (
            isEmail
            && !string.IsNullOrEmpty(serviceAccount)
            && !string.IsNullOrEmpty(servicePassword)
        )
        {
            var resolvedUsername = await ResolveEmailToUsernameAsync(
                ldapIdentifier,
                useSSL,
                baseDn,
                domain,
                serviceAccount,
                servicePassword,
                usernameOrEmail
            );

            if (resolvedUsername == null)
            {
                return new AdAuthResult
                {
                    Success = false,
                    ErrorMessage = "No user found with that email address.",
                };
            }

            username = resolvedUsername;
            _logger.LogInformation(
                "Resolved email {Email} to username {Username}",
                usernameOrEmail,
                username
            );
        }
        else if (isEmail)
        {
            // No service account configured, try using email prefix as username
            username = usernameOrEmail.Split('@')[0];
            _logger.LogInformation(
                "No service account configured, using email prefix as username: {Username}",
                username
            );
        }

        try
        {
            var userDn = !string.IsNullOrEmpty(domain) ? $"{domain}\\{username}" : username;

            var credential = new NetworkCredential(userDn, password);

            using var connection = new LdapConnection(ldapIdentifier)
            {
                AuthType = AuthType.Basic,
                Credential = credential,
            };

            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.SecureSocketLayer = useSSL;

            if (!useSSL)
            {
                connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            }

            connection.Bind();

            var userDetails = await GetUserDetailsFromAdAsync(
                connection,
                baseDn,
                username,
                isEmail ? usernameOrEmail : null
            );

            return new AdAuthResult
            {
                Success = true,
                Username = username,
                DisplayName = userDetails?.DisplayName ?? username,
                Email = userDetails?.Email ?? $"{username}@ktrn.rw",
                Department = userDetails?.Department,
            };
        }
        catch (LdapException ex)
        {
            var errorMessage = ex.ErrorCode switch
            {
                49 => "Invalid username or password.",
                81 => "Cannot connect to the Active Directory server.",
                _ =>
                    $"Authentication failed (Error {ex.ErrorCode}). Please check your credentials.",
            };

            return new AdAuthResult { Success = false, ErrorMessage = errorMessage };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error during AD authentication for user {Username}",
                username
            );
            return new AdAuthResult
            {
                Success = false,
                ErrorMessage = $"An unexpected error occurred: {ex.Message}",
            };
        }
    }

    /// <summary>
    /// Resolves an email address to a sAMAccountName using a service account
    /// </summary>
    private async Task<string?> ResolveEmailToUsernameAsync(
        LdapDirectoryIdentifier ldapIdentifier,
        bool useSSL,
        string? baseDn,
        string? domain,
        string serviceAccount,
        string servicePassword,
        string email
    )
    {
        if (string.IsNullOrEmpty(baseDn))
        {
            return null;
        }

        try
        {
            var serviceUserDn = !string.IsNullOrEmpty(domain)
                ? $"{domain}\\{serviceAccount}"
                : serviceAccount;
            var credential = new NetworkCredential(serviceUserDn, servicePassword);

            using var connection = new LdapConnection(ldapIdentifier)
            {
                AuthType = AuthType.Basic,
                Credential = credential,
            };

            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.SecureSocketLayer = useSSL;

            if (!useSSL)
            {
                connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            }

            connection.Bind();

            // Search by mail or userPrincipalName
            var escapedEmail = EscapeLdapSearchFilter(email);
            var searchFilter = $"(|(mail={escapedEmail})(userPrincipalName={escapedEmail}))";
            var searchRequest = new SearchRequest(
                baseDn,
                searchFilter,
                SearchScope.Subtree,
                "sAMAccountName"
            );

            var response = (SearchResponse)connection.SendRequest(searchRequest);

            if (response.Entries.Count > 0)
            {
                var entry = response.Entries[0];
                return GetAttributeValue(entry, "sAMAccountName");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not resolve email to username: {Error}", ex.Message);
        }

        return null;
    }

    private Task<AdUserDetails?> GetUserDetailsFromAdAsync(
        LdapConnection connection,
        string? baseDn,
        string username,
        string? originalEmail = null
    )
    {
        if (string.IsNullOrEmpty(baseDn))
        {
            return Task.FromResult<AdUserDetails?>(null);
        }

        try
        {
            var searchFilter = $"(sAMAccountName={EscapeLdapSearchFilter(username)})";
            var searchRequest = new SearchRequest(
                baseDn,
                searchFilter,
                SearchScope.Subtree,
                "displayName",
                "mail",
                "department",
                "sAMAccountName",
                "userPrincipalName"
            );

            var response = (SearchResponse)connection.SendRequest(searchRequest);

            if (response.Entries.Count > 0)
            {
                var entry = response.Entries[0];
                return Task.FromResult<AdUserDetails?>(
                    new AdUserDetails
                    {
                        DisplayName = GetAttributeValue(entry, "displayName"),
                        Email = GetAttributeValue(entry, "mail"),
                        Department = GetAttributeValue(entry, "department"),
                        SamAccountName = GetAttributeValue(entry, "sAMAccountName"),
                        UserPrincipalName = GetAttributeValue(entry, "userPrincipalName"),
                    }
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not retrieve user details from AD: {Error}", ex.Message);
        }

        return Task.FromResult<AdUserDetails?>(null);
    }

    private static string? GetAttributeValue(SearchResultEntry entry, string attributeName)
    {
        if (entry.Attributes.Contains(attributeName) && entry.Attributes[attributeName].Count > 0)
        {
            return entry.Attributes[attributeName][0]?.ToString();
        }

        return null;
    }

    private static string EscapeLdapSearchFilter(string input)
    {
        var sb = new StringBuilder();
        foreach (char c in input)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\5c");
                    break;
                case '*':
                    sb.Append("\\2a");
                    break;
                case '(':
                    sb.Append("\\28");
                    break;
                case ')':
                    sb.Append("\\29");
                    break;
                case '\0':
                    sb.Append("\\00");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Searches Active Directory for users matching the given query string.
    /// Uses the configured service account for binding. Searches across
    /// displayName, sAMAccountName, and mail attributes.
    /// </summary>
    public Task<List<AdUserDetails>> SearchUsersAsync(string query)
    {
        var results = new List<AdUserDetails>();

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return Task.FromResult(results);

        var server = _configuration["ActiveDirectory:Server"];
        var portStr = _configuration["ActiveDirectory:Port"] ?? "389";
        var port = int.Parse(portStr);
        var baseDn = _configuration["ActiveDirectory:BaseDn"];
        var domain = _configuration["ActiveDirectory:Domain"];
        var useSSL = bool.Parse(_configuration["ActiveDirectory:UseSSL"] ?? "false");
        var serviceAccount = _configuration["ActiveDirectory:ServiceAccount"];
        var servicePassword = _configuration["ActiveDirectory:ServicePassword"];

        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(serviceAccount) ||
            string.IsNullOrEmpty(servicePassword) || string.IsNullOrEmpty(baseDn))
        {
            _logger.LogWarning("AD search skipped: service account or server not configured");
            return Task.FromResult(results);
        }

        try
        {
            var ldapServer = server.Replace("ldap://", "").Replace("ldaps://", "");
            var ldapIdentifier = new LdapDirectoryIdentifier(ldapServer, port);

            var serviceUserDn = !string.IsNullOrEmpty(domain)
                ? $"{domain}\\{serviceAccount}"
                : serviceAccount;
            var credential = new NetworkCredential(serviceUserDn, servicePassword);

            using var connection = new LdapConnection(ldapIdentifier)
            {
                AuthType = AuthType.Basic,
                Credential = credential,
            };

            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.SecureSocketLayer = useSSL;

            if (!useSSL)
            {
                connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            }

            connection.Bind();

            var escapedQuery = EscapeLdapSearchFilter(query.Trim());

            // Search for person objects matching the query in name, username, or email
            var searchFilter =
                $"(&(objectCategory=person)(objectClass=user)(|(displayName=*{escapedQuery}*)(sAMAccountName=*{escapedQuery}*)(mail=*{escapedQuery}*)))";

            var searchRequest = new SearchRequest(
                baseDn,
                searchFilter,
                SearchScope.Subtree,
                "displayName",
                "mail",
                "department",
                "sAMAccountName",
                "userPrincipalName"
            );

            // Limit results to keep autocomplete snappy
            searchRequest.SizeLimit = 20;

            var response = (SearchResponse)connection.SendRequest(searchRequest);

            foreach (SearchResultEntry entry in response.Entries)
            {
                var email = GetAttributeValue(entry, "mail");
                var displayName = GetAttributeValue(entry, "displayName");

                // Skip entries without an email
                if (string.IsNullOrEmpty(email))
                    continue;

                results.Add(new AdUserDetails
                {
                    DisplayName = displayName,
                    Email = email,
                    Department = GetAttributeValue(entry, "department"),
                    SamAccountName = GetAttributeValue(entry, "sAMAccountName"),
                    UserPrincipalName = GetAttributeValue(entry, "userPrincipalName"),
                });
            }

            _logger.LogInformation("AD search for '{Query}' returned {Count} results", query, results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching AD for users matching '{Query}'", query);
        }

        return Task.FromResult(results);
    }

    public async Task<User> GetOrCreateUserAsync(AdAuthResult adResult)
    {
        var email =
            adResult.Email?.ToLowerInvariant() ?? $"{adResult.Username}@ktrn.rw".ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            user = new User
            {
                Email = email,
                Name = adResult.DisplayName ?? adResult.Username,
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                LastLoginAt = DateTime.UtcNow,
            };
            _db.Users.Add(user);
        }
        else
        {
            user.LastLoginAt = DateTime.UtcNow;
            user.Name = adResult.DisplayName ?? user.Name;
        }

        await _db.SaveChangesAsync();
        return user;
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
            new Claim("auth_method", "active_directory"),
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

public class AdAuthResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Department { get; set; }
}

public class AdUserDetails
{
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? SamAccountName { get; set; }
    public string? UserPrincipalName { get; set; }
}