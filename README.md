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

## Install

NuGet package: https://www.nuget.org/packages/CESDK

```bash
dotnet add package CESDK
```

## Requirements

- .NET Framework 4.8.1
- Cheat Engine 7.0 or later
- Windows

## Contributors

<a href="https://github.com/ShadowNineX/CESDK/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=ShadowNineX/CESDK" alt="Contributors" />
</a>

Made with [contrib.rocks](https://contrib.rocks).

## License

[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2FShadowNineX%2FCESDK.svg?type=large)](https://app.fossa.com/projects/git%2Bgithub.com%2FShadowNineX%2FCESDK?ref=badge_large)
