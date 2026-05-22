using Microsoft.AspNetCore.Mvc;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class SettingsController : BaseApiController
{
    private readonly ISettingsService _settingsService;

    public SettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [HttpGet("providers")]
    public ActionResult<ProviderSettingsDto> GetProviderSettings()
    {
        var result = _settingsService.GetProviderSettings();
        return ToActionResult(result);
    }

    [HttpGet("propagation")]
    public async Task<ActionResult<List<PropagationRuleDto>>> GetPropagationRules()
    {
        var result = await _settingsService.GetPropagationRulesAsync();
        return ToActionResult(result);
    }

    [HttpPost("propagation")]
    public async Task<ActionResult<PropagationRuleDto>> CreatePropagationRule([FromBody] PropagationRuleCreateDto dto)
    {
        var result = await _settingsService.CreatePropagationRuleAsync(dto);
        return ToActionResult(result);
    }

    [HttpPut("propagation/{id:int}")]
    public async Task<IActionResult> UpdatePropagationRule(int id, [FromBody] PropagationRuleUpdateDto dto)
    {
        var result = await _settingsService.UpdatePropagationRuleAsync(id, dto);
        return ToActionResult(result);
    }

    [HttpDelete("propagation/{id:int}")]
    public async Task<IActionResult> DeletePropagationRule(int id)
    {
        var result = await _settingsService.DeletePropagationRuleAsync(id);
        return ToActionResult(result);
    }
}
