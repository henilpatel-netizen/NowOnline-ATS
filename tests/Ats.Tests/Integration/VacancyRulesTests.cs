using Ats.Application.Integration;
using Xunit;

namespace Ats.Tests.Integration;

// Vacancy sync sends current state, so a wrong decision either duplicates, loses or wrongly
// deactivates a vacancy referrers can see.
public class VacancyRulesTests
{
    [Theory]
    [InlineData(true, false, VacancyCall.Update)]
    [InlineData(true, true, VacancyCall.Update)]    // closed/deleted: PUT Inactive, never DELETE
    [InlineData(false, false, VacancyCall.Create)]
    [InlineData(false, true, VacancyCall.None)]     // nothing to deactivate
    public void Decides_the_call_from_existence_and_state(bool exists, bool inactive, VacancyCall expected) =>
        Assert.Equal(expected, ReferralToolRules.DecideVacancyCall(exists, inactive));

    private const string Validation =
        """{"title":"Creating vacancy failed due to validation errors","status":400,"errors":{"Title":["too long"]}}""";
    private const string NotUnique = """{"title":"Id is not unique","status":400,"errors":{}}""";

    [Theory]
    [InlineData(true, 200, null, OutboxOutcome.Delivered)]
    [InlineData(false, 0, null, OutboxOutcome.Transient)]
    [InlineData(true, 500, null, OutboxOutcome.Transient)]
    [InlineData(true, 401, null, OutboxOutcome.Postpone)]
    [InlineData(true, 403, null, OutboxOutcome.Postpone)]
    [InlineData(true, 400, Validation, OutboxOutcome.Failed)]
    [InlineData(true, 400, NotUnique, OutboxOutcome.Transient)]   // raced a create; next run PUTs
    [InlineData(true, 400, "not json", OutboxOutcome.Transient)]
    [InlineData(true, 404, null, OutboxOutcome.Transient)]
    public void Classifies_a_vacancy_call(bool reached, int status, string? body, OutboxOutcome expected) =>
        Assert.Equal(expected, ReferralToolRules.ClassifyVacancyCall(reached, status, body));
}
