using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Application.Interfaces;

public interface INoteService
{
    Task<ServiceResult<List<NoteDto>>> GetNotesAsync(int profileId, int? mediaItemId, int? seasonId, int? episodeId);
    Task<ServiceResult<NoteDto>> CreateOrUpdateNoteAsync(int profileId, NoteCreateUpdateDto dto);
    Task<ServiceResult<object>> DeleteNoteAsync(int profileId, int noteId);
}
