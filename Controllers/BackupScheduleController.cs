using Microsoft.AspNetCore.Mvc;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class BackupScheduleController : BaseApiController
{
    private readonly IBackupScheduleManagementService _service;

    public BackupScheduleController(IBackupScheduleManagementService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetSchedule()
    {
        var authResult = RequireUserId();
        if (authResult is not OkResult) return authResult;

        var result = await _service.GetScheduleAsync(CurrentUserId!.Value);
        return ToActionResult(result);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateSchedule([FromBody] UpdateBackupScheduleRequest req)
    {
        var authResult = RequireUserId();
        if (authResult is not OkResult) return authResult;

        var result = await _service.UpdateScheduleAsync(CurrentUserId!.Value, req);
        return ToActionResult(result);
    }


    [HttpPost("run-now")]
    public IActionResult RunNow()
    {
        var authResult = RequireUserId();
        if (authResult is not OkResult) return authResult;

        var result = _service.RunNow(CurrentUserId!.Value);
        return result.Success ? Accepted(result.Data) : ToActionResult(result);
    }

    [HttpGet("admin/users")]
    public async Task<IActionResult> GetAllUserSchedules()
    {
        var authResult = RequireUserId();
        if (authResult is not OkResult) return authResult;

        var result = await _service.GetAllUserSchedulesAsync(CurrentUserId!.Value);
        return ToActionResult(result);
    }

    [HttpGet("admin/{userId:int}")]
    public async Task<IActionResult> GetUserSchedule(int userId)
    {
        var authResult = RequireUserId();
        if (authResult is not OkResult) return authResult;

        var result = await _service.GetUserScheduleAsync(CurrentUserId!.Value, userId);
        return ToActionResult(result);
    }

    [HttpPut("admin/{userId:int}")]
    public async Task<IActionResult> UpdateUserSchedule(int userId, [FromBody] UpdateBackupScheduleRequest req)
    {
        var authResult = RequireUserId();
        if (authResult is not OkResult) return authResult;

        var result = await _service.UpdateUserScheduleAsync(CurrentUserId!.Value, userId, req);
        return ToActionResult(result);
    }

    [HttpPost("admin/{userId:int}/run-now")]
    public async Task<IActionResult> RunNowForUser(int userId)
    {
        var authResult = RequireUserId();
        if (authResult is not OkResult) return authResult;

        var result = await _service.RunNowForUserAsync(CurrentUserId!.Value, userId);
        return result.Success ? Accepted(result.Data) : ToActionResult(result);
    }
}
