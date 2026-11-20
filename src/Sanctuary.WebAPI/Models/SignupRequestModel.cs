using System.ComponentModel.DataAnnotations;

namespace Sanctuary.WebAPI.Models;

public class SignupRequestModel
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters long.")]
    [RegularExpression(@"^[a-zA-Z0-9_.]+$", ErrorMessage = "Username can only contain letters, numbers, underscores, and dots.")]
    public required string Username { get; set; }

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be between 6 and 100 characters long.")]
    [RegularExpression(@"^[\x00-\x7F]+$", ErrorMessage = "Password can only contain ASCII characters.")]
    public required string Password { get; set; }

    [Required(ErrorMessage = "Please tell us where you live.")]
    [StringLength(64, ErrorMessage = "Please choose a country from the list.")]
    public required string Country { get; set; }

    [Range(1, 100, ErrorMessage = "Please tell us how old you are.")]
    public int Age { get; set; }

    [Required(ErrorMessage = "Please enter your e-mail address.")]
    [StringLength(254, ErrorMessage = "That e-mail address is too long.")]
    [EmailAddress(ErrorMessage = "Please enter a valid e-mail address.")]
    public required string Email { get; set; }

    public bool EmailOptIn { get; set; }

    public bool AcceptTerms { get; set; }

    public SignupCharacterModel? Character { get; set; }
}

public class SignupCharacterModel
{
    public int Preset { get; set; }

    public required string FirstName { get; set; }

    public required string LastNamePrefix { get; set; }

    public required string LastNameSuffix { get; set; }
}
