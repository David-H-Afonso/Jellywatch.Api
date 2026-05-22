using Microsoft.AspNetCore.Mvc;
using Jellywatch.Api.Application.Interfaces;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class DataController : BaseApiController
{
    private readonly IDataImportExportService _dataService;

    public DataController(IDataImportExportService dataService)
    {
        _dataService = dataService;
    }

    // ━━━━ EXPORT ━━━━

    [HttpGet("{profileId:int}/export")]
    public async Task<IActionResult> Export(int profileId)
    {
        var result = await _dataService.ExportAsync(profileId);
        if (!result.Success)
            return result.StatusCode == 404
                ? NotFound(new { message = result.Error })
                : BadRequest(new { message = result.Error });

        var data = result.Data!;
        return File(data.Bytes, data.ContentType, data.FileName);
    }

    // ━━━━ IMPORT PREVIEW ━━━━

    [HttpPost("{profileId:int}/import/preview")]
    public async Task<ActionResult<ImportPreviewDto>> ImportPreview(int profileId, IFormFile file)
    {
        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "File too large (max 10 MB)" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".csv")
            return BadRequest(new { message = "Only CSV files are supported" });

        using var stream = file.OpenReadStream();
        var result = await _dataService.ImportPreviewAsync(profileId, stream);
        return ToActionResult(result);
    }

    // ━━━━ IMPORT EXECUTE ━━━━

    [HttpPost("{profileId:int}/import")]
    public async Task<ActionResult<ImportResultDto>> Import(
        int profileId,
        IFormFile file,
        [FromQuery] bool skipDuplicates = true,
        [FromQuery] bool overwriteDates = false)
    {
        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "File too large (max 10 MB)" });

        using var stream = file.OpenReadStream();
        var result = await _dataService.ImportAsync(profileId, stream, skipDuplicates, overwriteDates);
        return ToActionResult(result);
    }
}

// DTOs
public class ImportPreviewDto
{
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int DuplicateRows { get; set; }
    public int NotFoundRows { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<ImportRowDto> Rows { get; set; } = new();
}

public class ImportRowDto
{
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public int? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? EpisodeName { get; set; }
    public string State { get; set; } = "";
    public decimal? Rating { get; set; }
    public DateTime? WatchedAt { get; set; }
    public bool IsDuplicate { get; set; }
    public bool IsNotFound { get; set; }
    public bool WillBeAdded { get; set; }
}

public class ImportResultDto
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = new();
}
