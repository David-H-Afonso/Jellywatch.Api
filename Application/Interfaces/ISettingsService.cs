using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Application.Interfaces;

public interface ISettingsService
{
    ServiceResult<ProviderSettingsDto> GetProviderSettings();
    Task<ServiceResult<List<PropagationRuleDto>>> GetPropagationRulesAsync();
    Task<ServiceResult<PropagationRuleDto>> CreatePropagationRuleAsync(PropagationRuleCreateDto dto);
    Task<ServiceResult<object>> UpdatePropagationRuleAsync(int id, PropagationRuleUpdateDto dto);
    Task<ServiceResult<object>> DeletePropagationRuleAsync(int id);
}
