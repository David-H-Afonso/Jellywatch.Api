using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Infrastructure;

namespace Jellywatch.Api.Controllers;

[Route("api/profiles/{profileId:int}/notes")]
public class NoteController : BaseApiController
{
    private readonly JellywatchDbContext _context;

    public NoteController(JellywatchDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<NoteDto>>> GetNotes(
        int profileId,
        [FromQuery] int? mediaItemId,
        [FromQuery] int? seasonId,
        [FromQuery] int? episodeId)
    {
        var query = _context.ProfileNotes
            .Where(n => n.ProfileId == profileId)
            .AsQueryable();

        if (mediaItemId.HasValue)
            query = query.Where(n => n.MediaItemId == mediaItemId.Value);
        if (seasonId.HasValue)
            query = query.Where(n => n.SeasonId == seasonId.Value);
        if (episodeId.HasValue)
            query = query.Where(n => n.EpisodeId == episodeId.Value);

        var notes = await query
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => new NoteDto
            {
                Id = n.Id,
                MediaItemId = n.MediaItemId,
                SeasonId = n.SeasonId,
                EpisodeId = n.EpisodeId,
                Text = n.Text,
                CreatedAt = n.CreatedAt,
                UpdatedAt = n.UpdatedAt
            })
            .ToListAsync();

        return Ok(notes);
    }

    [HttpPut]
    public async Task<ActionResult<NoteDto>> CreateOrUpdateNote(int profileId, [FromBody] NoteCreateUpdateDto dto)
    {
        // Find existing note for this profile + media target
        var existing = await _context.ProfileNotes
            .FirstOrDefaultAsync(n =>
                n.ProfileId == profileId &&
                n.MediaItemId == dto.MediaItemId &&
                n.SeasonId == dto.SeasonId &&
                n.EpisodeId == dto.EpisodeId);

        if (existing is not null)
        {
            existing.Text = dto.Text;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new ProfileNote
            {
                ProfileId = profileId,
                MediaItemId = dto.MediaItemId,
                SeasonId = dto.SeasonId,
                EpisodeId = dto.EpisodeId,
                Text = dto.Text
            };
            _context.ProfileNotes.Add(existing);
        }

        await _context.SaveChangesAsync();

        return Ok(new NoteDto
        {
            Id = existing.Id,
            MediaItemId = existing.MediaItemId,
            SeasonId = existing.SeasonId,
            EpisodeId = existing.EpisodeId,
            Text = existing.Text,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = existing.UpdatedAt
        });
    }

    [HttpDelete("{noteId:int}")]
    public async Task<IActionResult> DeleteNote(int profileId, int noteId)
    {
        var note = await _context.ProfileNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.ProfileId == profileId);

        if (note is null)
            return NotFound(new { message = "Note not found" });

        _context.ProfileNotes.Remove(note);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Note deleted" });
    }
}
