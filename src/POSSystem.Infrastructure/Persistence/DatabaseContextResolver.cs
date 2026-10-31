using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using POSSystem.Application.Common.Interfaces;

namespace POSSystem.Infrastructure.Persistence;

public class DatabaseContextResolver : IDatabaseContextResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private bool? _explicitMode;

    public DatabaseContextResolver(
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
    }

    public string GetNormalConnectionString()
    {
        var normal = _configuration.GetConnectionString("DefaultConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string is not configured.");
        return normal;
    }

    public string GetAdminConnectionString()
    {
        var admin = _configuration.GetConnectionString("AdminConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__AdminConnection");

        if (!string.IsNullOrWhiteSpace(admin))
        {
            return admin;
        }

        // Derive automatically from DefaultConnection by appending _admin or replacing pos_system
        var normal = GetNormalConnectionString();
        if (normal.Contains("Database=pos_system;", StringComparison.OrdinalIgnoreCase))
        {
            return normal.Replace("Database=pos_system;", "Database=pos_system_admin;", StringComparison.OrdinalIgnoreCase);
        }

        if (normal.Contains("Database=pos_system", StringComparison.OrdinalIgnoreCase))
        {
            return normal.Replace("Database=pos_system", "Database=pos_system_admin", StringComparison.OrdinalIgnoreCase);
        }

        // Generic fallback: inject _admin into Database=<name>
        var dbIdx = normal.IndexOf("Database=", StringComparison.OrdinalIgnoreCase);
        if (dbIdx >= 0)
        {
            var semiIdx = normal.IndexOf(';', dbIdx);
            if (semiIdx > dbIdx)
            {
                var originalDbName = normal.Substring(dbIdx + 9, semiIdx - (dbIdx + 9));
                return normal.Substring(0, dbIdx + 9) + originalDbName + "_admin" + normal.Substring(semiIdx);
            }
            else
            {
                var originalDbName = normal.Substring(dbIdx + 9);
                return normal.Substring(0, dbIdx + 9) + originalDbName + "_admin";
            }
        }

        return normal;
    }

    public bool IsAdminMode()
    {
        if (_explicitMode.HasValue)
        {
            return _explicitMode.Value;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return false;
        }

        // 1. Check Header 'X-Database-Context' (e.g. "admin", "dark")
        if (httpContext.Request.Headers.TryGetValue("X-Database-Context", out var headerVal))
        {
            var str = headerVal.ToString().Trim().ToLowerInvariant();
            if (str is "admin" or "dark" or "true" or "1")
            {
                return true;
            }
        }

        // 2. Check Query parameter ?db=admin or ?theme=dark
        if (httpContext.Request.Query.TryGetValue("db", out var qDb) &&
            qDb.ToString().Equals("admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public string GetActiveConnectionString()
    {
        return IsAdminMode() ? GetAdminConnectionString() : GetNormalConnectionString();
    }

    public void SetExplicitMode(bool isAdmin)
    {
        _explicitMode = isAdmin;
    }
}
