using FirebaseAdmin;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Tarefista.Api.Services;
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
    [HttpPost]
    public async Task<IActionResult> AddTask([FromBody] TaskDto task)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (string.IsNullOrWhiteSpace(task.UserId) && string.IsNullOrWhiteSpace(task.TempUserId))
            return BadRequest(new { message = "userId OR tempUserId is required" });

        // Se tiver userId (logado), ignora tempUserId. Se não tiver, garante um tempUserId.
        var ownerUserId = string.IsNullOrWhiteSpace(task.UserId) ? null : task.UserId;
        var ownerTempUserId = ownerUserId is null
            ? (string.IsNullOrWhiteSpace(task.TempUserId) ? Guid.NewGuid().ToString("N") : task.TempUserId)
            : null;

        // Normalização de datas (usa as do request se válidas, senão agora)
        DateTime createdAtUtc = task.CreatedAt.HasValue
            ? DateTime.SpecifyKind(task.CreatedAt.Value, DateTimeKind.Utc)
            : DateTime.UtcNow;

        DateTime updatedAtUtc = task.UpdatedAt.HasValue
            ? DateTime.SpecifyKind(task.UpdatedAt.Value, DateTimeKind.Utc)
            : DateTime.UtcNow;

        var createdTs = Google.Cloud.Firestore.Timestamp.FromDateTime(createdAtUtc);
        var updatedTs = Google.Cloud.Firestore.Timestamp.FromDateTime(updatedAtUtc);

        bool isRecurring = task.IsRecurring ? task.IsRecurring : false;
        string? recurrencePattern = isRecurring ? (task.RecurrencePattern ?? string.Empty) : null;

        Google.Cloud.Firestore.Timestamp? startTs = null;
        Google.Cloud.Firestore.Timestamp? endTs = null;

        if (isRecurring)
        {
            if (task.StartDate.HasValue)
                startTs = Google.Cloud.Firestore.Timestamp.FromDateTime(DateTime.SpecifyKind(task.StartDate.Value, DateTimeKind.Utc));
            if (task.EndDate.HasValue)
                endTs = Google.Cloud.Firestore.Timestamp.FromDateTime(DateTime.SpecifyKind(task.EndDate.Value, DateTimeKind.Utc));
        }

        // Documento no Firestore (camelCase como no Node)
        var docData = new Dictionary<string, object?>
        {
            ["text"] = task.Text,
            ["completed"] = task.Completed,
            ["createdAt"] = createdTs,
            ["updatedAt"] = updatedTs,
            ["userId"] = ownerUserId,
            ["tempUserId"] = ownerTempUserId,
            ["isRecurring"] = isRecurring,
            ["recurrencePattern"] = recurrencePattern,
            ["startDate"] = startTs,
            ["endDate"] = endTs
        };

        var taskRef = await _db.Collection("tasks").AddAsync(docData);

        // Resposta "igual à antiga": id + todos os campos "achatados"
        return Created(string.Empty, new
        {
            id = taskRef.Id,
            text = task.Text,
            completed = task.Completed,
            createdAt = createdAtUtc,     // ISO 8601 no JSON
            updatedAt = updatedAtUtc,     // ISO 8601 no JSON
            tempUserId = ownerTempUserId,
            userId = ownerUserId,
            isRecurring = isRecurring,
            recurrencePattern = recurrencePattern,
            startDate = isRecurring ? task.StartDate : null,
            endDate = isRecurring ? task.EndDate : null
        });
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
    public async Task<IActionResult> DeleteTask(string id, [FromQuery] string? userId, [FromQuery] string? tempUserId)
    {
        var docRef = _db.Collection("tasks").Document(id);
        var snapshot = await docRef.GetSnapshotAsync();

        if (!snapshot.Exists)
            return NotFound(new { message = "Task not found" });

        string? docUserId = snapshot.ContainsField("userId") ? snapshot.GetValue<string>("userId") : null;
        string? docTempUserId = snapshot.ContainsField("tempUserId") ? snapshot.GetValue<string>("tempUserId") : null;

        bool authorized =
            (!string.IsNullOrEmpty(userId) && userId == docUserId) ||
            (!string.IsNullOrEmpty(tempUserId) && tempUserId == docTempUserId);

        if (!authorized)
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

    [HttpGet]
    public async Task<IActionResult> GetTasks([FromQuery] string? userId, [FromQuery] string? tempUserId)
    {
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(tempUserId))
            return BadRequest(new { message = "User ID or Temp User ID is required" });

        Query query = _db.Collection("tasks");
        if (!string.IsNullOrWhiteSpace(userId))
            query = query.WhereEqualTo("userId", userId);
        else
            query = query.WhereEqualTo("tempUserId", tempUserId);

        var snapshot = await query.GetSnapshotAsync();

        var tasks = snapshot.Documents.Select(doc =>
        {
            // Helpers para datas (tolerante a null e tipos diferentes)
            DateTime? ToUtc(string field)
            {
                if (!doc.ContainsField(field)) return null;

                if (doc.TryGetValue<object>(field, out var raw))
                {
                    if (raw == null) return null;

                    if (raw is Google.Cloud.Firestore.Timestamp ts)
                        return ts.ToDateTime().ToUniversalTime();

                    if (raw is DateTime dt)
                        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);

                    if (raw is string s && DateTime.TryParse(s, out var parsed))
                        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                }
                return null;
            }


            // Campos simples (com defaults seguros)
            doc.TryGetValue<string>("text", out var textRaw);
            var text = textRaw ?? string.Empty;

            var completed = false;
            if (doc.TryGetValue<bool>("completed", out var completedRaw)) completed = completedRaw;

            var isRecurring = false;
            if (doc.TryGetValue<bool>("isRecurring", out var recurRaw)) isRecurring = recurRaw;

            string? recurrencePattern = null;
            if (isRecurring)
            {
                if (doc.TryGetValue<string>("recurrencePattern", out var rp))
                    recurrencePattern = rp ?? string.Empty;
                else
                    recurrencePattern = string.Empty;
            }

            doc.TryGetValue<string>("userId", out var userIdField);
            doc.TryGetValue<string>("tempUserId", out var tempUserIdField);

            return new
            {
                id = doc.Id,
                text,
                completed,
                createdAt = ToUtc("createdAt"),
                updatedAt = ToUtc("updatedAt"),
                userId = userIdField,
                tempUserId = tempUserIdField,
                isRecurring,
                recurrencePattern,
                startDate = isRecurring ? ToUtc("startDate") : null,
                endDate = isRecurring ? ToUtc("endDate") : null
            };
        });

        return Ok(tasks);
    }
}
