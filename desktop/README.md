# SteamAPI Desktop

**Version:** `v0.1.0 (Release Candidate)`  
**Target Runtime:** `.NET 10.0` (C# 14)  
**UI Framework:** [Avalonia UI](https://avaloniaui.net/) 12.0  
**Pattern:** MVVM (via `CommunityToolkit.Mvvm`)  
**Companion Backend:** [SteamAPI v0.2.0](https://github.com/fatihemres/SteamAPI)  
**License:** Permissive MIT License  
**Financial Model:** **$0.00 / Zero Mandatory Recurring Costs**

---

## 1. Executive Summary & Product Vision

**SteamAPI Desktop** is a standalone, lightweight, cross-platform desktop application designed to explore the Steam catalog, test regional pricing, inspect raw API telemetry, and visualize the capabilities of the `SteamAPI` backend.

Built from the ground up on modern **.NET 10** and **Avalonia UI**, SteamAPI Desktop delivers a native, responsive dark-mode gaming aesthetic while maintaining strict compliance with the verified `SteamAPI v0.2.0` contract (`POST /steam`).

### Key Principles
* **Zero Mandatory Recurring Costs:** Built entirely on open-source, royalty-free technologies without paid third-party components, commercial UI libraries, subscriptions, or cloud hosting dependencies.
* **Non-Invasive Architecture:** Preserves the existing ASP.NET Core `SteamAPI` project and its 157 baseline unit/integration tests without touching protected code.
* **Developer & User Friendly:** Functions both as an interactive visual consumer for everyday users and a rich API inspector for developers.
* **Zero-Configuration First Run:** Includes a supervised loopback child process manager to auto-launch the local API, alongside an instant Offline Demo Mode.

---

## 2. Core Features & Capabilities

```
┌────────────────────────────────────────────────────────────────────────┐
│                        SteamAPI Desktop v0.1.0                         │
├───────────────┬───────────────────┬──────────────────┬─────────────────┤
│ 🎮 Explorer   │ 🔍 API Inspector  │ ⚙️ Settings      │ ℹ️ About & Help │
└───────────────┴───────────────────┴──────────────────┴─────────────────┘
```

### Feature 1: Game Explorer
* **Search Catalog:** Search titles by keyword with client-side input validation (length limits, non-empty checks).
* **Multi-Culture Storefront Switching:** Instantly query regional stores:
  * 🇹🇷 **Turkey (TR / Culture 1):** Turkish storefront with regional pricing representations.
  * 🇺🇸 **United States (US / Culture 2):** Baseline USD storefront.
  * 🇪🇸 **Spain (ES / Culture 3):** European EUR storefront.
* **Game Cards & Selection:** View search results with game title, App ID badge, sale type badges (`PAID`, `FREE TO PLAY`, `NOT AVAILABLE`), and verified price formatting.

### Feature 2: Game Details Inspector
* Detailed slide-over sheet displaying:
  * Full game title and App ID badge.
  * Exact price formatted according to verified contract semantics.
  * Sale type classification.
  * Raw Steam store URL.
* **One-Click Native Launchers:**
  * `🚀 Open in Steam Client`: Launches `steam://store/{appId}` directly in the local desktop Steam client.
  * `🌐 View Store Webpage`: Opens the official web store page in the default system browser.
  * `📋 Copy App ID`: Copies the numeric App ID to the system clipboard.
  * `📋 Copy Store Link`: Copies the canonical store URL to the system clipboard.

### Feature 3: Live HTTP API Inspector
* Dedicated telemetry tab capturing live HTTP network transactions between desktop client and API server.
* **History Log:** Retains up to 50 recent HTTP calls displaying HTTP method (`POST`), status code badges (e.g. `200 OK` green, `400 Bad Request` yellow, `502/504` red), round-trip latency (`ms`), and timestamps.
* **Payload Inspection:** Formatted JSON viewers for both request payloads and response bodies with syntax coloring and one-click clipboard copying.

### Feature 4: Bundled Loopback Server (Child Process Manager)
* Automatically discovers and manages a local `SteamAPI` backend process.
* Binds strictly to `http://127.0.0.1:5271` to prevent unwanted network exposure.
* Performs socket-level port conflict detection.
* Graceful lifecycle management: guarantees termination of the child process tree upon desktop application exit.

### Feature 5: Offline Demo Mode (Zero Dependencies)
* Built-in synthetic catalog simulator.
* Allows instant offline testing and evaluation without an active internet connection or local API server.
* Clearly marks synthetic items with `[SYNTHETIC DEMO]` to prevent misrepresentation.
* Simulates regional price variations across Turkey, United States, and Spain.

---

## 3. Technology Stack & Architecture

```mermaid
graph TD
    UI[Avalonia UI 12.0 Desktop Window]
    VM[MainWindowViewModel]
    
    VM --> GVM[GameExplorerViewModel]
    VM --> IVM[ApiInspectorViewModel]
    VM --> SVM[SettingsViewModel]
    VM --> AVM[AboutViewModel]
    
    GVM --> Client[ISteamApiClient]
    GVM --> Demo[IDemoDataService]
    GVM --> Clip[IClipboardService]
    GVM --> Launch[ILauncherService]
    
    SVM --> Process[IApiProcessManager]
    SVM --> Settings[ISettingsService]
    
    Client -->|HTTP POST /steam| Backend[Local or Remote SteamAPI v0.2.0]
    Process -->|Spawns & Monitors| Backend
```

| Component | Technology | Version | Purpose |
| :--- | :--- | :--- | :--- |
| **Language & Runtime** | C# 14 / .NET 10 | `net10.0` | High-performance modern language runtime |
| **Desktop UI Framework** | Avalonia UI | `12.0.4` | Cross-platform XAML UI with SkiaSharp rendering |
| **MVVM Toolkit** | CommunityToolkit.Mvvm | `8.4.1` | Source-generated MVVM observable properties & commands |
| **JSON Serialization** | System.Text.Json | Built-in | Fast, memory-efficient JSON serialization |
| **Unit Testing** | xUnit & Microsoft.NET.Test.Sdk | `2.9.3` | Automated unit, regression, and telemetry tests |

---

## 4. First-Run & Quickstart Guide

### Option A: Running from Release Bundle (Zero Setup)
1. Download `SteamAPI-Desktop-v0.1.0-win-x64.zip` from GitHub Releases.
2. Verify the SHA-256 checksum:
   ```powershell
   Get-FileHash -Algorithm SHA256 .\SteamAPI-Desktop-v0.1.0-win-x64.zip
   ```
3. Extract the ZIP archive.
4. Launch `SteamAPI.Desktop.exe`.
5. If no external API is running, either:
   * Open **Settings** and click **Start Server** to launch the bundled loopback API.
   * Enable **Offline Demo Mode** to explore instantly with synthetic data.

### Option B: Building from Source
Prerequisites: [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

```powershell
# Clone the repository
git clone https://github.com/fatihemres/SteamAPI.git
cd SteamAPI

# Restore dependencies
dotnet restore SteamAPI.sln

# Build complete solution (backend + tests + desktop)
dotnet build SteamAPI.sln

# Run all 194 automated tests
dotnet test SteamAPI.sln

# Run the desktop application
dotnet run --project desktop/SteamAPI.Desktop/SteamAPI.Desktop.csproj
```

---

## 5. Regional Storefront Cultures

`SteamAPI` v0.2.0 natively accepts three culture IDs corresponding to specific Steam regional storefronts:

| Culture ID | Country Code | Language | Description & Pricing |
| :---: | :---: | :---: | :--- |
| **1** | `TR` | `turkish` | **Turkey:** Steam USD-TR regional market. |
| **2** | `US` | `english` | **United States:** Baseline North American USD market. |
| **3** | `ES` | `spanish` | **Spain:** European Union EUR (€) market. |

---

## 6. Verification & Automated Test Suite

SteamAPI maintains 100% test pass rates across all layers:

* **Baseline SteamAPI Tests:** 157 / 157 passing (100% green).
* **SteamAPI Desktop Tests:** 37 / 37 passing (100% green).
* **Total Executed Tests:** **194 / 194 passing** across solution.

Test coverage for Desktop includes:
* `SteamGameListingTests`: 6-field contract serialization and display price formatting.
* `SteamCultureTests`: Regional culture enumeration and mappings.
* `DemoDataServiceTests`: Synthetic catalog querying, filtering, and regional adjustments.
* `SteamApiClientTests`: HTTP handling (200 OK, 400 Validation ProblemDetails, 502/504 errors, network exceptions, telemetry event emission).
* `GameExplorerViewModelTests`: Input validation, search dispatching, selection, clipboard and launcher interactions.
* `ApiInspectorViewModelTests`: Telemetry record collection, FIFO capping at 50 items, payload copying.
* `SettingsViewModelTests`: Settings loading, persistence, default reset, connection testing.
* `SettingsServiceTests`: Isolated JSON configuration persistence on disk.
* `MainWindowViewModelTests`: Navigation tab transitions and lifecycle process cleanup.
* `AboutViewModelTests`: Metadata and external link verification.

---

## 7. Packaging & Distribution

To create an official release package and checksum:

```powershell
# Run the automated packaging script
.\desktop\package-release.ps1
```

This generates:
* `artifacts/releases/SteamAPI-Desktop-v0.1.0-win-x64.zip`
* `artifacts/releases/SteamAPI-Desktop-v0.1.0-win-x64.zip.sha256`

---

## 8. Author & Creator Attribution

SteamAPI and SteamAPI Desktop are created, designed, and maintained by:

* **Creator & Author:** **Fatih Emre Sarman**
* **GitHub Profile:** [@fatihemres](https://github.com/fatihemres)
* **Official Repository:** [https://github.com/fatihemres/SteamAPI](https://github.com/fatihemres/SteamAPI)
* **Copyright:** `Copyright © 2026 Fatih Emre Sarman`

---

## 9. Release Authenticity & Verification

To verify that a downloaded binary or package originates from the official repository:

1. **Official Release Source:** Download binaries strictly from the official GitHub repository releases at [https://github.com/fatihemres/SteamAPI/releases](https://github.com/fatihemres/SteamAPI/releases).
2. **Cryptographic Checksums:** Compare the computed SHA-256 hash of the archive against the published `.sha256` manifest:
   ```powershell
   Get-FileHash -Algorithm SHA256 .\SteamAPI-Desktop-v0.1.0-win-x64.zip
   ```
3. **Git Commit & Tag Verification:** Official builds correspond to signed or annotated Git release tags within `fatihemres/SteamAPI`.
4. **Integrity vs. Attribution:** While cryptographic checksums and digital signatures ensure binary payload integrity and detect unauthorized tampering, software authorship and copyright ownership are established by the public Git commit lineage and license documentation.

---

## 10. Licensing & Trademarks

### Software License
SteamAPI Desktop is proposed for release under the **MIT License**:

```text
Copyright (c) 2026 Fatih Emre Sarman

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
```

### Trademark Disclaimer
> **Notice:** SteamAPI and SteamAPI Desktop are independent open-source developer tools and are **not** affiliated with, authorized, maintained, sponsored, or endorsed by **Valve Corporation** or any of its affiliates.  
> *Steam* and the *Steam logo* are trademarks or registered trademarks of Valve Corporation in the United States and other countries.
