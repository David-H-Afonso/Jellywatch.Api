using Jellywatch.Api.Domain;

namespace Jellywatch.Api.Services.Auth;

public interface IAuthService
{
    string GenerateToken(User user);
}
