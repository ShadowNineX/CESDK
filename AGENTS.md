# Repository Guidelines

## Project Overview

CESDK is a work-in-progress C# wrapper for developing Cheat Engine plugins. It exposes managed APIs for memory access and scanning, process/debugger control, assembly/disassembly, symbols, Lua execution, address lists, and related reverse-engineering tasks.

The package targets `netstandard2.0`; repository builds use .NET SDK 10, while live integration requires Windows, Cheat Engine 7.0+, and a plugin architecture matching Cheat Engine. The current bootstrap discovers a `CheatEnginePlugin` subclass only in the assembly containing the CESDK sources, so loadable plugins must source-link `src/**/*.cs` as demonstrated by the live-test plugin rather than merely reference a separate `CESDK.dll`.

## Architecture & Data Flow

The code is a layered interop facade:

1. Cheat Engine calls `CESDK.CESDK.CEPluginInitialize` in `src/CESDK.cs`.
2. Bootstrap code finds the first concrete `CheatEnginePlugin` subclass, creates it, and publishes native lifecycle callbacks.
3. `EnablePlugin` initializes the process-global `PluginContext` from Cheat Engine callback pointers, then invokes the plugin's `OnEnable` hook.
4. Static feature facades in `src/Classes/` normally call Cheat Engine Lua globals through `LuaUtils.CallLuaFunction`.
5. Stateful `CEObjectWrapper` subclasses retain native `IntPtr` handles and call Lua object fields/methods with an explicit `self`.
6. `src/Lua/LuaNative.cs` dynamically resolves the Lua 5.3 C API and performs stack-based native calls.

Example: `MemoryAccess.ReadInteger` -> `LuaUtils.CallLuaFunction("readInteger", ...)` -> `PluginContext.Lua` -> Lua stack/`PCall` -> managed result or `MemoryAccessException`.

There is no dependency-injection container. Native dependencies enter through Cheat Engine callback pointers; shared runtime state lives in `PluginContext`, while wrappers own native handles and small caches. `CESDK.Synchronize(Action)` is the explicit GUI-thread bridge. General async code is uncommon; follow `SymbolWaiter.WaitForAsync` for linked cancellation, optional timeout, and `TimeoutException` behavior.

Native boundaries are load-bearing. Preserve struct field order, calling conventions (`StdCall` versus `Cdecl`), ANSI marshalling, callback delegate retention, Lua stack balance, and exact Cheat Engine enum/function names. CE-owned wrappers must set `SuppressDestroy`; owned wrappers may call Lua `destroy`.

## Key Directories

- `src/` — plugin ABI, lifecycle, shared context, and logging.
- `src/Classes/` — public feature facades, models, exceptions, and native-object wrappers.
- `src/Lua/` — low-level Lua 5.3 and Cheat Engine native interop.
- `src/Utils/` — managed-to-Lua invocation and marshalling helpers.
- `src/Polyfills/` — compiler compatibility types only.
- `tests/CESDK.LiveTests/` — MSTest host that validates plugin-produced reports.
- `tests/CESDK.LiveTestPlugin/` — in-process Cheat Engine test plugin; it links `src/**/*.cs` directly and is the canonical plugin project example.
- `.github/workflows/` — Windows/SonarCloud build analysis and NuGet publication.
- `bin/`, `obj/`, `artifacts/`, `.sonar/` — generated output; do not hand-edit.

There are no checked-in `scripts/`, `tools/`, `docs/`, `examples/`, or `samples/` directories. Do not invent script-based workflows.

## Development Commands

```powershell
# Build the package only
dotnet build

# Build the package and both x64 test projects
dotnet build CESDK.sln

# Run checks that do not require Cheat Engine
dotnet test tests/CESDK.LiveTests/CESDK.LiveTests.csproj -p:Platform=x64 --filter "TestCategory!=Live"

# Restore and create release packages
dotnet restore CESDK.sln
dotnet pack CESDK.csproj -c Release --no-restore -o artifacts /p:PackageVersion=<version>
```

No repository-specific lint, format, or coverage command is configured. CI compiles with `dotnet build --no-incremental` inside SonarCloud analysis.

For live tests, build `tests/CESDK.LiveTestPlugin/CESDK.LiveTestPlugin.csproj`, copy `tests/CESDK.LiveTestPlugin/bin/x64/Debug/net10.0-windows/cesdk-live-tests.dll` into Cheat Engine's plugin directory, restart Cheat Engine, and enable **CESDK Live Tests**. Then run:

```powershell
$env:CESDK_LIVE = "1"
dotnet test tests/CESDK.LiveTests/CESDK.LiveTests.csproj -p:Platform=x64 --filter TestCategory=Live
```

Set `CESDK_LIVE_RESULT` before starting both Cheat Engine and the test host when using a non-default report path.

## Code Conventions & Common Patterns

- Use latest C# with nullable references enabled; warnings are errors. Preserve the style of the edited file because private-field and constant naming is not fully uniform.
- Keep public APIs PascalCase. Existing MSTest methods use `Subject_ExpectedBehavior`; live case IDs use stable lowercase kebab-case.
- Preserve external names exactly (`ScanOption.soExactValue`, `VariableType.vtDword`, `getCurrentMemscan`). They mirror Cheat Engine rather than local naming preferences.
- Keep thin static facades expression-bodied where practical and route shared Lua calls through `LuaUtils`; do not duplicate stack protocols.
- Pair a feature facade with its domain exception. Public operations normally wrap low-level failures in a relevant `CesdkException` subtype while retaining the inner exception.
- Do not homogenize intentional fallbacks: cleanup/finalizers/logging and selected wrapper property getters are best effort; normal public operations should fail with typed, contextual exceptions. Managed exceptions must not cross the native plugin ABI.
- Treat Lua stack balance as an invariant. Pop exact return/error values and use `try/finally` with `SetTop(...)` where surrounding code does so. Check `PCall` results instead of copying older unchecked calls.
- Object methods push the method function followed by the wrapper as explicit `self`. Capture returned userdata pointers before popping their Lua values.
- Preserve ownership semantics in `CEObjectWrapper`: wrappers destroy owned Cheat Engine objects, while CE-owned objects set `SuppressDestroy`. Never create two owners for one native pointer.
- Release `MemScan`/`FoundList` cached results before another scan; initialized results retain native pointers that become unsafe after rescanning.
- Use `CESDK.Synchronize` for Cheat Engine GUI/address-list work. Do not generalize `SymbolWaiter`'s `Task.Run` pattern to arbitrary Lua or UI operations; the process-global Lua stack is shared state.
- Plugin initialization order matters: do not call feature APIs from constructors or static initializers. `PluginContext.Lua` is initialized immediately before `OnEnable`.
- Public lifecycle-sensitive APIs use XML documentation. Preserve ordering guidance and native safety notes when behavior changes.

## Important Files

- `src/CESDK.cs` — native entry point, ABI structs/delegates, plugin discovery, lifecycle, and synchronization.
- `src/CheatEnginePlugin.cs` — plugin-author extension point (`Name`, `OnEnable`, `OnDisable`).
- `src/PluginContext.cs` — one-time shared Lua bridge initialization.
- `src/Lua/LuaNative.cs` — dynamic Lua DLL/export loading and stack API.
- `src/Utils/LuaUtils.cs` — central Lua-call adapter and marshalling.
- `src/Classes/CEObjectWrapper.cs` — native object ownership and invocation model.
- `src/Classes/MemScan.cs` and `src/Classes/FoundList.cs` — representative stateful scan workflow.
- `src/Classes/MemoryAccess.cs` — representative thin static facade.
- `src/Classes/LuaExecutor.cs` — representative direct stack/table handling.
- `CESDK.csproj` — package metadata, `netstandard2.0`, nullable/latest C#, warnings-as-errors.
- `global.json` — .NET SDK, Microsoft Testing Platform, and MSTest SDK pins.
- `CESDK.sln` — package plus both x64 live-test projects.
- `README.md` — canonical setup, source-link constraint, deployment, and live-test procedure.
- `.github/workflows/build.yml` and `.github/workflows/publish-nuget.yml` — CI analysis and release packaging.

## Runtime/Tooling Preferences

- Use the SDK selected by `global.json`: `10.0.102` with `latestFeature` roll-forward.
- Use `dotnet` and NuGet; `nuget.config` declares only `https://api.nuget.org/v3/index.json`.
- The library targets `netstandard2.0`. Both test projects target `net10.0-windows`, x64, and use warnings-as-errors.
- Tests use Microsoft Testing Platform with `MSTest.Sdk` 4.2.3.
- Live work requires Windows, Cheat Engine 7.0+, and matching process/plugin architecture; the repository's test plugin is x64.
- Do not add Node/Bun, Python, Make, or custom-script assumptions. No such tooling is part of this repository.

## Testing & QA

`tests/CESDK.LiveTests/CesdkLiveTests.cs` contains MSTest cases categorized as `Unit` or `Live`. The non-live category currently verifies only harness/report-path behavior; it is not broad facade unit coverage. Live tests are opt-in: without `CESDK_LIVE=1`, they report inconclusive rather than fail.

The Cheat Engine plugin executes live cases synchronously, catches each case exception so later checks continue, and writes an indented JSON report. The MSTest host polls that report every 250 ms, validates freshness and aggregate success, and requires all expected case IDs. Preserve explicit cleanup in new Lua cases: restore stack depth in `finally` and clear registered globals.

Environment variables:

- `CESDK_LIVE_RESULT` — report path; default `%TEMP%\cesdk-live-tests-result.json`. Set it before both Cheat Engine and `dotnet test`.
- `CESDK_LIVE_TIMEOUT_SECONDS` — positive integer polling timeout; default 10.
- `CESDK_LIVE_MAX_RESULT_AGE_SECONDS` — positive integer freshness limit; default 600.

CI currently performs compilation and SonarCloud analysis but does not run tests or coverage. For behavioral changes, run the relevant x64 test command locally; native/Lua changes generally require the live workflow for end-to-end proof.
