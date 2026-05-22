using Jellywatch.Api.Controllers;

namespace Jellywatch.Api.Application.Interfaces;

public interface IDataImportExportService
{
    Task<ServiceResult<ExportResult>> ExportAsync(int profileId);
    Task<ServiceResult<ImportPreviewDto>> ImportPreviewAsync(int profileId, Stream csvStream);
    Task<ServiceResult<ImportResultDto>> ImportAsync(int profileId, Stream csvStream, bool skipDuplicates, bool overwriteDates);
}

public record ExportResult(byte[] Bytes, string ContentType, string FileName);
