# ReferralTool Integration Contract (FROZEN)

This is the complete, frozen contract the Ats product must satisfy to integrate with ReferralTool.
It was extracted from the ReferralTool codebase at design time (2026-06-26) and is the **source of
truth** — do not re-derive it from the ReferralTool repo. If ReferralTool ever changes these contracts,
that is a deliberate event that must be reflected here explicitly.

Two directions:
- **Appendix A** — Ats serves a CatsOne-compatible vacancy feed; ReferralTool pulls it.
- **Appendix B** — Ats pushes candidate status updates to ReferralTool.
- **Appendix C** — code-prefix + URL-parameter rules shared by both.

---

# Appendix A -- VACANCY FEED (Ats serves, ReferralTool pulls)

Source of truth at design time:
- `Logic/Import/CatsOne/CatsOneImportClient.cs`
- `Logic/Import/CatsOne/CatsOneMapper.cs`
- `Logic/Import/ImportVacancyDto.cs`

**Request ReferralTool makes (the Ats API must answer this):**
- Method: `POST`
- URL: `{ImportSetting.ApiUrl}/jobs/search?per_page=100&page={page}` (page increments until a page
  returns fewer than 100 rows)
- Auth header: `Authorization: Token {ImportSetting.ApiKey}` (static API key the Ats issues per tenant)
- Body (filter for published jobs):
  ```json
  { "and": [ { "field": "is_published", "filter": "exactly", "value": true } ] }
  ```

**Response the Ats must return:**
```json
{
  "count": 1,
  "total": 1,
  "_embedded": {
    "jobs": [
      {
        "id": "JOB-1042",
        "type": "H",
        "title": "Senior .NET Developer",
        "location": { "city": "Amsterdam" },
        "_embedded": {
          "status": { "title": "Actief" },
          "custom_fields": [
            { "name": "Aantal uren.", "value": "32-40" }
          ]
        }
      }
    ]
  }
}
```

Mapping applied by ReferralTool's `CatsOneMapper`:

| Feed field | ReferralTool field | Notes |
|---|---|---|
| `id` | `ExternalId` | Must equal `Job.ExternalRef`. Stable, unique per tenant. |
| `title` | Vacancy title | |
| `type` | (filter) | Row is SKIPPED unless `type` is `H`, `C2H`, or `FL`. Emit `H` by default. |
| `location.city` | `Location` | |
| `_embedded.status.title` | status | `"Actief"` = active; anything else marks the vacancy deleted/inactive. |
| `_embedded.custom_fields[name="Aantal uren."]` | MinHours/MaxHours | Optional. `"min-max"` or single value. |
| (derived by ReferralTool) | Vacancy `Url` | `ImportSetting.VacancySiteUrlTemplate.Replace("{vacancyId}", id)`. Point template at the Ats career site. |

---

# Appendix B -- STATUS UPDATE (Ats pushes to ReferralTool)

Source of truth at design time:
- `Api/Controllers/KafkaController.cs`
- `Api/Models/KafkaCreateCandidatePayload.cs`

**Request the Ats makes:**
- Method: `POST`
- Route: `candidatestatusupdate` (controller action `[HttpPost("candidatestatusupdate")]`; confirm full
  versioned path, expected `/v1.0/.../candidatestatusupdate`, before Phase 3)
- Auth header: `X-Auth-Token: {token}` -- compared by ReferralTool (direct string equality) against
  config key `Kafka:AuthToken`.
- Body (`KafkaCreateCandidatePayload`):

  | Field | Type | Required | Max len | Ats source |
  |---|---|---|---|---|
  | `CustomerId` | int | yes | - | `TenantSettings.ReferralToolCustomerId` |
  | `Code` | string | yes | 36 | `Application.SourceCode` (referrer `1...` or referral `2...`) |
  | `ExternalVacancyId` | string | yes | 36 | `Job.ExternalRef` |
  | `ExternalCandidateId` | string | yes | 36 | Candidate external id (`Candidate.Key` as 36-char GUID) |
  | `CandidateStatus` | string | no | - | mapped stage name |

  ```json
  {
    "customerId": 42,
    "code": "1RR123456",
    "externalVacancyId": "JOB-1042",
    "externalCandidateId": "c-5f6897d3",
    "candidateStatus": "1st Interview"
  }
  ```

**ReferralTool-side behavior the Ats depends on:**
- First status for an unknown `ExternalCandidateId` creates the Candidate, resolving the referrer from
  `Code` (prefix `1` = referrer code, prefix `2` = referral code).
- `ExternalVacancyId` must already exist as a ReferralTool Vacancy `ExternalId` (i.e. the job must have
  been imported via Appendix A first).
- Duplicate guard: the same event type on the same candidate is rejected; do not resend an identical stage.
- `CandidateStatus` must match a seeded `CustomerEventType.Type` for that customer (case-insensitive),
  else ReferralTool rejects it.
- Delivery must be **ordered per application** (the first event creates the candidate at that stage);
  send event N+1 only after N is confirmed.

---

# Appendix C -- Code prefix + URL parameter rules

- Referrer code prefix `"1"`, referral code prefix `"2"`, both 9 chars.
- The career-site referral query parameter name is set by ReferralTool's `Customer.CodeParameterName`
  (default `ref`). The Ats career site must read whatever parameter name the tenant configures and
  store its value verbatim on `Application.SourceCode`.
