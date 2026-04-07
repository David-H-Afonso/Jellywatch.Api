using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Infrastructure;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class SettingsController : BaseApiController
{
    private readonly JellywatchDbContext _context;
    private readonly TmdbSettings _tmdbSettings;
    private readonly OmdbSettings _omdbSettings;

    public SettingsController(
        JellywatchDbContext context,
        IOptions<TmdbSettings> tmdbSettings,
        IOptions<OmdbSettings> omdbSettings)
    {
        _context = context;
        _tmdbSettings = tmdbSettings.Value;
        _omdbSettings = omdbSettings.Value;
    }

    [HttpGet("providers")]
    public ActionResult<ProviderSettingsDto> GetProviderSettings()
    {
        return Ok(new ProviderSettingsDto
        {
            TmdbEnabled = !string.IsNullOrWhiteSpace(_tmdbSettings.ApiKey),
            TmdbHasApiKey = !string.IsNullOrWhiteSpace(_tmdbSettings.ApiKey),
            OmdbEnabled = !string.IsNullOrWhiteSpace(_omdbSettings.ApiKey),
            OmdbHasApiKey = !string.IsNullOrWhiteSpace(_omdbSettings.ApiKey),
            TvMazeEnabled = true, // TVmaze doesn't require an API key
            PrimaryLanguage = _tmdbSettings.PrimaryLanguage,
            FallbackLanguage = _tmdbSettings.FallbackLanguage
        });
    }

    [HttpGet("propagation")]
    public async Task<ActionResult<List<PropagationRuleDto>>> GetPropagationRules()
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

        return Ok(rules);
    }

    [HttpPost("propagation")]
    public async Task<ActionResult<PropagationRuleDto>> CreatePropagationRule([FromBody] PropagationRuleCreateDto dto)
    {
        // Verify profiles exist
        var source = await _context.Profiles.FindAsync(dto.SourceProfileId);
        var target = await _context.Profiles.FindAsync(dto.TargetProfileId);

        if (source is null || target is null)
            return BadRequest(new { message = "Source or target profile not found" });

        if (dto.SourceProfileId == dto.TargetProfileId)
            return BadRequest(new { message = "Source and target profiles must be different" });

        // Check for duplicate
        var existing = await _context.PropagationRules
            .FirstOrDefaultAsync(r => r.SourceProfileId == dto.SourceProfileId && r.TargetProfileId == dto.TargetProfileId);

        if (existing is not null)
            return Conflict(new { message = "Propagation rule already exists" });

        var rule = new PropagationRule
        {
            SourceProfileId = dto.SourceProfileId,
            TargetProfileId = dto.TargetProfileId,
            IsActive = dto.IsActive
        };

        _context.PropagationRules.Add(rule);
        await _context.SaveChangesAsync();

        return Ok(new PropagationRuleDto
        {
            Id = rule.Id,
            SourceProfileId = rule.SourceProfileId,
            SourceProfileName = source.DisplayName,
            TargetProfileId = rule.TargetProfileId,
            TargetProfileName = target.DisplayName,
            IsActive = rule.IsActive
        });
    }

    [HttpPut("propagation/{id:int}")]
    public async Task<IActionResult> UpdatePropagationRule(int id, [FromBody] PropagationRuleUpdateDto dto)
    {
        var rule = await _context.PropagationRules.FindAsync(id);
        if (rule is null)
            return NotFound(new { message = "Propagation rule not found" });

        rule.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Propagation rule updated" });
    }

    [HttpDelete("propagation/{id:int}")]
    public async Task<IActionResult> DeletePropagationRule(int id)
    {
        var rule = await _context.PropagationRules.FindAsync(id);
        if (rule is null)
            return NotFound(new { message = "Propagation rule not found" });

        _context.PropagationRules.Remove(rule);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Propagation rule deleted" });
    }
}
