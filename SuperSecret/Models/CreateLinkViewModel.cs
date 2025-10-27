using System.ComponentModel.DataAnnotations;

namespace SuperSecret.Models;

public class CreateLinkViewModel
{
    [Required(ErrorMessage = "Username is required")]
    [RegularExpression(@"^[A-Za-z0-9]{1,50}$", ErrorMessage = "Username must be 1-50 alphanumeric characters only")]
    [StringLength(50, MinimumLength = 1)]
    public string Username { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Max clicks must be at least 1")]
    [Display(Name = "Max Clicks")]
    public int Max { get; set; } = 1;

    [Display(Name = "Expires At")]
    [DataType(DataType.DateTime)]
    public DateTimeOffset? ExpiresAt { get; set; }
}