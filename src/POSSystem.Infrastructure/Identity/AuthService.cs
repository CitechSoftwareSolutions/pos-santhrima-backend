using Microsoft.AspNetCore.Identity;
using POSSystem.Application.Common.Exceptions;
using POSSystem.Application.Features.Auth;
using POSSystem.Domain.Enums;

namespace POSSystem.Infrastructure.Identity;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly ITokenService _tokenService;
    private readonly Persistence.IApplicationDbContextFactory _dbFactory;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ITokenService tokenService,
        Persistence.IApplicationDbContextFactory dbFactory)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _tokenService = tokenService;
        _dbFactory = dbFactory;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        if (request.Role == Roles.SuperSuperAdmin)
        {
            throw new ValidationAppException("Cannot assign SuperSuperAdmin role.");
        }

        if (!await _roleManager.RoleExistsAsync(request.Role))
        {
            throw new ValidationAppException($"Role '{request.Role}' does not exist.");
        }

        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            throw new ConflictException($"A user with email '{request.Email}' already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            IsActive = true,
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            throw new ValidationAppException(BuildErrors(createResult));
        }

        await _userManager.AddToRoleAsync(user, request.Role);

        return await BuildAuthResponseAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var identifier = request.Email?.Trim() ?? string.Empty;
        var user = await _userManager.FindByNameAsync(identifier) 
                ?? await _userManager.FindByEmailAsync(identifier);

        if (user is null || !user.IsActive)
        {
            throw new ValidationAppException("Invalid email or password.");
        }

        var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
        {
            throw new ValidationAppException("Invalid email or password.");
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        return await BuildAuthResponseAsync(user);
    }

    public async Task<List<UserDto>> GetAllUsersAsync()
    {
        var users = _userManager.Users.OrderBy(u => u.FullName).ToList();
        var result = new List<UserDto>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains(Roles.SuperSuperAdmin))
            {
                continue; // SuperSuperAdmin cannot be seen anywhere within the system
            }
            result.Add(new UserDto(user.Id, user.FullName, user.Email!, roles, user.IsActive));
        }

        return result;
    }

    public async Task SetUserActiveStatusAsync(Guid userId, bool isActive)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains(Roles.SuperSuperAdmin))
        {
            throw new NotFoundException(nameof(ApplicationUser), userId);
        }

        user.IsActive = isActive;
        await _userManager.UpdateAsync(user);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            throw new ValidationAppException(BuildErrors(result));
        }
    }

    public async Task ChangeSuperAdminCredentialsAsync(Guid userId, UpdateCredentialsRequest request)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains(Roles.SuperSuperAdmin))
        {
            throw new ValidationAppException("Only SuperSuperAdmin can update these credentials.");
        }

        var passValid = await _userManager.CheckPasswordAsync(user, request.CurrentPassword);
        if (!passValid)
        {
            throw new ValidationAppException("Current password is incorrect.");
        }

        if (!string.IsNullOrWhiteSpace(request.NewUsername))
        {
            var newU = request.NewUsername.Trim();
            var existing = await _userManager.FindByNameAsync(newU);
            if (existing != null && existing.Id != user.Id)
            {
                throw new ConflictException("Username is already in use.");
            }

            user.UserName = newU;
            user.NormalizedUserName = newU.ToUpperInvariant();
            user.Email = $"{newU}@possystem.local";
            user.NormalizedEmail = user.Email.ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetRes = await _userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);
            if (!resetRes.Succeeded)
            {
                throw new ValidationAppException(BuildErrors(resetRes));
            }
        }
        else
        {
            var updateRes = await _userManager.UpdateAsync(user);
            if (!updateRes.Succeeded)
            {
                throw new ValidationAppException(BuildErrors(updateRes));
            }
        }

        // Direct synchronization into companion database context to ensure instant dual consistency
        try
        {
            using var normalDb = _dbFactory.CreateNormalDbContext();
            var normalUser = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                normalDb.Users, u => u.Id == user.Id);
            if (normalUser != null)
            {
                normalUser.UserName = user.UserName;
                normalUser.NormalizedUserName = user.NormalizedUserName;
                normalUser.Email = user.Email;
                normalUser.NormalizedEmail = user.NormalizedEmail;
                normalUser.PasswordHash = user.PasswordHash;
                normalUser.SecurityStamp = user.SecurityStamp;
                await normalDb.SaveChangesAsync();
            }

            using var adminDb = _dbFactory.CreateAdminDbContext();
            var adminUser = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                adminDb.Users, u => u.Id == user.Id);
            if (adminUser != null)
            {
                adminUser.UserName = user.UserName;
                adminUser.NormalizedUserName = user.NormalizedUserName;
                adminUser.Email = user.Email;
                adminUser.NormalizedEmail = user.NormalizedEmail;
                adminUser.PasswordHash = user.PasswordHash;
                adminUser.SecurityStamp = user.SecurityStamp;
                await adminDb.SaveChangesAsync();
            }
        }
        catch (Exception)
        {
            // Fallback: Interceptor also auto-replicates
        }
    }

    public async Task<UserDto> UpdateUserAsync(Guid userId, UpdateUserRequest request)
    {
        if (request.Role == Roles.SuperSuperAdmin)
        {
            throw new ValidationAppException("Cannot assign SuperSuperAdmin role.");
        }

        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains(Roles.SuperSuperAdmin))
        {
            throw new NotFoundException(nameof(ApplicationUser), userId);
        }

        if (!await _roleManager.RoleExistsAsync(request.Role))
        {
            throw new ValidationAppException($"Role '{request.Role}' does not exist.");
        }

        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing is not null && existing.Id != user.Id)
        {
            throw new ConflictException($"A user with email '{request.Email}' already exists.");
        }

        user.FullName = request.FullName.Trim();
        user.Email = request.Email.Trim();
        user.UserName = request.Email.Trim();

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            throw new ValidationAppException(BuildErrors(updateResult));
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (!currentRoles.Contains(request.Role))
        {
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, request.Role);
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var passResult = await _userManager.ResetPasswordAsync(user, resetToken, request.Password);
            if (!passResult.Succeeded)
            {
                throw new ValidationAppException(BuildErrors(passResult));
            }
        }

        var updatedRoles = await _userManager.GetRolesAsync(user);
        return new UserDto(user.Id, user.FullName, user.Email!, updatedRoles, user.IsActive);
    }

    public async Task DeleteUserAsync(Guid userId, Guid currentUserId)
    {
        if (userId == currentUserId)
        {
            throw new BusinessRuleException("You cannot delete your own account.");
        }

        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains(Roles.SuperSuperAdmin))
        {
            throw new NotFoundException(nameof(ApplicationUser), userId);
        }

        if (roles.Contains(Roles.Admin))
        {
            var adminUsers = await _userManager.GetUsersInRoleAsync(Roles.Admin);
            if (adminUsers.Count(u => u.IsActive && u.Id != userId) == 0)
            {
                throw new BusinessRuleException("Cannot delete the only remaining active Administrator.");
            }
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new ValidationAppException(BuildErrors(result));
        }
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var (token, expiresAt) = _tokenService.GenerateToken(user.Id, user.Email!, user.FullName, roles);
        var userDto = new UserDto(user.Id, user.FullName, user.Email!, roles, user.IsActive);
        return new AuthResponse(token, expiresAt, userDto);
    }

    private static Dictionary<string, string[]> BuildErrors(IdentityResult result)
    {
        return result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
    }
}
