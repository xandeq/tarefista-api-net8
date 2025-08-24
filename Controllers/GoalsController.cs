using FirebaseAdmin;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;

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
        if (string.IsNullOrEmpty(goal.UserId))
            return BadRequest(new { message = "User ID is required" });

        var newGoal = new
        {
            text = goal.Text,
            periodicity = goal.Periodicity,
            userId = goal.UserId,
            createdAt = Timestamp.GetCurrentTimestamp()
        };

        var goalRef = await _db.Collection("goals").AddAsync(newGoal);
        return Created("", new { id = goalRef.Id, goal = newGoal });
    }

    [HttpGet]
    public async Task<IActionResult> GetGoals([FromQuery] string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { message = "User ID is required" });

        var snapshot = await _db.Collection("goals")
                                .WhereEqualTo("userId", userId)
                                .GetSnapshotAsync();

        var goals = snapshot.Documents.Select(doc => new { id = doc.Id, data = doc.ToDictionary() });
        return Ok(goals);
    }

    [HttpDelete("{goalId}")]
    public async Task<IActionResult> DeleteGoal(string goalId)
    {
        var doc = _db.Collection("goals").Document(goalId);
        var snapshot = await doc.GetSnapshotAsync();

        if (!snapshot.Exists)
            return NotFound(new { message = "Goal not found" });

        await doc.DeleteAsync();
        return Ok(new { message = "Goal deleted successfully" });
    }
}

public record GoalDto(string Text, string Periodicity, string UserId);
