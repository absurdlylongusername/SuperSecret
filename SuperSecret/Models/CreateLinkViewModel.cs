using SuperSecret.Validators;
using System.ComponentModel.DataAnnotations;
using static SuperSecret.Validators.ValidationMessages;

namespace SuperSecret.Models;

public class CreateLinkViewModel
{
    [Required(ErrorMessage = UsernameRequired)]
    [RegularExpression(@"^[A-Za-z0-9]{1,50}$", ErrorMessage = UsernameLengthAlphanumeric)]
    [StringLength(50, MinimumLength = 1)]
    public string Username { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = MaxClicksMinimum)]
    [Display(Name = "Max Clicks")]
    public int Max { get; set; } = 1;

    [Display(Name = "Expires At")]
    [DataType(DataType.DateTime)]
    [DisplayFormat(DataFormatString = "{0:yyyy-MM-ddTHH:mm}", ApplyFormatInEditMode = true)]
    public DateTimeOffset? ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(1);
}