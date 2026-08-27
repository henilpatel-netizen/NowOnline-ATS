using System.ComponentModel.DataAnnotations;
using Ats.Domain.Entities;

namespace Ats.Web.Models;

public class StageRow
{
    public int? Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
    public int Order { get; set; }
    public bool IsTerminal { get; set; }
    public StageOutcome TerminalOutcome { get; set; } = StageOutcome.None;
    [StringLength(120)] public string? ReferralStatusOverride { get; set; }
    public bool Delete { get; set; }
}

public class PipelineEditViewModel
{
    public int? Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
    public List<StageRow> Stages { get; set; } = new();

    // Optimistic-concurrency token, round-tripped as base64 through a hidden field.
    public byte[]? RowVersion { get; set; }
}
