using Absurd;
using Microsoft.AspNetCore.Mvc;

namespace LoanBooking.Controllers;

[ApiController]
[Route("tasks")]
public sealed class TasksController : ControllerBase
{
    private readonly IAbsurdClient _absurd;

    public TasksController(IAbsurdClient absurd) => _absurd = absurd;

    // GET /tasks/{taskId} – poll the Absurd workflow task state
    [HttpGet("{taskId}")]
    public async Task<IActionResult> GetTask(string taskId, CancellationToken ct)
    {
        var snapshot = await _absurd.FetchTaskResultAsync(taskId, ct);
        if (snapshot is null) return NotFound(new { error = "Task not found" });
        return Ok(snapshot);
    }
}
