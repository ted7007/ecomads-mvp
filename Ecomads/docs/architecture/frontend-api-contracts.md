# Frontend API contracts

Этот документ фиксирует API-поверхность, которую новый React-фронтенд должен использовать без изменения backend endpoints.

## Auth

### `POST /api/auth/login`

Request:

```json
{
  "email": "user@example.com",
  "password": "password"
}
```

Response:

```json
{
  "token": "jwt",
  "sellerId": "guid",
  "name": "User Name",
  "email": "user@example.com"
}
```

Side effects:

- Store JWT in `localStorage.ecomads_token`.
- Store user in `localStorage.ecomads_user`.

### `POST /api/auth/register`

Request:

```json
{
  "name": "User Name",
  "email": "user@example.com",
  "password": "password"
}
```

Response shape is equivalent to login token response.

### `GET /api/auth/me`

Headers:

```text
Authorization: Bearer <token>
```

Used for auth check and current user loading.

## Campaigns

### `GET /api/projects`

Optional query:

```text
startDate=YYYY-MM-DD
endDate=YYYY-MM-DD
```

Expected response:

```json
[
  {
    "id": "guid",
    "name": "Campaign name",
    "kpi": {
      "spend": 1000,
      "revenue": 5000,
      "orderedAmount": 5000,
      "drr": 20,
      "clicks": 100,
      "ctr": 3.5
    }
  }
]
```

## Statistics

### `GET /api/statistics/periods`

Expected response:

```json
[
  {
    "startDate": "2026-01-01T00:00:00Z",
    "endDate": "2026-01-31T00:00:00Z"
  }
]
```

### `GET /api/statistics/keywords/{campaignId}`

Optional query:

```text
startDate=YYYY-MM-DD
endDate=YYYY-MM-DD
```

Expected response:

```json
[
  {
    "phrase": "keyword",
    "frequency": 1000,
    "cpm": 120,
    "avgPosition": 4.5,
    "impressions": 10000,
    "clicks": 200,
    "ctr": 2,
    "spend": 3000,
    "orders": 10,
    "revenue": 15000,
    "drr": 20
  }
]
```

### `POST /api/statistics/upload`

Content type:

```text
multipart/form-data
```

Form fields:

- `file`
- `startDate`
- `endDate`

Do not manually set `Content-Type` from frontend when sending `FormData`.

### `POST /api/statistics/upload-with-keywords`

Content type:

```text
multipart/form-data
```

Form fields:

- `file`
- `keywordsFile`
- `startDate`
- `endDate`

### `POST /api/statistics/upload-keywords`

Content type:

```text
multipart/form-data
```

Form fields:

- `file`
- `startDate`
- `endDate`
- `campaignId`

## Recommendations

### `GET /api/recommendations/campaign/{campaignId}`

Returns recommendations for one campaign.

### `POST /api/recommendations/generate`

Request:

```json
{
  "campaignId": "guid",
  "goal": "рост прибыли"
}
```

### `PUT /api/recommendations/{id}/status`

Request:

```json
{
  "status": "принята",
  "userComment": "comment"
}
```

Valid statuses currently used by legacy frontend:

- `принята`
- `отложена`
- `отклонена`

### `GET /api/recommendations/stats`

Query:

```text
period=week|month|quarter|year
```

Expected response:

```json
{
  "counts": {
    "accepted": 1,
    "pending": 2,
    "rejected": 3
  },
  "monthly": [
    {
      "month": "июн.",
      "accepted": 1,
      "pending": 2,
      "rejected": 3,
      "total": 6
    }
  ],
  "recommendations": [
    {
      "id": "guid",
      "text": "Recommendation text",
      "status": "новая",
      "date": "2026-06-05T00:00:00Z",
      "campaign": "Campaign name",
      "comment": "User comment"
    }
  ]
}
```

## Frontend validation rule

Every endpoint used by React should have:

- TypeScript type.
- Zod schema for response validation.
- A small adapter if backend names/statuses differ from UI names.
