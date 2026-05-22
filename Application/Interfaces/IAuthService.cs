using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Application.Interfaces;

public interface IAuthService
{
    string GenerateToken(User user);
}
