using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using POSSystem.API.Middleware;
using POSSystem.Infrastructure;
using POSSystem.Infrastructure.Identity;
using POSSystem.Infrastructure.Persistence;
// Load .env file into environment if present
var potentialEnvPaths = new[]
{
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"),
    Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env"),
    Path.Combine(AppContext.BaseDirectory, ".env"),
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env")
};

foreach (var envPath in potentialEnvPaths)
{
    if (File.Exists(envPath))
    {
        foreach (var rawLine in File.ReadAllLines(envPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            var eqIdx = line.IndexOf('=');
            if (eqIdx > 0)
            {
                var key = line[..eqIdx].Trim();
                var val = line[(eqIdx + 1)..].Trim().Trim('"', '\'');
                Environment.SetEnvironmentVariable(key, val);
            }
        }
        break;
    }
}

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "POS System API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token. Example: eyJhbGciOi...",
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            Array.Empty<string>()
        },
    });
});

builder.Services.AddInfrastructure(builder.Configuration);

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt settings are not configured.");

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

app.UseCors("Default");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var resolver = scope.ServiceProvider.GetRequiredService<POSSystem.Application.Common.Interfaces.IDatabaseContextResolver>();
    var dbFactory = scope.ServiceProvider.GetRequiredService<IApplicationDbContextFactory>();

    // 1. Normal Database Setup
    var normalConn = resolver.GetNormalConnectionString();
    await EnsureDatabaseExistsAsync(normalConn, logger);
    using (var normalDb = dbFactory.CreateNormalDbContext())
    {
        await normalDb.Database.MigrateAsync();
        await EnsureStoreSettingsColumnsAsync(normalDb, logger);
    }
    await DbSeeder.SeedAsync(scope.ServiceProvider);

    // 2. Admin Database Setup
    var adminConn = resolver.GetAdminConnectionString();
    await EnsureDatabaseExistsAsync(adminConn, logger);
    using (var adminDb = dbFactory.CreateAdminDbContext())
    {
        await adminDb.Database.MigrateAsync();
        await EnsureStoreSettingsColumnsAsync(adminDb, logger);
        await DbSeeder.SeedIntoContextAsync(adminDb, scope.ServiceProvider);
    }

    // 3. Process any pending shifts for cashiers exceeding threshold
    try
    {
        var shiftService = scope.ServiceProvider.GetRequiredService<POSSystem.Application.Features.Sales.ISalesShiftService>();
        var shiftedResults = await shiftService.CheckAndShiftAllEligibleCashiersAsync();
        foreach (var r in shiftedResults)
        {
            if (r.Shifted)
            {
                logger.LogInformation("Startup Sales Shift: {Message}", r.Message);
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not run startup sales shift check.");
    }

    // 4. Align product stocks between Normal and Admin DBs if any historical mismatch exists
    try
    {
        using var normalDb = dbFactory.CreateNormalDbContext();
        using var adminDb = dbFactory.CreateAdminDbContext();
        var normalStocks = await normalDb.Products.AsNoTracking().Select(p => new { p.Id, p.StockQuantity }).ToDictionaryAsync(p => p.Id, p => p.StockQuantity);
        var adminProds = await adminDb.Products.ToListAsync();
        bool changed = false;
        foreach (var ap in adminProds)
        {
            if (normalStocks.TryGetValue(ap.Id, out var nStock) && ap.StockQuantity != nStock)
            {
                ap.StockQuantity = nStock;
                changed = true;
            }
        }
        if (changed)
        {
            await adminDb.SaveChangesAsync();
            logger.LogInformation("Startup: Synchronized product stock quantities to Admin DB.");
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not run startup product stock alignment.");
    }
}

app.Run();

static async Task EnsureDatabaseExistsAsync(string connectionString, ILogger logger)
{
    try
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        var targetDb = csb.Database;
        if (string.IsNullOrWhiteSpace(targetDb)) return;

        csb.Database = "postgres";
        await using var conn = new Npgsql.NpgsqlConnection(csb.ConnectionString);
        await conn.OpenAsync();

        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = @dbname";
        checkCmd.Parameters.AddWithValue("dbname", targetDb);
        var exists = await checkCmd.ExecuteScalarAsync();

        if (exists is null)
        {
            logger.LogInformation("Database '{TargetDb}' does not exist. Creating now...", targetDb);
            await using var createCmd = conn.CreateCommand();
            createCmd.CommandText = $"CREATE DATABASE \"{targetDb.Replace("\"", "\"\"")}\"";
            await createCmd.ExecuteNonQueryAsync();
            logger.LogInformation("Database '{TargetDb}' created successfully.", targetDb);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not verify/create database via maintenance connection. Will attempt direct EF migration.");
    }
}

static async Task EnsureStoreSettingsColumnsAsync(ApplicationDbContext db, ILogger logger)
{
    try
    {
        await db.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE ""StoreSettings"" ADD COLUMN IF NOT EXISTS ""CashierSalesShiftThreshold"" numeric NOT NULL DEFAULT 60000;
            ALTER TABLE ""StoreSettings"" ADD COLUMN IF NOT EXISTS ""CashierSalesShiftEnabled"" boolean NOT NULL DEFAULT true;
            ALTER TABLE ""StoreSettings"" ADD COLUMN IF NOT EXISTS ""HotkeyBindingsJson"" text NULL;
            ALTER TABLE ""SaleItems"" ADD COLUMN IF NOT EXISTS ""ReturnedQuantity"" numeric NOT NULL DEFAULT 0;
        ");
        logger.LogInformation("Verified schema columns for active context.");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not ensure StoreSettings columns via raw SQL.");
    }
}
