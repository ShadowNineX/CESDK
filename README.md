# CESDK — Cheat Engine SDK for C#

CESDK is a managed wrapper and plugin bootstrap for building Cheat Engine extensions in C#. It exposes typed facades over Cheat Engine's Lua API for processes, memory, scans, symbols, structures, address lists, assembly, debugging, injection, and optional DBVM operations.

[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2FShadowNineX%2FCESDK.svg?type=shield)](https://app.fossa.com/projects/git%2Bgithub.com%2FShadowNineX%2FCESDK?ref=badge_shield)

> [!IMPORTANT]
> CESDK is under active development. Test plugins against the exact Cheat Engine version they will run on, and use that installation's `celua.txt` as the authoritative Lua API reference.

## Requirements

- Windows with the same architecture as Cheat Engine; the tested live plugin is x64.
- Cheat Engine 7.6.2 or newer for managed plugin hosting.
- .NET 10 SDK to build this repository. `global.json` pins the tested SDK feature band.
- The published CESDK library targets `netstandard2.0`.

## Quick Start

### 1. Create a plugin project

Cheat Engine must find the CESDK bootstrap and your `CheatEnginePlugin` subclass in the same output assembly. The reliable pattern is to compile CESDK sources into the plugin project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <OutputType>Library</OutputType>
    <AssemblyName>my-cesdk-plugin</AssemblyName>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <PlatformTarget>x64</PlatformTarget>
    <Platforms>x64</Platforms>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="..\CESDK\src\**\*.cs" LinkBase="CESDK" />
  </ItemGroup>
</Project>
```

Adjust the relative path to `CESDK/src/`. `tests/CESDK.LiveTestPlugin/CESDK.LiveTestPlugin.csproj` is the working reference project.

> [!NOTE]
> A plugin subclass in a separate assembly that only references `CESDK.dll` is not auto-discovered by the current `CEPluginInitialize` bootstrap. Use the source-link pattern for a CE-loadable plugin.

### 2. Add one plugin class

```csharp
using CESDK;
using CESDK.Classes;

public sealed class MyCheatEnginePlugin : CheatEnginePlugin
{
    public override string Name => "My CESDK Plugin";

    protected override void OnEnable()
    {
        // PluginContext.Lua is initialized before this hook.
        LuaLogger.Print($"{Name} enabled");
    }

    protected override void OnDisable()
    {
        LuaLogger.Print($"{Name} disabled");
    }
}
```

Define exactly one concrete `CheatEnginePlugin` subclass. Do not call CESDK APIs from a constructor or static initializer; shared Lua state becomes available immediately before `OnEnable`.

### 3. Build and install

```powershell
dotnet build -p:Platform=x64
```

Copy the resulting DLL into Cheat Engine's `plugins` directory, restart Cheat Engine, then enable the plugin in CE's plugin settings.

## Runtime Model

CESDK is a typed layer over Cheat Engine's Lua/native plugin interfaces:

```text
Your plugin -> CESDK.Classes facade -> LuaUtils/LuaNative -> Cheat Engine
```

Important invariants:

- Cheat Engine Lua state, engine objects, scanners, and UI objects are not thread-safe.
- Marshal CE-facing work to the main GUI thread with `CESDK.Synchronize(...)`.
- Keep an attachment check and the operation it protects inside the same synchronized block.
- Restore the Lua stack to its original top in `finally` when using `LuaNative` directly.
- Use 64-bit conversions for addresses, pointers, sizes, and registers.
- Keep one owned `FoundList` per `MemScan`: deinitialize it before the next scan, wait for scanning to finish, then reinitialize it. Destroy the FoundList only when disposing the MemScan.
- `Debugger.DetachIfPossible` unpauses and detaches while preserving CE's valid same-PID process handle. A subsequent inactive `DebugProcess` attaches directly; an active interface switch detaches once before attaching. CE fallbacks are reported by `DebuggerAttachResult`.
- Dispose wrappers you own. Do not destroy CE-owned objects such as the current GUI `MemScan`.

## API Areas

| Type | Purpose |
| --- | --- |
| `Process`, `ProcessControl`, `ThreadList` | Open, create, pause, resume, and inspect processes and threads |
| `MemoryAccess`, `AdvancedMemory`, `MemoryRegions` | Typed reads/writes, allocation, protection, copy/compare, hashes, and region files |
| `PointerChains` | Resolve pointer chains and perform bounded direct-reference scans |
| `AobScanner`, `MemScan`, `FoundList` | AOB, value, and string scanning workflows |
| `AddressResolver`, `SymbolManager`, `SymbolRegistry`, `SymbolWaiter` | Addresses, modules, symbols, RTTI, and registered symbols |
| `Assembler`, `Disassembler` | Assembly, Auto Assembler, disassembly, comments, and function ranges |
| `AddressList`, `MemoryRecord`, `CheatTable` | Cheat-table records and `.CT` load/save |
| `StructureManager` | Global Structure Dissect definitions, elements, autoguess, and comparison |
| `Debugger` | Interfaces, breakpoints, context, registers, stepping, threads, XMM, and LBR |
| `Injection` | Script generation, target C compilation, DLL injection, and remote calls |
| `Dbvm` | Optional DBVM status, physical memory, and watches |
| `LuaExecutor`, `LuaNative`, `LuaLogger` | High-level Lua execution, stack access, callbacks, and logging |
| `Converter`, `Speedhack` | Encoding/hash helpers and process speed control |

Facade failures are wrapped in feature-specific `CesdkException` subclasses. Catch the narrow exception when recovery differs by operation.

## Common Workflows

### Open a process and access memory

```csharp
using CESDK.Classes;

global::CESDK.CESDK.Synchronize(() =>
{
    Process.OpenProcess("game.exe");

    ulong address = AddressResolver.GetAddress("game.exe+1234");
    int value = MemoryAccess.ReadInteger(address);

    if (!MemoryAccess.WriteInteger(address, value + 10))
        throw new InvalidOperationException("Cheat Engine rejected the write.");
});
```

`AddressResolver.GetAddress` throws on failure; `GetAddressSafe` returns `null`. `MemoryAccess` provides typed target reads/writes, while methods ending in `Local` access Cheat Engine's own process.

### Scan for an AOB

```csharp
using CESDK.Classes;

List<ulong> matches = AobScanner.Scan(
    "48 8B ?? ?? ?? 89",
    protectionFlags: "",
    alignmentType: 0,
    alignmentParam: "");

ulong? moduleMatch = AobScanner.ScanModuleUnique(
    "game.exe",
    "48 8B ?? ?? ?? 89");
```

AOB parameters are positional. Empty protection/alignment strings and alignment type `0` are valid values and are passed to Cheat Engine explicitly. CESDK copies returned addresses and destroys CE's temporary result list.

### Run a value scan safely

```csharp
using CESDK.Classes;

MemScan scan = MemScan.GetCurrentMemScan();
scan.DeinitializeResults();
scan.NewScan();
scan.Scan(new ScanParameters
{
    ScanOption = ScanOption.soExactValue,
    VarType = VariableType.vtDword,
    Input1 = "100",
    Input2 = string.Empty,
    AlignmentType = AlignmentType.fsmAligned,
    AlignmentParam = "4"
});
scan.WaitTillDone();
scan.InitializeResults();

try
{
    int count = scan.GetResultCount();
    if (count > 0)
        LuaLogger.Printf("First result: {0}", scan.GetResultAddress(0));
}
finally
{
    scan.DeinitializeResults();
}
```

Call `DeinitializeResults()` before every subsequent scan. `MemScan` keeps its owned FoundList object alive, deinitializes result access before the scan, and reinitializes the same object afterward. Destroying and recreating that FoundList between first/next scans can invalidate Cheat Engine's internal result ownership and crash the host.

### Work with the address list

```csharp
using CESDK.Classes;

global::CESDK.CESDK.Synchronize(() =>
{
    using var addressList = new AddressList();
    MemoryRecord record = addressList.CreateMemoryRecord();
    record.Description = "Player health";
    record.Address = "game.exe+1234";
    record.VarType = VariableType.vtDword;
});
```

The generic synchronization overload can return a value:

```csharp
int count = global::CESDK.CESDK.Synchronize(
    () => new AddressList().Count);
```

### Execute Lua

`LuaExecutor.Execute` converts Lua `nil`, booleans, numbers, strings, tables, and multiple returns into managed values:

```csharp
using CESDK.Classes;

LuaResult result = LuaExecutor.Execute(
    "return getOpenedProcessID(), 'ready', { 10, 20, 30 }");

if (result.Values is not null)
{
    foreach (object? value in result.Values)
        Console.WriteLine(value);
}
```

Use `PluginContext.Lua` only when no typed facade fits. Manual stack code must preserve stack balance:

```csharp
LuaNative lua = PluginContext.Lua;
int initialTop = lua.GetTop();

try
{
    lua.GetGlobal("getOpenedProcessID");
    if (!lua.IsFunction(-1))
        throw new InvalidOperationException("getOpenedProcessID is unavailable.");

    int status = lua.PCall(0, 1);
    if (status != 0)
        throw new InvalidOperationException(lua.ToString(-1));

    int processId = lua.ToInteger(-1);
}
finally
{
    lua.SetTop(initialTop);
}
```

### Register a C# Lua callback

Register callbacks from `OnEnable`, after `PluginContext` is initialized:

```csharp
protected override void OnEnable()
{
    LuaNative lua = PluginContext.Lua;

    lua.RegisterCEFunction("my_plugin_add", _ =>
    {
        int left = lua.ToInteger(1);
        int right = lua.ToInteger(2);
        lua.PushInteger(left + right);
        return 1;
    });
}
```

Remove exported callbacks in `OnDisable` if the plugin may be toggled without restarting CE.

## Build and Test

### Build the SDK

```powershell
dotnet restore
dotnet build CESDK.sln
```

Normal tests do not require Cheat Engine:

```powershell
dotnet test tests/CESDK.LiveTests/CESDK.LiveTests.csproj -p:Platform=x64 --filter "TestCategory!=Live"
```

### Run the CE-loaded live suite

1. Build `tests/CESDK.LiveTestPlugin/CESDK.LiveTestPlugin.csproj` for x64.
2. Load its `cesdk-live-tests.dll` in Cheat Engine and enable **CESDK Live Tests**.
3. Startup executes target-independent cases. Target-dependent cases are reported as skipped until a process is attached.
4. Attach a disposable process.
5. Choose **CESDK Tests** -> **Run Tests Against Attached Process**.
6. Validate `%TEMP%\cesdk-live-tests-result.json`:

```powershell
$env:CESDK_LIVE = "1"
dotnet test tests/CESDK.LiveTests/CESDK.LiveTests.csproj `
  -p:Platform=x64 `
  --filter TestCategory=Live
```

Enable mutating coverage before launching Cheat Engine:

```powershell
$env:CESDK_LIVE_MUTATING = "1"
& "C:\Program Files\Cheat Engine\cheatengine-x86_64.exe"
```

Then attach a disposable target and use the **CESDK Tests** menu. Mutating coverage allocates and writes target memory and temporarily changes address-list, structure, symbol, and table state.

Optional environment variables:

| Variable | Purpose |
| --- | --- |
| `CESDK_LIVE` | Enables the external MSTest report validator |
| `CESDK_LIVE_MUTATING` | Enables target and CE-state mutations in the plugin suite |
| `CESDK_LIVE_TARGET_PID` | Automatically attaches a disposable PID at plugin startup |
| `CESDK_LIVE_RESULT` | Overrides the JSON report path |
| `CESDK_LIVE_TIMEOUT_SECONDS` | Overrides report polling timeout |
| `CESDK_LIVE_MAX_RESULT_AGE_SECONDS` | Overrides accepted report age |

The attached Notepad validation currently exercises 35 CESDK cases with no skipped cases, including a first/next scan FoundList-lifecycle regression.

## Logging

CESDK uses an isolated NLog factory. CESDK and ce-mcp share one canonical file:

```text
%APPDATA%\CeMCP\ce-mcp.log
```

The log rolls at 10 MiB and retains five archives. Do not add alternate file loggers.

## NuGet Package

The API package is published at [NuGet.org](https://www.nuget.org/packages/CESDK):

```powershell
dotnet add package CESDK
```

The package is useful when CESDK bootstrap code already exists in the loadable assembly. For a new Cheat Engine plugin, use the source-link bootstrap pattern described in Quick Start.

## Contributors

<a href="https://github.com/ShadowNineX/CESDK/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=ShadowNineX/CESDK" alt="Contributors" />
</a>

Made with [contrib.rocks](https://contrib.rocks).

## License

[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2FShadowNineX%2FCESDK.svg?type=large)](https://app.fossa.com/projects/git%2Bgithub.com/ShadowNineX%2FCESDK?ref=badge_large)
