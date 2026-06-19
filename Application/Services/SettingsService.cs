using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Infrastructure.Persistence;

namespace Jellywatch.Api.Application.Services;

public class SettingsService : ISettingsService
{
    private readonly JellywatchDbContext _context;
    private readonly TmdbSettings _tmdbSettings;
    private readonly OmdbSettings _omdbSettings;
    private readonly SonarrSettings _sonarrSettings;
    private readonly RadarrSettings _radarrSettings;

    public SettingsService(
        JellywatchDbContext context,
        IOptions<TmdbSettings> tmdbSettings,
        IOptions<OmdbSettings> omdbSettings,
        IOptions<SonarrSettings> sonarrSettings,
        IOptions<RadarrSettings> radarrSettings)
    {
        _context = context;
        _tmdbSettings = tmdbSettings.Value;
        _omdbSettings = omdbSettings.Value;
        _sonarrSettings = sonarrSettings.Value;
        _radarrSettings = radarrSettings.Value;
    }

    public ServiceResult<ProviderSettingsDto> GetProviderSettings()
    {
        return ServiceResult<ProviderSettingsDto>.Ok(new ProviderSettingsDto
        {
            TmdbEnabled = !string.IsNullOrWhiteSpace(_tmdbSettings.ApiKey),
            TmdbHasApiKey = !string.IsNullOrWhiteSpace(_tmdbSettings.ApiKey),
            OmdbEnabled = !string.IsNullOrWhiteSpace(_omdbSettings.ApiKey),
            OmdbHasApiKey = !string.IsNullOrWhiteSpace(_omdbSettings.ApiKey),
            TvMazeEnabled = true,
            SonarrEnabled = !string.IsNullOrWhiteSpace(_sonarrSettings.BaseUrl) && !string.IsNullOrWhiteSpace(_sonarrSettings.ApiKey),
            RadarrEnabled = !string.IsNullOrWhiteSpace(_radarrSettings.BaseUrl) && !string.IsNullOrWhiteSpace(_radarrSettings.ApiKey),
            PrimaryLanguage = _tmdbSettings.PrimaryLanguage,
            FallbackLanguage = _tmdbSettings.FallbackLanguage
        });
    }

    public async Task<ServiceResult<List<PropagationRuleDto>>> GetPropagationRulesAsync()
    {
        var rules = await _context.PropagationRules
            .Include(r => r.SourceProfile)
            .Include(r => r.TargetProfile)
            .OrderBy(r => r.SourceProfileId)
            .Select(r => new PropagationRuleDto
            {
                Id = r.Id,
                SourceProfileId = r.SourceProfileId,
                SourceProfileName = r.SourceProfile.DisplayName,
                TargetProfileId = r.TargetProfileId,
                TargetProfileName = r.TargetProfile.DisplayName,
                IsActive = r.IsActive
            })
            .ToListAsync();

        return ServiceResult<List<PropagationRuleDto>>.Ok(rules);
    }

    public async Task<ServiceResult<PropagationRuleDto>> CreatePropagationRuleAsync(PropagationRuleCreateDto dto)
    {
        var source = await _context.Profiles.FindAsync(dto.SourceProfileId);
        var target = await _context.Profiles.FindAsync(dto.TargetProfileId);

        if (source is null || target is null)
            return ServiceResult<PropagationRuleDto>.Fail("Source or target profile not found", 400);

        if (dto.SourceProfileId == dto.TargetProfileId)
            return ServiceResult<PropagationRuleDto>.Fail("Source and target profiles must be different", 400);

        var existing = await _context.PropagationRules
            .FirstOrDefaultAsync(r => r.SourceProfileId == dto.SourceProfileId && r.TargetProfileId == dto.TargetProfileId);

        if (existing is not null)
            return ServiceResult<PropagationRuleDto>.Fail("Propagation rule already exists", 409);

        var rule = new PropagationRule
        {
            SourceProfileId = dto.SourceProfileId,
            TargetProfileId = dto.TargetProfileId,
            IsActive = dto.IsActive
        };

        _context.PropagationRules.Add(rule);
        await _context.SaveChangesAsync();

        return ServiceResult<PropagationRuleDto>.Ok(new PropagationRuleDto
        {
            Id = rule.Id,
            SourceProfileId = rule.SourceProfileId,
            SourceProfileName = source.DisplayName,
            TargetProfileId = rule.TargetProfileId,
            TargetProfileName = target.DisplayName,
            IsActive = rule.IsActive
        });
    }

    public async Task<ServiceResult<object>> UpdatePropagationRuleAsync(int id, PropagationRuleUpdateDto dto)
    {
        var rule = await _context.PropagationRules.FindAsync(id);
        if (rule is null)
            return ServiceResult<object>.Fail("Propagation rule not found", 404);

        rule.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();

        return ServiceResult<object>.Ok(new { message = "Propagation rule updated" });
    }

    public async Task<ServiceResult<object>> DeletePropagationRuleAsync(int id)
    {
        var rule = await _context.PropagationRules.FindAsync(id);
        if (rule is null)
            return ServiceResult<object>.Fail("Propagation rule not found", 404);

        _context.PropagationRules.Remove(rule);
        await _context.SaveChangesAsync();

        return ServiceResult<object>.Ok(new { message = "Propagation rule deleted" });
    }
}
