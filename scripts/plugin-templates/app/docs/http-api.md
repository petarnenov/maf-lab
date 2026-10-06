The $title page's one route (any signed-in role).

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/$name/summary` | — | `{ conversations, moreThanAPage }` — how many of the caller's own conversations there are (up to 100); `401` without a token |
