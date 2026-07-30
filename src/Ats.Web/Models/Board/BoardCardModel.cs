using Ats.Domain.Enums;

namespace Ats.Web.Models.Board;

public sealed record BoardCardModel(
    int ApplicationId,
    string CandidateName,
    string Email,
    string RowVersion,
    ApplicationOrigin Origin,
    int DaysInStage,
    int StageIndex,
    int StageCount,
    bool Rejected);
