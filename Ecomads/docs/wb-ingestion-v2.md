# WB ingestion v2

## Dates and snapshots

Reporting days and refresh slots use Europe/Moscow. Request/response instants use UTC.
`WbDates.TryInstant` accepts ISO timestamps only with `Z` or an explicit offset. It never
falls back to the server's local timezone. Daily report dates remain calendar labels.
The parser version is `WbIngestionVersion.Current`; increment it when changing replay rules.

Expenses use target parts of at most 29 calendar days. `WbExpenseWindow` pads each part by
one day on each side, with the right edge capped at the current Moscow day. This gives at
most 31 requested days. Rows outside the target are retained in `wb_cost_observations`.
Two identical rows in one response remain two operations. Responses for overlapping
windows are alternative snapshots and are never added together. New expense jobs start
with the latest 29-day part so the current summary does not wait for older history.

The HTTP handler captures request parameters, job state, attempt/page, UTC instants, status,
body, checksum and parser version. Authorization headers and tokens are not journal fields.
Before publishing to PostgreSQL, `WbResponseJournal` writes the response to disk with a
durable flush. The spool is recovered after database/process failures. Production requires
the `wb_response_journal` volume mounted at `/var/lib/ecomads/wb-journal`; do not remove it
on rollback. A parse/import failure cannot roll back the independently saved response.

Publication, source quality and job advancement use one transaction and a per-store lock.
Reprocessing the same response/version is idempotent. PostgreSQL JSONB job state is compared
structurally. Older request timestamps cannot overwrite newer accepted observations.

## Quality

| State | Meaning |
| --- | --- |
| `data` | Accepted numeric data |
| `zero` | Explicit numeric zero |
| `no_operations` | No expense operations in the received history; not proof of zero spend |
| `no_data` | Missing day, campaign, article or response portion |
| `partial` | An incomplete snapshot; old rows are retained as stale |
| `ambiguous` | An uninterpretable date/value or inconsistent coverage |
| `revision` | A lower proposed expense total; previous spend is retained as stale |
| `accepted` | An owner accepted the current expense recalculation |
| `error` | Transport, HTTP or parsing failure |
| `legacy` | Data from the previous processor; requires rechecking |

Only `data`, `zero` and `accepted` are confirmed. Job completion means processing has
finished, and does not certify completeness. Summary collection and full readiness are
separate. Partial summaries identify missing sources/campaigns. Unconfirmed account spend
or order totals produce an unavailable account DRR. Site, Telegram and recommendations
consume source quality. Telegram reads a consistent database snapshot before claiming
a delivery; the existing delivery record prevents automatic duplicate sends.

Cluster planning rechecks legacy completions that have no v2 quality evidence. A processed
sparse response counts as an attempt and waits for normal scheduled refresh/retry rather
than creating another historical job on every recommendation pass. Recommendation gaps
show the number of unconfirmed days instead of promising an unspecified background load.
Fresh fullstats/cluster versions also invalidate the recommendation cards in the browser.

An empty/partial cluster response cannot erase the previously retained snapshot. Jam is
a limited list of search queries; it does not certify all search traffic. Funnel product
pages are accumulated privately and published only after a terminal page; repeated product
IDs across pages invalidate the chain. Old page chains without product provenance restart.

## Owner endpoints

All routes below require the store owner:

- `GET /api/wb/data-state?startDate=...&endDate=...`: source quality, reasons, attempt/accepted
  instants, response references and previous/proposed amounts.
- `GET /api/wb/stores/{store}/ingestion/snapshots/{snapshot}`: saved response and metadata.
- `POST /api/wb/stores/{store}/ingestion/snapshots/{snapshot}/replay`: replay without WB;
  matching queue progress is advanced atomically.
- `GET /api/wb/stores/{store}/ingestion/expenses/{date}/evidence`: alternative per-response
  operations/totals for a Moscow day, including boundary observations.
- `POST /api/wb/stores/{store}/ingestion/expenses/{date}/accept-recalculation`, body
  `{ "snapshotId": "..." }`: accept only the current `revision`, with an audit record.
  A stale snapshot or an ambiguous date cannot be accepted this way.

Processed, superseded responses expire after 90 days. Current quality references, the
previous accepted version of a disputed value, unresolved observations, unprocessed
responses, decision evidence and required pagination ancestors remain protected.

## Controlled rollout

1. Finish the integration suite, review the additive migration, and back up the database.
2. Before starting v2 on production, set `WB_INGESTION_STORE_IDS` to the hygiene/cleaning
   store only. A nonempty invalid list enables no stores. Empty means all stores.
3. Set `WB_INGESTION_RECHECK_DAYS=31`. The planner submits legacy expenses, advertising
   and order totals for the last 31 closed Moscow days through the existing monthly queues.
   It waits for active work, preserves pauses and reserves no WB quota while planning.
   Fresh seven-day orders are queued separately through `funnel_recent`, even if the
   historical monthly job is already running. Expense parts and monthly order pairs start
   with the latest dates. Failed rechecks remain visible and use the ordinary retry action.
4. Keep the 06:15 and 18:15 Moscow slots and existing per-method limits. Follow the selected
   store for a complete daily cycle. Resolve unexplained differences using saved evidence;
   missing responses remain explicit missing data.
5. After successful verification, set the recheck setting back to `0`, then expand the store
   allowlist. Enabling additional stores is a separate rollout step, not a timer.
6. Roll back the application image if needed. Keep the additive journal schema, spool volume
   and latest accepted data. The migration intentionally refuses destructive schema rollback.

No deployment or daily-cycle acceptance is implied by a passing local build.

## Tests

The default integration fixture uses a disposable PostgreSQL 16 container. An isolated
PostgreSQL can instead be supplied through `ECOMADS_TEST_POSTGRES`; the fixture creates
random `ecomads_test_*` databases. Never point that variable at the production database.
Explicit migration-upgrade tests still exercise EF's migrator. For an isolated server
reached through SSH, fresh-schema setup can apply EF-generated migration SQL as one batch.

```powershell
dotnet test Ecomads.WebApplication.Tests/Ecomads.WebApplication.Tests.csproj --no-restore
```

`WbHttpScenario` provides ordered HTTP replies/errors and rejects unexpected extra requests.
Tests cover UTC offsets, day/month/year/leap boundaries, padded windows, explicit zero vs
missing fields, duplicate operations/pages, replay, older responses, decreasing expense,
HTTP errors/Retry-After, database rollback/restart, owner isolation, manual decisions,
retention/page ancestry, partial summaries and delivery deduplication.

WB does not provide a universal completeness proof for an arbitrary response. The guarantee
is retained evidence, repeatable processing and explicit uncertainty, not guessed zeroes.
