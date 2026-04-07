namespace Jellywatch.Api.Contracts;

public class JellyfinLoginRequest
{
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string JellyfinUserId { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string? AvatarUrl { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public DateTime CreatedAt { get; set; }
}

public class UserMeResponse
{
    public int Id { get; set; }
    public string JellyfinUserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string? AvatarUrl { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public List<ProfileDto> Profiles { get; set; } = new();
}

public class ProfileDto
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string JellyfinUserId { get; set; } = string.Empty;
    public bool IsJoint { get; set; }
    public int? UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProfileDetailDto : ProfileDto
{
    public int TotalSeriesWatching { get; set; }
    public int TotalSeriesCompleted { get; set; }
    public int TotalMoviesSeen { get; set; }
    public int TotalEpisodesSeen { get; set; }
}

public class PropagationRuleDto
{
    public int Id { get; set; }
    public int SourceProfileId { get; set; }
    public string SourceProfileName { get; set; } = string.Empty;
    public int TargetProfileId { get; set; }
    public string TargetProfileName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class PropagationRuleUpdateDto
{
    public bool IsActive { get; set; }
}

public class PropagationRuleCreateDto
{
    public int SourceProfileId { get; set; }
    public int TargetProfileId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AddProfileRequest
{
    public string JellyfinUserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
