using System.ComponentModel.DataAnnotations;

namespace Pentagon.Functions.Models;

public class ProcessImageRequest
{
    [Required]
    [Url]
    public required string ImageUrl { get; init; }

    [EmailAddress]
    public string? Email { get; init; }

    [Phone]
    public string? Phone { get; init; }
}
