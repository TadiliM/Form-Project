using backend.Models.Enums;

namespace backend.Models.Dtos;

/// <summary>
/// Profile of the signed-in user. Used in particular to re-read the up-to-date
/// <see cref="PlanType"/> from the database, since the JWT one is frozen at login.
/// </summary>
public class UserProfileDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PlanType PlanType { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdateProfileRequest
{
    public string Name { get; set; } = string.Empty;
}
