# Repository Guidelines

## Project Overview

CESDK is a work-in-progress C# wrapper for developing Cheat Engine plugins. It exposes managed APIs for memory access and scanning, process/debugger control, assembly/disassembly, symbols, Lua execution, and related reverse-engineering tasks. The package targets `netstandard2.0`; live integration requires Windows, Cheat Engine 7.0+, and an x64 test plugin.

## Architecture & Data Flow

The code is a layered interop facade:

1. Cheat Engine calls `CESDK.CESDK.CEPluginInitialize` in `src/CESDK.cs`.
2. Bootstrap code discovers the first concrete `CheatEnginePlugin` subclass, creates it, and publishes native lifecycle callbacks.
3. `EnablePlugin` initializes the process-global `PluginContext` from Cheat Engine function pointers and invokes the plugin's `OnEnable` hook.
4. Static feature facades in `src/Classes/` normally call Cheat Engine Lua globals through `LuaUtils.CallLuaFunction`.
5. Stateful wrappers derived from `CEObjectWrapper` hold native `IntPtr` handles and invoke Lua object fields/methods with an explicit `self`.
6. `src/Lua/LuaNative.cs` resolves the Lua 5.3 C API and performs the final stack-based native calls.

Example: `MemoryAccess.ReadInteger` -> `LuaUtils.CallLuaFunction("readInteger", ...)` -> `PluginContext.Lua` -> Lua stack/PCall -> managed result or `MemoryAccessException`.

There is no DI container. Native dependencies enter through Cheat Engine callback pointers; shared runtime state lives in `PluginContext`, while wrappers own native handles and small local caches. `CESDK.Synchronize(Action)` is the explicit GUI-thread bridge. General async code is uncommon; `SymbolWaiter.WaitForAsync` is the model for cancellation and timeout handling.

## Key Directories

- `src/` — plugin ABI, lifecycle, shared context, and logging.
- `src/Classes/` — public feature facades, models, exceptions, and native-object wrappers.
- `src/Lua/` — low-level Lua 5.3 and Cheat Engine native interop.
- `src/Utils/` — managed-to-Lua invocation and marshaling helpers.
- `src/Polyfills/` — compiler compatibility types only.
- `tests/CESDK.LiveTests/` — MSTest host that validates plugin-produced reports.
- `tests/CESDK.LiveTestPlugin/` — in-process Cheat Engine test plugin; it links `src/**/*.cs` directly.
- `.github/workflows/` — Windows/SonarCloud build analysis and NuGet publication.
- `bin/`, `obj/`, `artifacts/`, `.sonar/` — generated output; do not hand-edit.

## Development Commands

```powershell
# Build the package only
dotnet build

# Build the package and both test projects
dotnet build CESDK.sln

# Run tests that do not require Cheat Engine
dotnet test tests/CESDK.LiveTests/CESDK.LiveTests.csproj -p:Platform=x64 --filter "TestCategory!=Live"

# Restore and create release packages
dotnet restore CESDK.sln
dotnet pack CESDK.csproj -c Release --no-restore -o artifacts /p:PackageVersion=<version>
```

No repository-specific lint or format command is configured. CI compiles with `dotnet build --no-incremental` inside SonarCloud analysis.

For live tests, build `tests/CESDK.LiveTestPlugin/CESDK.LiveTestPlugin.csproj`, copy `bin/x64/Debug/net10.0-windows/cesdk-live-tests.dll` from that project into Cheat Engine's plugin directory, restart Cheat Engine, enable `CESDK Live Tests`, then run:

```powershell
$env:CESDK_LIVE = "1"
dotnet test tests/CESDK.LiveTests/CESDK.LiveTests.csproj -p:Platform=x64 --filter TestCategory=Live
```

## Code Conventions & Common Patterns

- Use latest C# with nullable references enabled; warnings are errors. Preserve the style of the file being edited because private-field and constant naming is not fully uniform.
- Keep public types and members PascalCase. Existing test methods use `Subject_ExpectedBehavior`; live case IDs use lowercase kebab-case.
- Preserve Cheat Engine/Lua names exactly (`ScanOption.soExactValue`, `VariableType.vtDword`, `getCurrentMemscan`). They mirror an external API rather than local naming preferences.
- Thin static facades should remain expression-bodied where practical and route shared Lua calls through `LuaUtils` rather than duplicating stack protocols.
- Treat Lua stack balance as an invariant. Pop exact return/error values and use `try/finally` with `SetTop(0)` where the surrounding code does so.
- Layer errors: low-level invocation code adds Lua/PCall context; public feature APIs wrap failures in a relevant `CesdkException` subtype. Do not let managed exceptions cross the native plugin ABI.
- Preserve ownership semantics in `CEObjectWrapper`: wrappers destroy owned Cheat Engine objects, while CE-owned objects set `SuppressDestroy`.
- Release `MemScan`/`FoundList` cached results before starting another scan; stale native result pointers are unsafe.
- Best-effort suppression is limited to cleanup/logging and selected wrapper property fallbacks. Normal public operations should fail with typed, contextual exceptions.
- Use `CESDK.Synchronize` for Cheat Engine GUI-thread work. Follow `SymbolWaiter.WaitForAsync` for linked cancellation, optional timeout, and `TimeoutException` behavior.

## Important Files

- `src/CESDK.cs` — native entry point, ABI structs/delegates, plugin discovery, lifecycle, and synchronization.
- `src/CheatEnginePlugin.cs` — plugin-author extension point (`Name`, `OnEnable`, `OnDisable`).
- `src/PluginContext.cs` — one-time shared Lua bridge initialization.
- `src/Lua/LuaNative.cs` — dynamic Lua DLL/export loading and stack API.
- `src/Utils/LuaUtils.cs` — central Lua-call adapter and marshaling.
- `src/Classes/CEObjectWrapper.cs` — base exception and native object ownership model.
- `src/Classes/MemScan.cs` and `src/Classes/FoundList.cs` — representative stateful scan workflow.
- `src/Classes/MemoryAccess.cs` — representative thin static facade.
- `CESDK.csproj` — package metadata, `netstandard2.0`, nullable/latest C#, warnings-as-errors.
- `global.json` — .NET SDK and test-runner pinning.
- `CESDK.sln` — package plus both x64 live-test projects.
- `README.md` — supported environment and canonical build/live-test procedure.

## Runtime/Tooling Preferences

- Use the .NET SDK pinned by `global.json`: `10.0.102` with `latestFeature` roll-forward.
- Use the `dotnet` CLI and NuGet; the configured feed is `https://api.nuget.org/v3/index.json`.
- The main package targets `netstandard2.0`. Repository test projects target `net10.0-windows` and x64.
- Run live integration and CI-equivalent work on Windows. The README lists .NET Framework 4.8.1, Cheat Engine 7.0+, and Windows for consumers.
- Tests use Microsoft Testing Platform with `MSTest.Sdk` 4.2.3.
- Do not add Node/Bun tooling assumptions; this is a .NET/NuGet repository.

## Testing & QA

`tests/CESDK.LiveTests/CesdkLiveTests.cs` contains MSTest `[TestClass]`/`[TestMethod]` tests categorized as `Unit` or `Live`. Live tests are opt-in: without `CESDK_LIVE=1`, they report inconclusive rather than fail.

The Cheat Engine plugin runs checks independently, catches per-case exceptions so later checks continue, and writes an indented JSON report. The MSTest host polls that report asynchronously and validates freshness, aggregate success, and required case names. Preserve explicit cleanup in new Lua checks: save/restore stack depth in `finally` and reset registered globals.

Environment variables:

- `CESDK_LIVE_RESULT` — report path; default `%TEMP%\cesdk-live-tests-result.json`. Set it before both Cheat Engine and `dotnet test`.
- `CESDK_LIVE_TIMEOUT_SECONDS` — positive integer poll timeout; default 10.
- `CESDK_LIVE_MAX_RESULT_AGE_SECONDS` — positive integer freshness limit; default 600.

There is no checked-in coverage configuration, fixture/snapshot directory, mocking framework, or coverage command. CI currently performs build plus SonarCloud analysis but does not run `dotnet test`; run the relevant x64 test command locally for behavioral changes.