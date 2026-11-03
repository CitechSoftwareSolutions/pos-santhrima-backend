using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using POSSystem.Domain.Enums;
using POSSystem.Infrastructure.Identity;

namespace POSSystem.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbSeeder));

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                logger.LogInformation("Created role {Role}", role);
            }
        }

        const string adminEmail = "admin@possystem.local";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);

        if (adminUser is null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "System Administrator",
                EmailConfirmed = true,
                IsActive = true,
            };

            var result = await userManager.CreateAsync(adminUser, "Admin@12345");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, Roles.Admin);
                logger.LogInformation("Seeded default admin user {Email}", adminEmail);
            }
            else
            {
                logger.LogWarning("Failed to seed admin user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

        var superSuperAdmins = await userManager.GetUsersInRoleAsync(Roles.SuperSuperAdmin);
        if (superSuperAdmins.Count == 0)
        {
            var superAdminUser = await userManager.FindByNameAsync("8888101610265");
            if (superAdminUser is null)
            {
                superAdminUser = new ApplicationUser
                {
                    UserName = "8888101610265",
                    Email = "8888101610265@possystem.local",
                    FullName = "Super Super Admin",
                    EmailConfirmed = true,
                    IsActive = true,
                };

                var result = await userManager.CreateAsync(superAdminUser, "8888101613266");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(superAdminUser, Roles.SuperSuperAdmin);
                    logger.LogInformation("Seeded hidden super super admin user");
                }
                else
                {
                    logger.LogWarning("Failed to seed super super admin user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
    }

    public static async Task SeedIntoContextAsync(ApplicationDbContext context, IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbSeeder));

        foreach (var role in Roles.All)
        {
            var exists = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                context.Roles, r => r.Name == role);
            if (!exists)
            {
                context.Roles.Add(new IdentityRole<Guid>(role) { NormalizedName = role.ToUpperInvariant() });
                logger.LogInformation("Created role {Role} in context", role);
            }
        }
        await context.SaveChangesAsync();

        const string adminEmail = "admin@possystem.local";
        var adminExists = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
            context.Users, u => u.Email == adminEmail);

        if (!adminExists)
        {
            var hasher = new PasswordHasher<ApplicationUser>();
            var adminUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = adminEmail,
                NormalizedUserName = adminEmail.ToUpperInvariant(),
                Email = adminEmail,
                NormalizedEmail = adminEmail.ToUpperInvariant(),
                FullName = "System Administrator",
                EmailConfirmed = true,
                IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString()
            };
            adminUser.PasswordHash = hasher.HashPassword(adminUser, "Admin@12345");
            context.Users.Add(adminUser);
            await context.SaveChangesAsync();

            var adminRole = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                context.Roles, r => r.Name == Roles.Admin);

            if (adminRole != null)
            {
                context.UserRoles.Add(new IdentityUserRole<Guid>
                {
                    UserId = adminUser.Id,
                    RoleId = adminRole.Id
                });
                await context.SaveChangesAsync();
            }
            logger.LogInformation("Seeded default admin user in context");
        }

        var superAdminRole = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            context.Roles, r => r.Name == Roles.SuperSuperAdmin);

        if (superAdminRole != null)
        {
            var hasSuperAdmin = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                context.UserRoles, ur => ur.RoleId == superAdminRole.Id);

            if (!hasSuperAdmin)
            {
                var hasher = new PasswordHasher<ApplicationUser>();
                var superUser = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = "8888101610265",
                    NormalizedUserName = "8888101610265".ToUpperInvariant(),
                    Email = "8888101610265@possystem.local",
                    NormalizedEmail = "8888101610265@possystem.local".ToUpperInvariant(),
                    FullName = "Super Super Admin",
                    EmailConfirmed = true,
                    IsActive = true,
                    SecurityStamp = Guid.NewGuid().ToString()
                };
                superUser.PasswordHash = hasher.HashPassword(superUser, "8888101613266");
                context.Users.Add(superUser);
                await context.SaveChangesAsync();

                context.UserRoles.Add(new IdentityUserRole<Guid>
                {
                    UserId = superUser.Id,
                    RoleId = superAdminRole.Id
                });
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded hidden super super admin user in context");
            }
        }
    }
}
