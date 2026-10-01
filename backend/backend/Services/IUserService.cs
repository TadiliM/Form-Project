using backend.Models.Dtos;

namespace backend.Services;

public interface IUserService
{
    Task<UserProfileDto> GetProfileAsync(Guid userId);

    Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
}
