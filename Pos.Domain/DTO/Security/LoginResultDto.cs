namespace Pos.Domain.DTO.Security
{
    public sealed class LoginResultDto
    {
        public bool Ok { get; init; }
        public string? Error { get; init; }
        public UserInfoDto? User { get; init; }

        public static LoginResultDto Success(UserInfoDto user) => new() { Ok = true, User = user };
        public static LoginResultDto Fail(string error) => new() { Ok = false, Error = error };
    }
}
