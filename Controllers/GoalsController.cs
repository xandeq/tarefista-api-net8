using FirebaseAdmin;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Tarefista.Api.Services;
using TarefistaApi.DTOs.Goals;

[ApiController]
[Route("api/[controller]")]
public class GoalsController : ControllerBase
{
    private readonly FirestoreDb _db;

    public GoalsController(FirebaseService firebaseService)
    {
        _db = firebaseService.GetFirestoreDb();
    }

    [HttpPost]
    public async Task<IActionResult> AddGoal([FromBody] GoalDto goal)
    {
        if (string.IsNullOrWhiteSpace(goal.UserId) && string.IsNullOrWhiteSpace(goal.TempUserId))
            return BadRequest(new { message = "userId OR tempUserId is required" });

        var ownerUserId = string.IsNullOrWhiteSpace(goal.UserId) ? null : goal.UserId;
        var ownerTempUserId = ownerUserId is null
            ? (string.IsNullOrWhiteSpace(goal.TempUserId) ? Guid.NewGuid().ToString("N") : goal.TempUserId)
            : null;

        var createdAtUtc = goal.CreatedAt.HasValue
            ? DateTime.SpecifyKind(goal.CreatedAt.Value, DateTimeKind.Utc)
            : DateTime.UtcNow;

        var docData = new Dictionary<string, object?>
        {
            ["text"] = goal.Text,
            ["periodicity"] = goal.Periodicity,
            ["userId"] = ownerUserId,
            ["tempUserId"] = ownerTempUserId,
            ["createdAt"] = Google.Cloud.Firestore.Timestamp.FromDateTime(createdAtUtc)
        };

        var goalRef = await _db.Collection("goals").AddAsync(docData);

        return Created(string.Empty, new
        {
            id = goalRef.Id,
            text = goal.Text,
            periodicity = goal.Periodicity,
            userId = ownerUserId,
            tempUserId = ownerTempUserId,
            createdAt = createdAtUtc
        });
    }


    [HttpGet]
    public async Task<IActionResult> GetGoals([FromQuery] string? userId, [FromQuery] string? tempUserId)
    {
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(tempUserId))
            return BadRequest(new { message = "User ID or Temp User ID is required" });

        Query query = _db.Collection("goals"); // Change type to Query
        query = !string.IsNullOrWhiteSpace(userId)
            ? query.WhereEqualTo("userId", userId)
            : query.WhereEqualTo("tempUserId", tempUserId);

        var snapshot = await query.GetSnapshotAsync();

        var goals = snapshot.Documents.Select(doc =>
        {
            DateTime? ToUtc(string field)
            {
                if (!doc.ContainsField(field)) return null;
                if (doc.TryGetValue<object>(field, out var raw))
                {
                    if (raw is Google.Cloud.Firestore.Timestamp ts) return ts.ToDateTime().ToUniversalTime();
                    if (raw is DateTime dt) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                }
                return null;
            }

            doc.TryGetValue<string>("text", out var text);
            doc.TryGetValue<string>("periodicity", out var periodicity);
            doc.TryGetValue<string>("userId", out var userIdField);
            doc.TryGetValue<string>("tempUserId", out var tempUserIdField);

            return new
            {
                id = doc.Id,
                text = text ?? string.Empty,
                periodicity = periodicity ?? "diaria",
                userId = userIdField,
                tempUserId = tempUserIdField,
                createdAt = ToUtc("createdAt")
            };
        });

        return Ok(goals);
    }


    [HttpDelete("{goalId}")]
    public async Task<IActionResult> DeleteGoal(string goalId, [FromQuery] string? userId, [FromQuery] string? tempUserId)
    {
        var doc = _db.Collection("goals").Document(goalId);
        var snapshot = await doc.GetSnapshotAsync();

        if (!snapshot.Exists)
            return NotFound(new { message = "Goal not found" });

        string? docUserId = snapshot.ContainsField("userId") ? snapshot.GetValue<string>("userId") : null;
        string? docTempUserId = snapshot.ContainsField("tempUserId") ? snapshot.GetValue<string>("tempUserId") : null;

        bool authorized =
            (!string.IsNullOrEmpty(userId) && userId == docUserId) ||
            (!string.IsNullOrEmpty(tempUserId) && tempUserId == docTempUserId);

        if (!authorized)
            return Unauthorized(new { message = "Unauthorized" });

        await doc.DeleteAsync();
        return Ok(new { message = "Goal deleted successfully" });
    }

}