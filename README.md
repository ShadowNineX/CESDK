# CESDK - Cheat Engine SDK for C#

⚠️ **Work in Progress** ⚠️

A C# wrapper library for developing plugins for Cheat Engine. Provides managed .NET interfaces for memory scanning, process manipulation, and reverse engineering tasks.

## Status

[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2FShadowNineX%2FCESDK.svg?type=shield)](https://app.fossa.com/projects/git%2Bgithub.com%2FShadowNineX%2FCESDK?ref=badge_shield)

This project is currently WIP. Things might be missing.

## Build

```bash
dotnet build
```

## Testing

Tests live under `tests/` and are not referenced by `CESDK.csproj`, so they do not get packed into the CESDK NuGet package.

Build the SDK and test projects:

```bash
dotnet build CESDK.sln
dotnet test tests/CESDK.LiveTests/CESDK.LiveTests.csproj -p:Platform=x64 --filter "TestCategory!=Live"
```

The live CESDK tests run through a dedicated Cheat Engine plugin:

1. Build `tests/CESDK.LiveTestPlugin/CESDK.LiveTestPlugin.csproj`.
2. Copy `tests/CESDK.LiveTestPlugin/bin/x64/Debug/net10.0-windows/cesdk-live-tests.dll` into Cheat Engine's plugins directory.
3. Restart Cheat Engine and enable `CESDK Live Tests`.
4. Run the test harness:

```powershell
$env:CESDK_LIVE = "1"
dotnet test tests/CESDK.LiveTests/CESDK.LiveTests.csproj -p:Platform=x64 --filter TestCategory=Live
```

By default the plugin writes `%TEMP%\cesdk-live-tests-result.json`. Set `CESDK_LIVE_RESULT` before launching Cheat Engine and before running `dotnet test` to use a custom result path.

## Install the API package

NuGet package: https://www.nuget.org/packages/CESDK

```bash
dotnet add package CESDK
```

> **Plugin bootstrap note:** the current `CEPluginInitialize` implementation discovers
> `CheatEnginePlugin` subclasses in the assembly containing the CESDK sources. A plugin
> subclass compiled only in a separate assembly that references `CESDK.dll` is not
> auto-discovered. For a Cheat Engine-loadable plugin, use the source-link project pattern
> shown below; `tests/CESDK.LiveTestPlugin/CESDK.LiveTestPlugin.csproj` is the working
> reference implementation.

## Requirements

- Windows
- Cheat Engine 7.0 or later
- A plugin architecture matching Cheat Engine (`x64` in the tested live-plugin project)
- .NET SDK `10.0.102` to build this repository; the package itself targets `netstandard2.0`

## Usage

### Create and register a plugin

CESDK registration is convention-based. Define exactly one non-abstract
`CheatEnginePlugin` subclass in the same output assembly as the CESDK bootstrap.
`CEPluginInitialize` creates the first subclass it finds, uses `Name` as the Cheat Engine
plugin name, and calls `OnEnable`/`OnDisable` when Cheat Engine toggles the plugin. There is
no separate registration call.

The repository's live plugin compiles CESDK sources into the plugin assembly. A minimal
project uses the same pattern (adjust the relative path to `src/`):

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
    <Compile Include="..\..\src\**\*.cs" LinkBase="CESDK" />
  </ItemGroup>
</Project>
```

Then add the plugin class:

```csharp
using CESDK;
using CESDK.Classes;

public sealed class MyCheatEnginePlugin : CheatEnginePlugin
{
    public override string Name => "My CESDK Plugin";

    protected override void OnEnable()
    {
        // PluginContext is initialized before this hook runs.
        LuaLogger.Print($"{Name} enabled");
    }

    protected override void OnDisable()
    {
        LuaLogger.Print($"{Name} disabled");
    }
}
```

Do not call CESDK APIs from the plugin constructor or a static initializer:
`PluginContext.Lua` becomes available immediately before `OnEnable`. Build the project,
copy its DLL to Cheat Engine's plugin directory, restart Cheat Engine, and enable the
plugin in Cheat Engine's plugin settings.

### Open a process, resolve an address, and access memory

Most high-level APIs are static facades in `CESDK.Classes`. Open the target by PID or
process name before using target-memory APIs:

```csharp
using CESDK.Classes;

CESDK.Classes.Process.OpenProcess("game.exe");

ulong healthAddress = AddressResolver.GetAddress("game.exe+1234");
int health = MemoryAccess.ReadInteger(healthAddress);

if (!MemoryAccess.WriteInteger(healthAddress, health + 10))
    throw new InvalidOperationException("Cheat Engine rejected the write.");
```

`AddressResolver.GetAddress` throws when a symbol cannot be resolved.
`GetAddressSafe` returns `null` instead:

```csharp
ulong? address = AddressResolver.GetAddressSafe("game.exe+PlayerHealth");
if (address is not null)
    Console.WriteLine($"Resolved to 0x{address.Value:X}");
```

`MemoryAccess` provides typed target-process reads and writes:
`ReadByte`, `ReadSmallInteger`, `ReadInteger`, `ReadQword`, `ReadPointer`, `ReadFloat`,
`ReadDouble`, `ReadString`, `ReadBytes`, and corresponding `Write*` methods. The
`*Local` variants access Cheat Engine's own process rather than the opened target.

### Scan for an array of bytes

Use `Scan` for all matches, `ScanUnique` for a single match, or `ScanModuleUnique` to
limit a unique scan to one module. Patterns use Cheat Engine's AOB syntax.

```csharp
using CESDK.Classes;

List<ulong> matches = AobScanner.Scan("48 8B ?? ?? ?? 89");
ulong? unique = AobScanner.ScanModuleUnique(
    "game.exe",
    "48 8B ?? ?? ?? 89");

foreach (ulong match in matches)
    LuaLogger.Printf("AOB match: 0x{0:X}", match);
```

Optional `protectionFlags`, `alignmentType`, and `alignmentParam` arguments are passed
through to Cheat Engine.

### Run a full value scan

`MemScan.GetCurrentMemScan()` wraps Cheat Engine's main scanner. `Scan` is the preferred
entry point for it because Cheat Engine chooses first-scan versus next-scan behavior.

```csharp
using CESDK.Classes;

MemScan scan = MemScan.GetCurrentMemScan();
scan.NewScan();
scan.Scan(new ScanParameters
{
    ScanOption = ScanOption.soExactValue,
    VarType = VariableType.vtDword,
    Input1 = "100"
});
scan.WaitTillDone();
scan.InitializeResults();

try
{
    int count = scan.GetResultCount();
    if (count > 0)
    {
        string firstAddress = scan.GetResultAddress(0);
        string firstValue = scan.GetResultValue(0);
        LuaLogger.Printf("First of {0} results: {1} = {2}", count, firstAddress, firstValue);
    }
}
finally
{
    scan.DeinitializeResults();
}
```

Always call `DeinitializeResults()` before starting another scan. An initialized
`FoundList` holds pointers into the previous result set; scanning again with those stale
pointers can crash Cheat Engine. For a separately owned scanner, construct `new MemScan()`
and use `FirstScan`/`NextScan` when explicit control is required.

### Work with the address list on the GUI thread

Cheat Engine UI objects, including the main address list, should be accessed through
`CESDK.Synchronize`:

```csharp
using CESDK.Classes;

global::CESDK.CESDK.Synchronize(() =>
{
    var addressList = new AddressList();
    MemoryRecord record = addressList.CreateMemoryRecord();
    record.Description = "Player health";
    record.Address = "game.exe+1234";
    record.VarType = VariableType.vtDword;
});
```

The generic overload returns a result:

```csharp
int recordCount = global::CESDK.CESDK.Synchronize(
    () => new AddressList().Count);
```

### Execute Lua

#### High-level execution

`LuaExecutor.Execute` is the simplest way to run Lua and convert returned values to C#.
Lua `nil`, booleans, numbers, strings, and tables become managed values; multiple returns
are stored in `Values`. Nested table conversion is limited to 5 levels and 100 entries
per table.

```csharp
using CESDK.Classes;

LuaResult result = LuaExecutor.Execute(
    "return getOpenedProcessID(), 'ready', { 10, 20, 30 }");

if (result.ReturnCount == 1)
    Console.WriteLine(result.Value);
else if (result.Values is not null)
    foreach (object? value in result.Values)
        Console.WriteLine(value);
```

`LuaExecutor` restores the values it reads from the Lua stack and wraps execution failures
in `LuaExecutorException`.

#### Manual stack API

For Lua functions not covered by a facade, use `PluginContext.Lua`. The API follows the
Lua C stack model: push the function and arguments, call `PCall`, read return values, and
restore the original stack top in `finally`.

```csharp
using CESDK;
using CESDK.Lua;

static int GetOpenedProcessIdManually()
{
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

        return lua.ToInteger(-1);
    }
    finally
    {
        lua.SetTop(initialTop);
    }
}
```

`LuaNative.DoString` is a lower-level alternative to `LuaExecutor`. It leaves every Lua
return value on the stack, so callers must read or discard those values and restore stack
balance themselves.

### Register C# functions in Cheat Engine Lua

Register callbacks during `OnEnable`, after `PluginContext` has been initialized.
`RegisterCEFunction` is preferred for Cheat Engine plugins. Callback arguments are at
Lua stack indices `1..N`; push each return value and return the number of values pushed.

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

    LuaLogger.Print("Call my_plugin_add(2, 3) from Cheat Engine Lua.");
}

protected override void OnDisable()
{
    PluginContext.Lua.DoString("my_plugin_add = nil");
}
```

`RegisterFunction(name, Action)` is available for a parameterless, no-return managed
callback using the standard Lua API. `RegisterCEFunction` uses Cheat Engine's
`LuaRegister`, keeps the delegate alive, accepts arguments, and can return values.

### Other API areas

| Type | Common operations |
| --- | --- |
| `Assembler` | `Assemble`, `AutoAssemble`, `AutoAssembleCheck`, `SetAssemblerMode` |
| `Disassembler` | `Disassemble`, `GetInstructionSize`, `GetFunctionRange`, comments |
| `Debugger` | attach/detach, pause/resume, breakpoints, register access |
| `SymbolManager` / `SymbolWaiter` | modules, symbols, pointer size, synchronous or cancellable symbol waits |
| `MemoryRegions` | enumerate regions, inspect protection, grant full access |
| `AddressList` / `MemoryRecord` | create, find, update, select, freeze, and delete cheat-table records |
| `Speedhack` | `SetSpeed`, `GetSpeed` |
| `Converter` | MD5 and ANSI/UTF-8 conversion |
| `LuaLogger` | `Print`, `Printf`, severity helpers, and non-throwing `TryPrint` |

Facade failures are normally wrapped in a feature-specific `CesdkException` subtype
(`MemoryAccessException`, `AobScanException`, `AddressResolutionException`, and so on).
Catch the narrow exception when recovery differs by operation.

## Contributors

<a href="https://github.com/ShadowNineX/CESDK/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=ShadowNineX/CESDK" alt="Contributors" />
</a>

Made with [contrib.rocks](https://contrib.rocks).

## License

[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2FShadowNineX%2FCESDK.svg?type=large)](https://app.fossa.com/projects/git%2Bgithub.com%2FShadowNineX%2FCESDK?ref=badge_large)
