---
name: .NET SDK environment
description: The imported MVC project targets .NET 9 and needs the Replit dotnet-9.0 module for local builds and tests.
---

The project targets .NET 9; a default shell or workflow may still resolve the .NET 7 SDK and fail before compilation. Use the available .NET 9 module for verification rather than changing the target framework.

**Why:** The imported workflow initially resolved SDK 7 even though the project and NuGet packages target .NET 9.

**How to apply:** Check `dotnet --version` before diagnosing build errors; distinguish SDK selection failures from source-code failures.