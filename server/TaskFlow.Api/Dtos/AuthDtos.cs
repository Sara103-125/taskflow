using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.Dtos;

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(8), MaxLength(100)] string Password,
    [Required, MaxLength(50)] string DisplayName);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record UserDto(string Id, string Email, string DisplayName);

public record AuthResponse(string Token, UserDto User);
