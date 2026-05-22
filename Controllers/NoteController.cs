using Microsoft.AspNetCore.Mvc;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Controllers;

[Route("api/profiles/{profileId:int}/notes")]
public class NoteController : BaseApiController
{
    private readonly INoteService _noteService;

    public NoteController(INoteService noteService)
    {
        _noteService = noteService;
    }

    [HttpGet]
    public async Task<ActionResult<List<NoteDto>>> GetNotes(
        int profileId,
        [FromQuery] int? mediaItemId,
        [FromQuery] int? seasonId,
        [FromQuery] int? episodeId)
    {
        var result = await _noteService.GetNotesAsync(profileId, mediaItemId, seasonId, episodeId);
        return ToActionResult(result);
    }

    [HttpPut]
    public async Task<ActionResult<NoteDto>> CreateOrUpdateNote(int profileId, [FromBody] NoteCreateUpdateDto dto)
    {
        var result = await _noteService.CreateOrUpdateNoteAsync(profileId, dto);
        return ToActionResult(result);
    }

    [HttpDelete("{noteId:int}")]
    public async Task<IActionResult> DeleteNote(int profileId, int noteId)
    {
        var result = await _noteService.DeleteNoteAsync(profileId, noteId);
        return ToActionResult(result);
    }
}
