using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Application.Interfaces;

public interface IStatsService
{
    Task<ServiceResult<WrappedDto>> GetWrappedAsync(int profileId, int? year);
    Task<ServiceResult<List<CalendarDayDto>>> GetCalendarAsync(int profileId, int? year, int? month);
    Task<ServiceResult<List<UpcomingEpisodeDto>>> GetUpcomingAsync(int profileId, int days, int? currentUserId);
}
