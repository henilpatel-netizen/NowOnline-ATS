using Ats.Api.Authentication;
using Ats.Api.Models.Feed;
using Ats.Application.Integration;
using Ats.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Api.Controllers;

[ApiController]
[Route("jobs")]
[ServiceFilter(typeof(FeedApiKeyFilter))]
public class FeedController : ControllerBase
{
    private readonly IVacancyFeedRepository _feed;
    public FeedController(IVacancyFeedRepository feed) => _feed = feed;

    [HttpPost("search")]
    public async Task<FeedResponse> Search([FromQuery] int per_page = 100, [FromQuery] int page = 1)
    {
        if (per_page <= 0) per_page = 100;
        if (page <= 0) page = 1;

        var (jobs, total) = await _feed.GetPageAsync(page, per_page);

        var response = new FeedResponse { Total = total, Count = jobs.Count };
        foreach (var j in jobs)
        {
            response.Embedded.Jobs.Add(new FeedJob
            {
                Id = j.ExternalRef,
                Type = "H",
                Title = j.Title,
                Location = new FeedLocation { City = j.Location?.City ?? j.Location?.Name },
                Embedded = new FeedJobEmbedded
                {
                    Status = new FeedStatus { Title = j.Status == JobStatus.Published ? "Actief" : "Gesloten" }
                }
            });
        }
        return response;
    }
}
