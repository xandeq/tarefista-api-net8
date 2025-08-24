using FirebaseAdmin;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using TarefistaApi.DTOs.Tasks;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly FirestoreDb _db;

    public TasksController(FirebaseService firebaseService)
    {
        _db = firebaseService.GetFirestoreDb();
    }

    [HttpGet]
    public async Task<IActionResult> GetTasks([FromQuery] string userId, [FromQuery] string tempUserId)
    {
        if (string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(tempUserId))
            return BadRequest(new { message = "User ID or Temp User ID is required" });

        Query query = _db.Collection("tasks");
        if (!string.IsNullOrEmpty(userId))
            query = query.WhereEqualTo("userId", userId);
        else
            query = query.WhereEqualTo("tempUserId", tempUserId);

        var snapshot = await query.GetSnapshotAsync();
        var tasks = snapshot.Documents.Select(d => new { id = d.Id, data = d.ToDictionary() });
        return Ok(tasks);
    }

    [HttpPost]
    public async Task<IActionResult> AddTask([FromBody] TaskDto task)
    {
        var newTask = new
        {
            task.Text,
            task.Completed,
            createdAt = Timestamp.GetCurrentTimestamp(),
            updatedAt = Timestamp.GetCurrentTimestamp(),
            task.TempUserId,
            task.IsRecurring,
            task.RecurrencePattern,
            task.StartDate,
            task.EndDate
        };

        var taskRef = await _db.Collection("tasks").AddAsync(newTask);
        return Created("", new { id = taskRef.Id, newTask });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTask(string id, [FromBody] TaskDto task)
    {
        var docRef = _db.Collection("tasks").Document(id);
        var snapshot = await docRef.GetSnapshotAsync();

        if (!snapshot.Exists)
            return NotFound(new { message = "Task not found" });

        await docRef.UpdateAsync(new Dictionary<string, object>
        {
            { "text", task.Text },
            { "completed", task.Completed },
            { "updatedAt", Timestamp.GetCurrentTimestamp() },
            { "isRecurring", task.IsRecurring },
            { "recurrencePattern", task.RecurrencePattern }
        });

        return Ok(new { message = "Task updated" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTask(string id, [FromBody] string userId)
    {
        var docRef = _db.Collection("tasks").Document(id);
        var snapshot = await docRef.GetSnapshotAsync();

        if (!snapshot.Exists || snapshot.GetValue<string>("userId") != userId)
            return Unauthorized(new { message = "Unauthorized" });

        await docRef.DeleteAsync();
        return Ok(new { message = "Task deleted successfully" });
    }

    [HttpPost("sync")]
    public async Task<IActionResult> SyncTasks([FromBody] SyncTasksDto sync)
    {
        if (string.IsNullOrEmpty(sync.UserId))
            return BadRequest(new { message = "User ID required" });

        var batch = _db.StartBatch();

        foreach (var t in sync.Tasks)
        {
            var doc = _db.Collection("tasks").Document();
            batch.Set(doc, new
            {
                t.Text,
                t.Completed,
                createdAt = t.CreatedAt ?? DateTime.UtcNow,
                updatedAt = DateTime.UtcNow,
                userId = sync.UserId
            });
        }

        await batch.CommitAsync();
        return Ok(new { message = "Tasks synced successfully" });
    }
}
