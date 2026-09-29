# ReferralTool Integration Contract (FROZEN)

This is the complete, frozen contract the Ats product must satisfy to integrate with ReferralTool.
It was extracted from the ReferralTool codebase at design time (2026-06-26) and is the **source of
truth** — do not re-derive it from the ReferralTool repo. If ReferralTool ever changes these contracts,
that is a deliberate event that must be reflected here explicitly.

Two directions:
- **Appendix A** -- Ats serves a CatsOne-compatible vacancy feed; ReferralTool pulls it (removed 2026-09-28, see Appendix D).
- **Appendix B** -- Ats pushes candidate status updates to ReferralTool.
- **Appendix C** -- code-prefix + URL-parameter rules shared by both.
- **Appendix D** -- Ats pushes vacancies to ReferralTool's REST vacancy API.

---

# Appendix A -- VACANCY FEED (Ats serves, ReferralTool pulls)

> **Removed 2026-09-28.** Ats no longer serves a pull feed; vacancies are pushed (Appendix D).

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
  been pushed via Appendix D first; until 2026-09-28 this was the Appendix A import).
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

---

## Verified against ReferralTool source on 2026-06-29 (supersedes the 2026-06-26 notes above)

1. Status route is `POST /v1.0/kafka/candidatestatusupdate` (controller `Kafka`, version 1.0).
2. The status endpoint requires BOTH headers: `X-Api-Key` (a ReferralTool-issued key, validated by the
   `[Authorize(ApiKey)]` scheme) AND `X-Auth-Token` (compared to `Kafka:AuthToken`). The 2026-06-26
   Appendix B documented only `X-Auth-Token`.
3. "Vacancy does not exist" returns HTTP 400 (transient until the feed import lands). A pre-flight
   `POST /v1.0/kafka/checkvacancyexists` (`{ CustomerId, ExternalVacancyId }` -> `{ exists }`, same dual
   auth) is available and is used by the worker.
4. Feed hours `custom_fields` use a nested shape: each entry's name is at
   `custom_fields[]._embedded.definition.name` (value at `[].value`), not the flat `{name,value}` shown
   in Appendix A. The Ats feed omits `custom_fields` in v1 (hours optional).

---

# Appendix D -- VACANCY PUSH (Ats pushes, ReferralTool stores)

Verified against ReferralTool source on 2026-09-28 (`Api/Controllers/VacancyController.cs`, `Api/Models/CreateVacancyModel.cs`, `Api/Models/UpdateVacancyModel.cs`, `Api/Controllers/KafkaController.cs`, `Logic/Managers/VacancyManager.cs`, `Repositories/Repositories/VacancyRepository.cs`).

| Call | Auth | Behaviour |
|---|---|---|
| `POST /v1.0/vacancy` | `X-Api-Key` (customer from the key) | Body `CreateVacancyModel`: `Id` req max 36, `Title` req max 150, `Url` req max 300, `Location` 150, `EmploymentType` 150, `MinHours`/`MaxHours` 0-168, `Education` 150, `Categories` list. 200 empty body. Existing Id -> 400 "Id is not unique". No `Inactive` field. |
| `PUT /v1.0/vacancy/{id}` | `X-Api-Key` | Body `UpdateVacancyModel`: same minus `Id`, plus `Inactive` (req). **Full replace**: omitted fields become null, `Categories` null clears them. Missing -> 400 "Vacancy does not exist". |
| `POST /v1.0/kafka/checkvacancyexists` | `X-Api-Key` + `X-Auth-Token` | `{ CustomerId, ExternalVacancyId }` -> `{ exists }`. The only clean existence check (`GET /vacancy/{id}` answers 400 for missing). Finds inactive vacancies too (`VacancyRepository.GetByCustomerIdAndExternalId` filters by `CustomerId` only, not `Inactive`), so reopening a closed job is a `PUT` with `Inactive = false`. A vacancy soft-deleted through `DELETE` is not found, which is one reason Ats never calls `DELETE`. |

Every vacancy-endpoint failure is HTTP 400 `ValidationProblemDetails`, including caught exceptions. A 400 whose `errors` object has entries is a model-validation failure (terminal); any other 400 is retried.

- **Mapping:** `Id`=`Job.ExternalRef`, `Title`=`Job.Title` (first 150 chars), `Url`=`{Integration:CareerSiteBaseUrl}/careers/{slug}/jobs/{ExternalRef}`, `Location`=`Location.City ?? Location.Name`, `EmploymentType`=Ats enum name (`FullTime`, `PartTime`, ...), `Categories`=`[Department.Name]` or `[]`, `Inactive`=`IsDeleted || Status != Published`. `MinHours`, `MaxHours`, `Education` are not sent (null).
- **Algorithm per message:** call `checkvacancyexists`; exists -> PUT; not exists and active -> POST; not exists and inactive -> nothing. Ats never calls `DELETE /v1.0/vacancy/{id}`: ReferralTool's delete renames the ExternalId to `deleted{timestamp}-{id}`, after which candidate status updates for that vacancy fail and referred candidates are orphaned. A closed or deleted Ats job is sent as `PUT` with `Inactive = true`, matching what the old importer did for vacancies missing from the feed.
- **Credentials:** both headers are copied by the operator from ReferralTool into the tenant's Ats integration settings: `X-Api-Key` = a ReferralTool `ApiKeys` row GUID for the customer; `X-Auth-Token` = ReferralTool config `Kafka:AuthToken`. Neither expires.
- **Prerequisite (ReferralTool data, not code):** set `ImportSettings.Enabled = 0` for every customer served by Ats, so the Tasker importer no longer imports or deactivates that customer's vacancies.
- **Known risk (accepted; ReferralTool unchanged):** the Kafka endpoints trust `CustomerId` from the body rather than the X-Api-Key's customer, and `X-Auth-Token` is one global value, so a holder of any valid API key plus the token can post candidate events for another customer. Keep both secrets restricted to operators.
