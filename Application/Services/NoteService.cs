using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Infrastructure.Persistence;

namespace Jellywatch.Api.Application.Services;

public class NoteService : INoteService
{
    private readonly JellywatchDbContext _context;

    public NoteService(JellywatchDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<List<NoteDto>>> GetNotesAsync(int profileId, int? mediaItemId, int? seasonId, int? episodeId)
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

        return ServiceResult<List<NoteDto>>.Ok(notes);
    }

    public async Task<ServiceResult<NoteDto>> CreateOrUpdateNoteAsync(int profileId, NoteCreateUpdateDto dto)
    {
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

        return ServiceResult<NoteDto>.Ok(new NoteDto
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

    public async Task<ServiceResult<object>> DeleteNoteAsync(int profileId, int noteId)
    {
        var note = await _context.ProfileNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.ProfileId == profileId);

        if (note is null)
            return ServiceResult<object>.Fail("Note not found", 404);

        _context.ProfileNotes.Remove(note);
        await _context.SaveChangesAsync();

        return ServiceResult<object>.Ok(new { message = "Note deleted" });
    }
}
