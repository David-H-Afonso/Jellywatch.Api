using Microsoft.AspNetCore.Mvc;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class StatsController : BaseApiController
{
    private readonly IStatsService _statsService;

    public StatsController(IStatsService statsService)
    {
        _statsService = statsService;
    }

    [HttpGet("{profileId:int}/wrapped")]
    public async Task<ActionResult<WrappedDto>> GetWrapped(int profileId, [FromQuery] int? year)
    {
        var result = await _statsService.GetWrappedAsync(profileId, year);
        return ToActionResult(result);
    }

    [HttpGet("{profileId:int}/calendar")]
    public async Task<ActionResult<List<CalendarDayDto>>> GetCalendar(
        int profileId,
        [FromQuery] int? year,
        [FromQuery] int? month)
    {
        var result = await _statsService.GetCalendarAsync(profileId, year, month);
        return ToActionResult(result);
    }

    [HttpGet("{profileId:int}/upcoming")]
    public async Task<ActionResult<List<UpcomingEpisodeDto>>> GetUpcoming(int profileId, [FromQuery] int days = 30)
    {
        var result = await _statsService.GetUpcomingAsync(profileId, days, CurrentUserId);
        return ToActionResult(result);
    }
}
