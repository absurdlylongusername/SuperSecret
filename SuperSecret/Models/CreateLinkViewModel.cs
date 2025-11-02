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

    [Display(Name = "Expires In")]
    public bool HasExpiryDate { get; set; }

    [Display(Name = "Expires At")]
    [DataType(DataType.DateTime)]
    [DisplayFormat(DataFormatString = "{0:yyyy-MM-ddTHH:mm}", ApplyFormatInEditMode = true)]
    public DateTimeOffset? ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(1);

    [Range(0, 59)]
    [Display(Name = "Seconds")]
    public int DurationInSeconds { get; set; }

    [Range(0, 59)]
    [Display(Name = "Minutes")]
    public int DurationInMinutes { get; set; }

    [Range(0, 23)]
    [Display(Name = "Hours")]
    public int DurationInHours { get; set; }

    [Range(0, 30)]
    [Display(Name = "Days")]
    public int DurationInDays { get; set; }
}