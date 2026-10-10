namespace POSSystem.Application.Features.Auth;

public record RegisterRequest(string FullName, string Email, string Password, string Role);

public record UpdateUserRequest(string FullName, string Email, string? Password, string Role);

public record LoginRequest(string Email, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record UserDto(Guid Id, string FullName, string Email, IList<string> Roles, bool IsActive);

public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<List<UserDto>> GetAllUsersAsync();
    Task SetUserActiveStatusAsync(Guid userId, bool isActive);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
    Task<UserDto> UpdateUserAsync(Guid userId, UpdateUserRequest request);
    Task DeleteUserAsync(Guid userId, Guid currentUserId);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) GenerateToken(Guid userId, string email, string fullName, IList<string> roles);
}
