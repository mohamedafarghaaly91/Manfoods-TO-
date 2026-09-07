---
name: .NET SDK environment
description: The imported MVC project targets .NET 9 and needs the Replit dotnet-9.0 module for local builds and tests.
---

The project targets .NET 9; `.replit` must provide only the `dotnet-9.0` module and the repository must include `global.json` so a fresh Replit workspace or CI checkout cannot resolve the .NET 7 SDK.

**Why:** The imported workflow initially resolved SDK 7 even though the project and NuGet packages target .NET 9.

**How to apply:** Check `dotnet --version` before diagnosing build errors; keep the SDK pin and the Replit module declaration aligned, and distinguish SDK selection failures from source-code failures.