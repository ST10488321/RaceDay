using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using RaceDay.Api.Security;

namespace RaceDay.Api.Controllers;

[ApiController]
[SessionAuthorize]
public class ResultsController(RaceDayContext db) : ApiControllerBase
{
    private IQueryable<Result> WithDetails() =>
        db.Results.Include(r => r.Enrolment!).ThenInclude(e => e.Event)
                  .Include(r => r.Enrolment!).ThenInclude(e => e.Participant);

    /// <summary>Get the result of one enrolment (the Participant who owns it, or the Organiser of the event).</summary>
    /// <remarks>Input: enrolment id in the URL. Returns: 200 result (finishTime, position). 401, 403, 404.</remarks>
    [HttpGet("api/enrolments/{enrolmentId:int}/result")]
    [ProducesResponseType(typeof(ResultDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ResultDto>> GetForEnrolment(int enrolmentId)
    {
        var en = await db.Enrolments.Include(e => e.Event).FirstOrDefaultAsync(e => e.Id == enrolmentId);
        if (en is null) return NotFoundMsg("Enrolment not found.");

        var allowed = IsOrganiser ? en.Event!.OrganiserId == CurrentUserId : en.ParticipantId == CurrentUserId;
        if (!allowed) return Forbidden("You are not allowed to view this result.");

        var result = await WithDetails().AsNoTracking().FirstOrDefaultAsync(r => r.EnrolmentId == enrolmentId);
        return result is null ? NotFoundMsg("No result has been recorded yet.") : Ok(result.ToDto());
    }

    /// <summary>List all of your own results (Participant only).</summary>
    /// <remarks>Input: none. Returns: 200 array of results. 401, 403 not a Participant.</remarks>
    [HttpGet("api/results/me")]
    [SessionAuthorize(Roles.Participant)]
    [ProducesResponseType(typeof(IEnumerable<ResultDto>), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<IEnumerable<ResultDto>>> GetMine()
    {
        var list = await WithDetails().AsNoTracking()
            .Where(r => r.Enrolment!.ParticipantId == CurrentUserId).ToListAsync();
        return Ok(list.Select(r => r.ToDto()));
    }

    /// <summary>List all results for one of your events (Organiser only), ordered by position.</summary>
    /// <remarks>Input: event id in the URL. Returns: 200 array of results. 401, 403, 404.</remarks>
    [HttpGet("api/events/{eventId:int}/results")]
    [SessionAuthorize(Roles.Organiser)]
    [ProducesResponseType(typeof(IEnumerable<ResultDto>), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<IEnumerable<ResultDto>>> GetForEvent(int eventId)
    {
        var ev = await db.Events.FindAsync(eventId);
        if (ev is null) return NotFoundMsg("Event not found.");
        if (ev.OrganiserId != CurrentUserId) return Forbidden("You can only view results of your own events.");

        var list = await WithDetails().AsNoTracking()
            .Where(r => r.Enrolment!.EventId == eventId).OrderBy(r => r.Position).ToListAsync();
        return Ok(list.Select(r => r.ToDto()));
    }

    /// <summary>Capture (or replace) the finish time and finishing position for an enrolment (Organiser only).</summary>
    /// <remarks>
    /// Input: enrolmentId, finishTime ("hh:mm:ss"), position (1 or higher), comments (optional).
    /// Creates the result, or updates it if one already exists (upsert).
    /// Returns: 200 the saved result. 400 invalid, 401, 403 not the event's Organiser, 404 enrolment not found.
    /// </remarks>
    [HttpPost("api/results")]
    [SessionAuthorize(Roles.Organiser)]
    [ProducesResponseType(typeof(ResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ResultDto>> Upsert(ResultUpsertDto dto)
    {
        var en = await db.Enrolments.Include(e => e.Event).Include(e => e.Participant)
            .Include(e => e.Result).FirstOrDefaultAsync(e => e.Id == dto.EnrolmentId!.Value);
        if (en is null) return NotFoundMsg("Enrolment not found.");
        if (en.Event!.OrganiserId != CurrentUserId) return Forbidden("You can only record results for your own events.");
        if (dto.FinishTime!.Value <= TimeSpan.Zero) return BadRequest(new { message = "Finish time must be greater than zero." });

        if (en.Result is null)
        {
            en.Result = new Result { EnrolmentId = en.Id };
            db.Results.Add(en.Result);
        }
        en.Result.FinishTime = dto.FinishTime.Value;
        en.Result.Position = dto.Position!.Value;
        en.Result.Comments = dto.Comments?.Trim();
        en.Result.RecordedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        en.Result.Enrolment = en;
        return Ok(en.Result.ToDto());
    }

    /// <summary>Update an existing result (Organiser only).</summary>
    /// <remarks>Input: any of finishTime, position, comments. Returns: 200 updated result. 400, 401, 403, 404.</remarks>
    [HttpPut("api/results/{id:int}")]
    [SessionAuthorize(Roles.Organiser)]
    [ProducesResponseType(typeof(ResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ResultDto>> Update(int id, ResultUpdateDto dto)
    {
        var result = await WithDetails().FirstOrDefaultAsync(r => r.Id == id);
        if (result is null) return NotFoundMsg("Result not found.");
        if (result.Enrolment!.Event!.OrganiserId != CurrentUserId) return Forbidden("You can only change results of your own events.");

        if (dto.FinishTime.HasValue) result.FinishTime = dto.FinishTime.Value;
        if (dto.Position.HasValue) result.Position = dto.Position.Value;
        if (dto.Comments != null) result.Comments = dto.Comments.Trim();
        await db.SaveChangesAsync();
        return Ok(result.ToDto());
    }

    /// <summary>Delete a result (Organiser only).</summary>
    /// <remarks>Input: result id in the URL. Returns: 204 No Content. 401, 403, 404.</remarks>
    [HttpDelete("api/results/{id:int}")]
    [SessionAuthorize(Roles.Organiser)]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await WithDetails().FirstOrDefaultAsync(r => r.Id == id);
        if (result is null) return NotFoundMsg("Result not found.");
        if (result.Enrolment!.Event!.OrganiserId != CurrentUserId) return Forbidden("You can only delete results of your own events.");

        db.Results.Remove(result);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
