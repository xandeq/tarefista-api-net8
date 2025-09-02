namespace TarefistaApi.DTOs.Goals;

public class GoalDto
{
    public string? UserId { get; set; }
    public string? TempUserId { get; set; }

    public string Text { get; set; } = default!;
    public string Periodicity { get; set; } = default!;

    public DateTime? CreatedAt { get; set; }
}

