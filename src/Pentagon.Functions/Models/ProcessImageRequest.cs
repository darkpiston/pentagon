using System.ComponentModel.DataAnnotations;

namespace Pentagon.Functions.Models;

public class ProcessImageRequest
{
    [Required]
    [Url]
    public required string ImageUrl { get; init; }

    [Required]
    [EmailAddress]
    public required string Email { get; init; }
}
