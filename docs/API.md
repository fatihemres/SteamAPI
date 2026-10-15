# SteamAPI — Technical API Specification

**Endpoint:** `POST /steam`  
**API Version:** v0.2.0 (Release Candidate)  
**Protocol:** HTTP/1.1, HTTP/2 (depending on host environment and client TLS negotiation)  
**Media Type:** `application/json`  
**Error Standard:** RFC 9457 / RFC 7807 (Problem Details for HTTP APIs)  

---

## 1. Endpoint Overview

The `POST /steam` endpoint accepts search criteria, forwards an encoded autocomplete suggestion query to the Steam store search service (`https://store.steampowered.com/search/suggest`), parses the resulting HTML markup, and returns an array of matched game listings.

### Headers

| Header | Expected Value | Description |
| :--- | :--- | :--- |
| `Content-Type` | `application/json` | Media type of the request payload. |
| `Accept` | `application/json` | Requested media type for the response. |

---

## 2. Request Schema

### Model: `GetSteamGameInfoRequestModel`

```json
{
  "game": "Portal",
  "culture": 2
}
```

### Property Constraints

| Property | Type | Nullable | Required | Constraints | Description |
| :--- | :---: | :---: | :---: | :--- | :--- |
| `game` | `string` | **No** | **Yes** | Min length: 1<br>Max length: **200 UTF-16 code units**<br>Non-whitespace | The title or keyword to query on Steam. |
| `culture` | `integer` | **No** | **Yes** | Valid enum values: `1`, `2`, `3` | The Steam regional store and language mapping. |

#### Notes on the `game` Property
- **Length Constraint:** The maximum length of 200 corresponds to C# `string.Length` (UTF-16 code units). Supplementary Unicode characters (such as certain emojis, mathematical symbols, and historic scripts) are represented as surrogate pairs and count as two UTF-16 code units. Requests exceeding this limit return HTTP 400 Bad Request.
- **Character Encoding:** Search strings are safely encoded into query strings using standard RFC 3986 percent-encoding (`Uri.EscapeDataString`). Titles containing spaces, symbols (e.g. `C++`, `Half-Life`), and non-ASCII scripts (e.g. `Türkçe`, Cyrillic, CJK) are fully supported.
- **Search Behavior:** Steam processes search queries according to Valve's internal suggestion service logic. Search results may include approximate or partial matches, and exact title matching is not guaranteed.

#### Supported Culture Identifiers

| Value | Identifier | Target Country (`cc`) | Target Language (`lang`) | Description |
| :---: | :--- | :---: | :---: | :--- |
| `1` | `tr_TR` | `tr` | `turkish` | Turkish regional store and localized descriptions. |
| `2` | `en_US` | `us` | `english` | United States regional store and English descriptions. |
| `3` | `es_ES` | `es` | `spanish` | Spanish regional store and Spanish descriptions. |

Any other integer (e.g. `0`, `4`, `-1`, `99`) or non-integer value is rejected with HTTP 400 Bad Request.

---

## 3. Successful Response Schema (`200 OK`)

A successful request returns a top-level JSON array of `Listing` objects directly, without an outer envelope wrapper.

```json
[
  {
    "name": "Portal 2",
    "storeLink": "https://store.steampowered.com/app/620/Portal_2",
    "imageLink": "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/620/bd23943dcd0280aa2cdc066e2f86d2185cfd4cce/capsule_sm_120.jpg",
    "price": 9.99,
    "saleType": 0,
    "appId": "620"
  }
]
```

### Listing Object Fields

| Field | JSON Type | Nullable | Description |
| :--- | :---: | :---: | :--- |
| `name` | `string` | No | HTML-decoded display title of the game or software. |
| `storeLink` | `string` | No | Extracted upstream Steam store URL with tracking query parameters and trailing slash removed where applicable. Canonical URL normalization across redirects is not guaranteed. |
| `imageLink` | `string` | No | Direct URL string to the small capsule header image on Steam CDN. Defaults to an empty string (`""`) if no image element or URL attribute is extracted from the upstream markup. |
| `price` | `number` | **Yes** | Extracted numeric price. Non-null for paid games (`saleType: 0`); `null` for free games (`saleType: 1`) or unavailable pricing (`saleType: 2`). |
| `saleType` | `integer` | No | Enumerated classification of the listing's pricing model. |
| `appId` | `string` | No | Numeric string identifier corresponding to the Steam Application ID. |

### Sale Type Enumeration (`sType`)

| Value | Name | Conditions |
| :---: | :--- | :--- |
| `0` | `CostsMoney` | Upstream HTML displays a recognized monetary price format (e.g. `$9.99`, `9,69€`). |
| `1` | `FreeToPlay` | Upstream HTML indicates free access (e.g. "Free to Play", "Free", "Ücretsiz", "Gratis"). |
| `2` | `NotAvailable` | Pricing element is absent, unparseable, or denotes an unpriced announcement. |

### Important Currency Representation Disclaimer
The `price` field carries a numeric floating-point value **without a currency symbol or ISO 4217 currency code**.
- The requested `culture` parameter controls the Steam store language and regional catalog; **it does not guarantee the currency**.
- For example, Steam's Turkish regional store (`culture: 1`) denominates games in **USD**, not Turkish Lira.
- Developers should not assume or attempt automatic currency conversions without verifying the currency source externally.

### Zero Search Results
When Steam returns no matching suggestions for a valid term, the endpoint returns **HTTP 200 OK** with an empty JSON array:

```json
[]
```

---

## 4. Error Responses (RFC 9457 Problem Details)

Handled validation, upstream network, markup parsing, and timeout exceptions return standardized JSON payloads adhering to RFC 9457 / RFC 7807 with appropriate Content-Type headers (`application/problem+json` or `application/json`). Unhandled framework exceptions follow default ASP.NET Core error pipelines.

### 4.1. HTTP 400 Bad Request

Returned when the incoming request fails model binding or validation rules.

#### Example: Search Term Exceeds 200 Code Units

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Game": [
      "The Game field cannot exceed 200 characters."
    ]
  }
}
```

#### Example: Unsupported Culture Integer

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Culture": [
      "The Culture value '99' is invalid. Supported values are: tr_TR (1), en_US (2), es_ES (3)."
    ]
  }
}
```

### 4.2. HTTP 502 Bad Gateway

Returned when the upstream Steam service fails or returns an unprocessable response.

```json
{
  "status": 502,
  "title": "Bad Gateway",
  "detail": "The upstream Steam search service returned an error or was unreachable."
}
```

#### Triggers for HTTP 502:
1. **Network Connectivity Failures:** DNS resolution failure, connection timeout, connection reset, or SSL/TLS negotiation fault connecting to `store.steampowered.com`.
2. **Upstream Non-Success HTTP Responses:** Steam returns non-success HTTP status codes (such as `404 Not Found`, `429 Too Many Requests`, `500 Internal Server Error`, or `503 Service Unavailable`).
3. **Unparseable Steam Markup (`SteamResponseParseException`):** Steam suggestion markup contains anchor elements with class `match`, but none of the matched elements can be parsed into valid listings (detail: *"The upstream Steam search service returned unparseable or unrecognized markup."*).
4. **Refused Upstream Redirects:** Steam returns an unexpected HTTP 301/302 redirect response. Because automatic redirects are disabled, the unhandled redirect status is cleanly mapped to 502.
5. **Upstream Response Payload Overflow:** Steam (or an intermediate proxy) returns a response body exceeding the configured 1 MiB buffering limit.

### 4.3. HTTP 504 Gateway Timeout

Returned when the upstream Steam service fails to complete the request within the configured 30-second timeout window.

```json
{
  "status": 504,
  "title": "Gateway Timeout",
  "detail": "The upstream Steam search service did not respond within the configured timeout."
}
```

### 4.4. Caller-Initiated Request Cancellation

When a client aborts the incoming HTTP request (via connection reset or cancellation token), the API stops downstream processing promptly. The cancellation is propagated without synthesizing a 502 or 504 response.

---

## 5. Upstream Policies and Resource Hardening

The backend service applies strict boundaries on all outbound communication with Valve's infrastructure:

- **Target Base URL:** `https://store.steampowered.com/` (fixed, unconfigurable at request time).
- **Hard Timeout:** 30 seconds (`client.Timeout = TimeSpan.FromSeconds(30)`).
- **Redirect Policy:** Automatic redirect following is explicitly disabled (`AllowAutoRedirect = false` on `SocketsHttpHandler`). Cross-host redirects are never followed.
- **Maximum Buffer Size:** Outbound response buffering is capped at **1 MiB** (`MaxResponseContentBufferSize = 1,048,576 bytes`).
- **Retry Policy:** No automatic retries are executed to avoid compounding upstream load.

---

## 6. Known Limitations & Deployment Notes

1. **Unrecognized Upstream Markup:**
   - If Valve alters the HTML layout of the suggestion service entirely such that **no** elements match `<a class="match">` (e.g. replacing them with custom web components or returning a full HTML challenge page with HTTP 200), the parser cannot distinguish this structural change from a legitimate zero-result query and returns `[]`.
   - The dedicated `SteamResponseParseException` detects cases where `match` elements exist but attributes or internal nodes are malformed.
2. **Scraping Fragility:**
   - Because this service interacts with an undocumented HTML suggestion endpoint rather than an authenticated enterprise API, changes to Valve's client-side suggestion markup can affect metadata extraction.
3. **Rate Limiting & Gateway Deployment:**
   - Application-level rate limiting is not implemented in this milestone. Production deployments should sit behind an API gateway or reverse proxy providing request rate limiting and monitoring.

---

## 7. Interactive Documentation (Swagger / OpenAPI)

Interactive OpenAPI (Swagger) documentation is provided for rapid local exploration:

- **Swagger UI:** `https://localhost:7030/swagger/index.html` (or HTTP port if HTTPS redirection is bypassed)
- **OpenAPI v3 Specification:** `https://localhost:7030/swagger/v1/swagger.json`

> **Note on Environment:** Swagger generation and the Swagger UI middleware are registered conditionally using `app.Environment.IsDevelopment()`. They are enabled in development environments and disabled in production deployments by default.
