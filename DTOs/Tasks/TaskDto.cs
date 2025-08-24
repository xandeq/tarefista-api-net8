namespace TarefistaApi.DTOs.Tasks;

public record TaskDto
{
    public string Text { get; init; }
    public bool Completed { get; init; }
    public string TempUserId { get; init; }
    public bool IsRecurring { get; init; }
    public string RecurrencePattern { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
