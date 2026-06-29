using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models;

public class AddCandidateViewModel
{
    [Required] public int JobId { get; set; }

    // When set, attach this existing candidate. When null, create a new candidate from the fields below.
    public int? CandidateId { get; set; }

    [StringLength(100)] public string? FirstName { get; set; }
    [StringLength(100)] public string? LastName { get; set; }
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    [StringLength(40)] public string? Phone { get; set; }
}
