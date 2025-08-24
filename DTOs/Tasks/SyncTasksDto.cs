namespace TarefistaApi.DTOs.Tasks;

public record SyncTasksDto(List<TaskDto> Tasks, string UserId);
