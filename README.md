# SteamAPI

A lightweight ASP.NET Core 10 Web API providing structured search suggestions from the Steam store.

> **Project Status: v0.2.0 Release Candidate Under Review**<br>
> *This repository is currently undergoing final quality assurance and release candidate review. The default remote GitHub `master` branch remains v0.1.1 until modernization is approved and merged. During release candidate review, modernized code resides on the `net10_modernization_planning` branch.*<br>
> *SteamAPI is not an official Valve product and is not endorsed by or affiliated with Valve Corporation.*

---

## Overview

**SteamAPI** is an API service built on **.NET 10** (`net10.0`) that translates search queries into regional Steam store suggestion requests, parses the returned HTML markup, and provides structured JSON listings containing game identifiers, titles, store URLs, capsule imagery, pricing, and free-to-play classifications.

### Key Features

- **Regional Localization:** Support for three Steam store cultures (`tr_TR`, `en_US`, `es_ES`) mapping country codes and languages.
- **Asynchronous Architecture:** Non-blocking async/await pipeline using a typed `HttpClient` registered via `IHttpClientFactory` with managed connection lifetimes.
- **Input Length Capping:** Enforces non-empty search terms and limits search strings to a maximum of 200 UTF-16 code units (`string.Length`) to prevent query buffer bloat and excessive upstream URI construction.
- **Structured Error Handling:** Standardized **RFC 9457 Problem Details** for specifically handled validation and upstream failures:
  - `400 Bad Request` for invalid request bodies, empty search terms, search terms exceeding 200 code units, or out-of-range culture values.
  - `502 Bad Gateway` for upstream Steam non-success HTTP status codes, network errors, unparseable markup (`SteamResponseParseException`), refused redirects, or responses exceeding buffer limits.
  - `504 Gateway Timeout` for upstream requests exceeding the configured 30-second timeout.
- **Resource Hardened:**
  - Automatic HTTP redirects disabled (`AllowAutoRedirect = false`) on the primary HTTP handler to prevent unexpected cross-host redirect following.
  - Upstream response buffering capped at **1 MiB** (`MaxResponseContentBufferSize = 1,048,576 bytes`).
  - Request-cancellation propagation via `CancellationToken`.
- **Deterministic Test Suite:** 157 offline tests covering contract verification, culture mapping, price parsing, and failure classifications.

---

## Requirements

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (version `10.0.100` or newer)

Verify your installed .NET SDK version:

```shell
dotnet --version
```

---

## Getting Started

### 1. Clone the Repository

Clone the repository and enter the directory:

```shell
git clone https://github.com/fatihemres/SteamAPI.git
cd SteamAPI
```

*(Note: During active release candidate review prior to merging PR #3, modern .NET 10 code resides on the `net10_modernization_planning` branch; run `git checkout net10_modernization_planning` when building from source before merge).*

### 2. HTTPS Certificate Setup (Development)

The application configures HTTPS redirection via `app.UseHttpsRedirection()`. For local development with HTTPS, ensure your local development certificate is generated and trusted:

```shell
dotnet dev-certs https --trust
```

*(On platforms or environments where `--trust` is not supported, generate the certificate with `dotnet dev-certs https` and follow platform-specific trust procedures).*

### 3. Restore and Build

```shell
dotnet restore SteamAPI.sln
dotnet build SteamAPI.sln -c Release
```

### 4. Run the Application

Run using the project's configured `SteamAPI` launch profile:

```shell
dotnet run --project SteamAPI/SteamAPI.csproj --launch-profile SteamAPI
```

When started with the `SteamAPI` profile in the `Development` environment, Kestrel prints the active listening endpoints:
- HTTPS: `https://localhost:7030`
- HTTP: `http://localhost:5271`

Because `UseHttpsRedirection()` is active, HTTP requests to port 5271 are redirected to HTTPS on port 7030. Always check the actual console output from Kestrel to confirm active listening URLs.

Interactive OpenAPI documentation is available locally when running in `Development`:
- Swagger UI: `https://localhost:7030/swagger/index.html`
- OpenAPI Specification: `https://localhost:7030/swagger/v1/swagger.json`

*(Note: Swagger UI is enabled exclusively in the `Development` environment).*

### 5. Quick Query Examples

#### PowerShell (Windows)

```powershell
$body = @{
    game = "Portal"
    culture = 2
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://localhost:7030/steam" -Method Post -ContentType "application/json" -Body $body
```

#### cURL (Linux / macOS / Bash)

```shell
curl -X POST "https://localhost:7030/steam" \
     -H "Content-Type: application/json" \
     -d '{"game": "Portal", "culture": 2}'
```

---

## API Quick Start

### `POST /steam`

Queries Steam store search suggestions for matching game titles.

#### Request Body

```json
{
  "game": "Portal",
  "culture": 2
}
```

| Field | Type | Required | Constraints | Description |
| :--- | :--- | :---: | :--- | :--- |
| `game` | `string` | **Yes** | 1–200 UTF-16 code units, non-whitespace | Search term (e.g. game title, keyword). |
| `culture` | `integer` | **Yes** | Must be `1`, `2`, or `3` | Regional store and language identifier. |

#### Supported Cultures

| Culture Value | Enum Identifier | Region / Language Mapping | Steam Parameters |
| :---: | :--- | :--- | :--- |
| `1` | `tr_TR` | Turkey / Turkish | `cc=tr&lang=turkish` |
| `2` | `en_US` | United States / English | `cc=us&lang=english` |
| `3` | `es_ES` | Spain / Spanish | `cc=es&lang=spanish` |

#### Successful Response (`200 OK`)

Returns a top-level JSON array of `Listing` objects:

```json
[
  {
    "name": "Portal 2",
    "storeLink": "https://store.steampowered.com/app/620/Portal_2",
    "imageLink": "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/620/bd23943dcd0280aa2cdc066e2f86d2185cfd4cce/capsule_sm_120.jpg",
    "price": 9.99,
    "saleType": 0,
    "appId": "620"
  },
  {
    "name": "Portal",
    "storeLink": "https://store.steampowered.com/app/400/Portal",
    "imageLink": "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/400/capsule_sm_120.jpg",
    "price": 9.99,
    "saleType": 0,
    "appId": "400"
  }
]
```

*Note: Pricing and ranking data in examples are illustrative only and reflect dynamic upstream Steam store results.*

#### Sale Types (`saleType`)

| Value | Identifier | Description | `price` Field |
| :---: | :--- | :--- | :--- |
| `0` | `CostsMoney` | Paid title | Floating-point numeric price (e.g. `9.99`). |
| `1` | `FreeToPlay` | Free or Free-to-Play title | `null` |
| `2` | `NotAvailable` | Title without available pricing | `null` |

*Important:* The `price` field represents a raw numeric amount without a currency symbol. The requested culture does not guarantee a specific currency (for example, Steam's Turkish regional store prices titles in USD). Do not attempt automatic currency conversions without an external currency rate source.

For detailed schema constraints and error contracts, see the [API Reference](docs/API.md).

---

## Error Handling

Specifically handled validation and upstream failures return **RFC 9457 Problem Details for HTTP APIs**:

- **`400 Bad Request`:** Request validation failure (e.g. empty game name, game name exceeding 200 code units, unsupported culture integer). Returns a `ValidationProblemDetails` payload specifying field errors.
- **`502 Bad Gateway`:** Upstream Steam non-success HTTP status codes (such as 404, 429, 500, 503), network connectivity failures, unparseable Steam HTML markup (`SteamResponseParseException`), refused redirects, or upstream responses exceeding the 1 MiB buffer limit.
- **`504 Gateway Timeout`:** The upstream Steam service failed to respond within the configured 30-second timeout window.
- **Client Cancellation:** If the caller aborts the request, the cancellation token cancels in-flight operations promptly.

---

## Testing

The project maintains a 100% deterministic, offline automated test suite. Automated tests execute using synthetic fixtures and in-memory test doubles without issuing live network calls to Valve or external services.

Run the test suite across configurations:

```shell
# Debug Configuration
dotnet test SteamAPI.sln -c Debug

# Release Configuration
dotnet test SteamAPI.sln -c Release
```

For test inventory details, see [Release Candidate Notes](docs/releases/v0.2.0-rc-notes.md).

---

## Known Limitations & Deployment Notes

- **Upstream Scraping Dependency:** Steam does not provide an official frozen public contract for search suggestions. The API parses the suggestion HTML markup returned by `store.steampowered.com`.
- **Unrecognized Markup Behavior:** Completely unrecognized HTML containing no expected `match` elements (e.g. an unexpected full-page redesign or bot-challenge returned with HTTP 200) currently produces an empty result array `[]`.
- **Currency Code Omission:** The public Listing contract provides a numeric `price` but does not carry an ISO 4217 currency code.
- **Deployment Protections:** This milestone does not implement application-level rate limiting or production monitoring. The application is intended for deployment behind a managed reverse proxy or API gateway (such as YARP, Nginx, or Cloudflare) configured with appropriate rate limiting, TLS termination, and health probes.
- **Repository Licensing:** The repository currently does not contain a declared open-source LICENSE file; public visibility does not imply open-source licensing.

---

## Project Status and Release

This repository is currently preparing the **v0.2.0** milestone.

- Comprehensive documentation: [docs/API.md](docs/API.md)
- Release candidate notes: [docs/releases/v0.2.0-rc-notes.md](docs/releases/v0.2.0-rc-notes.md)

*Valve, Steam, and the Steam logo are trademarks and/or registered trademarks of Valve Corporation in the U.S. and/or other countries. All other trademarks are the property of their respective owners.*
