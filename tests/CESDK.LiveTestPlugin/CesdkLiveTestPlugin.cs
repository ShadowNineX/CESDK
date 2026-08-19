using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using CESDK;
using CESDK.Classes;

namespace CESDK.LiveTestPlugin;

public sealed class CesdkLiveTestPlugin : CheatEnginePlugin
{
    private const string DefaultResultFileName = "cesdk-live-tests-result.json";

    public override string Name => "CESDK Live Tests";

    protected override void OnEnable()
    {
        PluginContext.Lua.RegisterFunction("__cesdk_run_attached_live_tests", () => RunAllTests(useAttachedTarget: true));
        PluginContext.Lua.DoString(@"
            local menu=MainForm.Menu
            cesdkLiveTopMenu=createMenuItem(menu)
            cesdkLiveTopMenu.Caption='CESDK Tests'
            menu.Items.insert(MainForm.miHelp.MenuIndex,cesdkLiveTopMenu)

            cesdkLiveRunMenu=createMenuItem(menu)
            cesdkLiveRunMenu.Caption='Run Tests Against Attached Process'
            cesdkLiveRunMenu.Enabled=getOpenedProcessID()>0
            cesdkLiveRunMenu.OnClick=function()
                __cesdk_run_attached_live_tests()
            end
            cesdkLiveTopMenu.add(cesdkLiveRunMenu)

            cesdkLiveTopMenu.OnClick=function()
                cesdkLiveRunMenu.Enabled=getOpenedProcessID()>0
            end
        ");

        RunAllTests(useAttachedTarget: false);
    }

    protected override void OnDisable()
    {
        PluginContext.Lua.DoString(@"
            if cesdkLiveRunMenu then
                cesdkLiveRunMenu.destroy()
                cesdkLiveRunMenu=nil
            end
            if cesdkLiveTopMenu then
                cesdkLiveTopMenu.destroy()
                cesdkLiveTopMenu=nil
            end
        ");
    }

    private void RunAllTests(bool useAttachedTarget)
    {
        string resultPath = GetResultPath();
        bool mutatingTestsEnabled =
            Environment.GetEnvironmentVariable("CESDK_LIVE_MUTATING") == "1";
        PrintSeparator();
        LuaLogger.TryPrint("CESDK LIVE TESTS STARTED");
        LuaLogger.TryPrint($"JSON result file: {resultPath}");
        LuaLogger.TryPrint($"Mutating tests: {(mutatingTestsEnabled ? "ENABLED" : "disabled")}");
        PrintSeparator();

        LiveTestReport report = new()
        {
            Plugin = Name,
            StartedAtUtc = DateTimeOffset.UtcNow,
            MutatingTestsEnabled = mutatingTestsEnabled,
            Tests = []
        };

        string? targetPidText = Environment.GetEnvironmentVariable("CESDK_LIVE_TARGET_PID");
        bool targetReady =
            useAttachedTarget &&
            global::CESDK.Classes.Process.GetOpenedProcessID() > 0;
        if (!targetReady && !string.IsNullOrWhiteSpace(targetPidText))
        {
            Run(report, "process-open-configured-target", () =>
            {
                AssertTrue(int.TryParse(targetPidText, out int targetPid) && targetPid > 0, "CESDK_LIVE_TARGET_PID must be a positive integer.");
                global::CESDK.Classes.Process.OpenProcess(targetPid);
                AssertEqual(targetPid, global::CESDK.Classes.Process.GetOpenedProcessID(), "CESDK should attach the configured disposable target.");
            });
            targetReady =
                int.TryParse(targetPidText, out int configuredTargetPid) &&
                global::CESDK.Classes.Process.GetOpenedProcessID() == configuredTargetPid;
        }

        report.AttachedTargetProcessId =
            targetReady ? global::CESDK.Classes.Process.GetOpenedProcessID() : null;

        Run(report, "lua-native-do-string", LuaNativeDoStringLeavesExpectedValues);
        Run(report, "lua-native-register-function", LuaNativeRegisterFunctionCallsManagedCode);
        Run(report, "lua-native-register-ce-function", LuaNativeRegisterCeFunctionCallsManagedCode);
        Run(report, "lua-executor-multiple-results", LuaExecutorReadsMultipleResults);
        Run(report, "lua-executor-table-results", LuaExecutorReadsTables);
        Run(report, "converter-string-md5", ConverterReturnsExpectedMd5);
        Run(report, "cesdk-synchronize", SynchronizeExecutesCallback);
        Run(report, "plugin-context-current-plugin", PluginContextHasCurrentPlugin);
        Run(report, "plugin-logger-nlog-file", PluginLoggerWritesCanonicalFile);
        Run(report, "lua-logger-print", LuaLoggerPrints);
        RunForTarget(report, targetReady, "process-list-attached-target", ProcessReportsAttachedTarget);
        RunForTarget(report, targetReady, "process-control-status", ProcessControlReportsStatus);
        RunForTarget(report, targetReady, "address-resolver-module", AddressResolverResolvesModule);
        RunForTarget(report, targetReady, "symbol-manager-modules", SymbolManagerReportsModules);
        Run(report, "symbol-waiter-sections", SymbolWaiterCompletes);
        RunForTarget(report, targetReady, "thread-list-current-process", ThreadListReportsThreads);
        RunForTarget(report, targetReady, "memory-regions-enumeration", MemoryRegionsReportAttachedProcess);
        Run(report, "debugger-status-queries", DebuggerStatusQueriesReturn);
        RunForTarget(report, targetReady, "speedhack-current-speed", SpeedhackReportsSpeed);
        Run(report, "dbvm-availability", DbvmAvailabilityReturns);
        Run(report, "assembler-nop", AssemblerProducesNop);
        RunForTarget(report, targetReady, "disassembler-module", DisassemblerReadsModuleCode);
        if (mutatingTestsEnabled && targetReady)
        {
            Run(report, "address-list-record-lifecycle", AddressListRecordLifecycle);
            Run(report, "structure-manager-lifecycle", StructureManagerLifecycle);
            Run(report, "debugger-windows-reattach-lifecycle", DebuggerWindowsReattachLifecycle);
            Run(report, "cheat-table-save", CheatTableSavesFile);
            Run(report, "symbol-registry-lifecycle", SymbolRegistryLifecycle);
            Run(report, "memory-access-read-write", MemoryAccessRoundTripsValues);
            Run(report, "advanced-memory-copy-compare-file", AdvancedMemoryRoundTrips);
            Run(report, "pointer-chains-resolve", PointerChainsResolveAllocatedMemory);
            Run(report, "aob-scanner-allocated-marker", AobScannerFindsAllocatedMarker);
            Run(report, "memscan-bounded-marker", MemScanFindsBoundedMarker);
            Run(report, "memscan-next-scan-foundlist-lifecycle", MemScanNextScanReusesFoundList);
            Run(report, "found-list-lifecycle", FoundListReadsBoundedResults);
            Run(report, "injection-script-generation", InjectionGeneratesScript);
        }
        else if (mutatingTestsEnabled)
        {
            const string reason = "Mutating coverage requires a successfully attached disposable target.";
            Skip(report, "address-list-record-lifecycle", reason);
            Skip(report, "structure-manager-lifecycle", reason);
            Skip(report, "debugger-windows-reattach-lifecycle", reason);
            Skip(report, "cheat-table-save", reason);
            Skip(report, "symbol-registry-lifecycle", reason);
            Skip(report, "memory-access-read-write", reason);
            Skip(report, "advanced-memory-copy-compare-file", reason);
            Skip(report, "pointer-chains-resolve", reason);
            Skip(report, "aob-scanner-allocated-marker", reason);
            Skip(report, "memscan-bounded-marker", reason);
            Skip(report, "memscan-next-scan-foundlist-lifecycle", reason);
            Skip(report, "found-list-lifecycle", reason);
            Skip(report, "injection-script-generation", reason);
        }

        Run(report, "ce-object-wrapper-double-dispose", CeObjectWrapperDoubleDisposeIsSafe);

        report.FinishedAtUtc = DateTimeOffset.UtcNow;
        report.Success = report.Tests.All(test => test.Success || test.Skipped);

        WriteReport(report, resultPath);
        PrintCompletion(report, resultPath);
    }

    private static void LuaNativeDoStringLeavesExpectedValues()
    {
        var lua = PluginContext.Lua;
        int initialTop = lua.GetTop();

        try
        {
            lua.DoString("return 'cesdk', 42, true");

            AssertEqual(initialTop + 3, lua.GetTop(), "DoString should leave three return values on the stack.");
            AssertEqual("cesdk", lua.ToString(-3), "First return value should be a string.");
            AssertEqual(42, lua.ToInteger(-2), "Second return value should be an integer.");
            AssertTrue(lua.ToBoolean(-1), "Third return value should be true.");
        }
        finally
        {
            lua.SetTop(initialTop);
        }
    }

    private static void LuaNativeRegisterFunctionCallsManagedCode()
    {
        var lua = PluginContext.Lua;
        int initialTop = lua.GetTop();
        int calls = 0;

        try
        {
            lua.RegisterFunction("__cesdk_live_managed_callback", () => calls++);
            lua.DoString("__cesdk_live_managed_callback()");

            AssertEqual(1, calls, "Registered Lua function should call managed code exactly once.");
        }
        finally
        {
            lua.SetTop(initialTop);
            lua.DoString("__cesdk_live_managed_callback = nil");
            lua.SetTop(initialTop);
        }
    }

    private static void LuaNativeRegisterCeFunctionCallsManagedCode()
    {
        var lua = PluginContext.Lua;
        int initialTop = lua.GetTop();
        int calls = 0;

        try
        {
            lua.RegisterCEFunction("__cesdk_live_ce_callback", _ =>
            {
                calls++;
                lua.PushString("ok");
                return 1;
            });

            lua.DoString("return __cesdk_live_ce_callback()");

            AssertEqual(1, calls, "Registered CE Lua function should call managed code exactly once.");
            AssertEqual(initialTop + 1, lua.GetTop(), "CE callback should leave one return value on the stack.");
            AssertEqual("ok", lua.ToString(-1), "CE callback should return its pushed value.");
        }
        finally
        {
            lua.SetTop(initialTop);
            lua.DoString("__cesdk_live_ce_callback = nil");
            lua.SetTop(initialTop);
        }
    }

    private static void LuaExecutorReadsMultipleResults()
    {
        LuaResult result = LuaExecutor.Execute("return 12, 'ok', false");
        List<object?> values = AssertIs<List<object?>>(result.Values, "Multiple return values should be present.");

        AssertEqual(3, result.ReturnCount, "LuaExecutor should report three return values.");
        AssertEqual(12L, AssertIs<long>(values[0], "First return value should be an integer."), "First return value should match.");
        AssertEqual("ok", AssertIs<string>(values[1], "Second return value should be a string."), "Second return value should match.");
        AssertFalse(AssertIs<bool>(values[2], "Third return value should be false."), "Third return value should be false.");
    }

    private static void LuaExecutorReadsTables()
    {
        LuaResult arrayResult = LuaExecutor.Execute("return { 5, 'six', true }");
        List<object?> array = AssertIs<List<object?>>(arrayResult.Value, "Sequential table should become a list.");

        AssertEqual(3, array.Count, "Sequential table should contain three entries.");
        AssertEqual(5L, AssertIs<long>(array[0], "First array entry should be an integer."), "First array entry should match.");
        AssertEqual("six", AssertIs<string>(array[1], "Second array entry should be a string."), "Second array entry should match.");
        AssertTrue(AssertIs<bool>(array[2], "Third array entry should be true."), "Third array entry should be true.");

        LuaResult dictResult = LuaExecutor.Execute("return { name = 'cesdk', count = 3, nested = { ok = true } }");
        Dictionary<string, object?> dict = AssertIs<Dictionary<string, object?>>(dictResult.Value, "Record table should become a dictionary.");
        Dictionary<string, object?> nested = AssertIs<Dictionary<string, object?>>(dict["nested"], "Nested table should become a dictionary.");

        AssertEqual("cesdk", AssertIs<string>(dict["name"], "Dictionary name should be a string."), "Dictionary name should match.");
        AssertEqual(3L, AssertIs<long>(dict["count"], "Dictionary count should be an integer."), "Dictionary count should match.");
        AssertTrue(AssertIs<bool>(nested["ok"], "Nested ok value should be true."), "Nested ok value should be true.");
    }

    private static void ConverterReturnsExpectedMd5()
    {
        string md5 = Converter.StringToMD5("abc");
        AssertEqual("900150983cd24fb0d6963f7d28e17f72", md5.ToLowerInvariant(), "CE MD5 helper should match the known digest for abc.");
    }

    private static void SynchronizeExecutesCallback()
    {
        bool called = false;

        global::CESDK.CESDK.Synchronize(() => called = true);

        AssertTrue(called, "Synchronize should execute the managed callback.");
    }

    private static void PluginContextHasCurrentPlugin()
    {
        AssertTrue(global::CESDK.CESDK.CurrentPlugin != null, "CESDK should retain the current plugin instance.");
        AssertEqual("CESDK Live Tests", global::CESDK.CESDK.CurrentPlugin!.Name, "Current plugin name should match the loaded live-test plugin.");
        AssertTrue(PluginContext.Lua != null, "PluginContext should expose the shared Lua state.");
    }

    private static void PluginLoggerWritesCanonicalFile()
    {
        string expectedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CeMCP",
            "ce-mcp.log");
        string marker = $"cesdk-live-nlog-{Guid.NewGuid():N}";

        AssertEqual(expectedPath, PluginLogger.LogFilePath, "CESDK and ce-mcp should use one canonical log path.");
        PluginLogger.Log(marker);
        PluginLogger.LogException(new InvalidOperationException($"{marker}-exception"));
        PluginLogger.Flush();

        AssertTrue(File.Exists(expectedPath), "NLog should create the canonical log file.");
        string log = File.ReadAllText(expectedPath);
        AssertTrue(log.Contains(marker, StringComparison.Ordinal), "NLog file should contain the live-test marker.");
        AssertTrue(log.Contains("InvalidOperationException", StringComparison.Ordinal), "NLog file should include exception details.");
    }

    private static void LuaLoggerPrints() =>
        AssertTrue(LuaLogger.TryPrint("CESDK LuaLogger live test"), "LuaLogger should print through CE's Lua console.");

    private static void ProcessReportsAttachedTarget()
    {
        int processId = global::CESDK.Classes.Process.GetOpenedProcessID();
        AssertTrue(processId > 0, "A target process must be attached for CESDK live tests.");

        Dictionary<int, string> processes = global::CESDK.Classes.Process.GetProcessList();
        AssertTrue(processes.ContainsKey(processId), "CE's process list should contain the attached process.");
        AssertFalse(string.IsNullOrWhiteSpace(processes[processId]), "Attached process should have a name.");
    }

    private static void ProcessControlReportsStatus()
    {
        _ = ProcessControl.IsPaused();
        AssertTrue(ProcessControl.GetForegroundProcessId() > 0, "Foreground process ID should be positive.");
        AssertTrue(ProcessControl.GetProcessAge() >= 0, "Attached process age should not be negative.");
    }

    private static void AddressResolverResolvesModule()
    {
        ModuleInfo module = GetMainModule();
        AssertEqual(module.Address, AddressResolver.GetAddress(module.Name), "Module name should resolve to its base address.");
        AssertEqual(module.Address, AddressResolver.GetAddressSafe(module.Name), "Safe module resolution should match its base address.");
        AssertTrue(AddressResolver.InModule(module.Address), "Main module base should be inside a module.");
        AssertFalse(string.IsNullOrWhiteSpace(AddressResolver.GetNameFromAddress(module.Address)), "Module base should have a symbolic name.");
        _ = AddressResolver.InSystemModule(module.Address);
    }

    private static void SymbolManagerReportsModules()
    {
        ModuleInfo module = GetMainModule();
        AssertTrue(module.Size > 0, "Main module should have a nonzero size.");
        AssertEqual(module.Size, SymbolManager.GetModuleSize(module.Name), "Module-size lookup should match module enumeration.");
        AssertTrue(SymbolManager.GetPointerSize() is 4 or 8, "Target pointer size should be 4 or 8 bytes.");
        _ = SymbolManager.GetSymbolInfo(module.Name);
        _ = SymbolManager.SymbolsDoneLoading();
    }

    private static void SymbolWaiterCompletes() =>
        SymbolWaiter.WaitForSections();

    private static void ThreadListReportsThreads()
    {
        var threads = new ThreadList();
        AssertTrue(threads.Count > 0, "Attached process should expose at least one thread.");
        AssertEqual(threads.Count, threads.GetAllThreadIds().Length, "Thread ID array should match Count.");
        AssertEqual(threads.Count, threads.GetAllThreadIdsAsInt().Length, "Integer thread ID array should match Count.");
    }

    private static void MemoryRegionsReportAttachedProcess()
    {
        List<MemoryRegion> regions = MemoryRegions.EnumMemoryRegions();
        AssertTrue(regions.Count > 0, "Attached process should expose memory regions.");

        ModuleInfo module = GetMainModule();
        MemoryProtection protection = MemoryRegions.GetMemoryProtection(module.Address);
        AssertTrue(protection.Read || protection.Write || protection.Execute, "Main module should have at least one protection flag.");
    }

    private static void DebuggerStatusQueriesReturn()
    {
        _ = global::CESDK.Classes.Debugger.IsDebugging();
        _ = global::CESDK.Classes.Debugger.IsBroken();
        _ = global::CESDK.Classes.Debugger.IsStepping();
        _ = global::CESDK.Classes.Debugger.CanBreak();
        _ = global::CESDK.Classes.Debugger.IsPaused();
        _ = global::CESDK.Classes.Debugger.GetCurrentDebuggerInterface();
        _ = global::CESDK.Classes.Debugger.GetBreakpointList();
    }

    private static void DebuggerWindowsReattachLifecycle()
    {
        int processId = global::CESDK.Classes.Process.GetOpenedProcessID();
        AssertTrue(processId > 0, "A disposable target must be attached for debugger lifecycle coverage.");

        try
        {
            for (int cycle = 1; cycle <= 2; cycle++)
            {
                DebuggerAttachResult attached =
                    global::CESDK.Classes.Debugger.DebugProcess(1);
                AssertTrue(
                    global::CESDK.Classes.Debugger.IsDebugging(),
                    $"Windows debugger cycle {cycle} should attach.");
                AssertEqual(
                    1,
                    attached.ActualInterface,
                    $"Windows debugger cycle {cycle} should use interface 1.");

                DebuggerAttachResult idempotent =
                    global::CESDK.Classes.Debugger.DebugProcess(1);
                AssertTrue(
                    idempotent.AlreadyAttached,
                    $"Windows debugger cycle {cycle} should be idempotent.");
                AssertEqual(
                    processId,
                    global::CESDK.Classes.Process.GetOpenedProcessID(),
                    $"Idempotent attach cycle {cycle} should preserve the target PID.");

                global::CESDK.Classes.Debugger.DetachIfPossible();
                AssertFalse(
                    global::CESDK.Classes.Debugger.IsDebugging(),
                    $"Windows debugger cycle {cycle} should detach.");
                AssertEqual(
                    processId,
                    global::CESDK.Classes.Process.GetOpenedProcessID(),
                    $"Windows debugger cycle {cycle} should reopen and preserve the target PID.");
            }
        }
        finally
        {
            if (global::CESDK.Classes.Debugger.IsDebugging())
                global::CESDK.Classes.Debugger.DetachIfPossible();
        }
    }

    private static void SpeedhackReportsSpeed() =>
        AssertTrue(Speedhack.GetSpeed() > 0, "Speedhack speed should be positive.");

    private static void DbvmAvailabilityReturns() =>
        _ = Dbvm.IsAvailable();

    private static void AssemblerProducesNop()
    {
        byte[] bytes = Assembler.Assemble("nop");
        AssertEqual(1, bytes.Length, "NOP should assemble to one byte.");
        AssertEqual((byte)0x90, bytes[0], "NOP opcode should be 0x90.");
    }

    private static void DisassemblerReadsModuleCode()
    {
        ModuleInfo module = GetMainModule();
        string text = Disassembler.Disassemble(module.Address) ?? "";
        AssertFalse(string.IsNullOrWhiteSpace(text), "Main module entry should disassemble.");
        AssertTrue(Disassembler.GetInstructionSize(module.Address) > 0, "Disassembled instruction should have a positive size.");
        _ = Disassembler.SplitDisassembledString(text);
    }

    private static void AddressListRecordLifecycle()
    {
        using var addressList = new AddressList();
        int initialCount = addressList.Count;
        string description = $"CESDK live {Guid.NewGuid():N}";
        MemoryRecord record = addressList.CreateMemoryRecord();

        try
        {
            record.Description = description;
            record.Address = GetMainModule().Address.ToString("X");
            record.VarType = VariableType.vtByte;
            record.BeginEdit();
            record.EndEdit();
            addressList.RebuildDescriptionCache();

            AssertEqual(initialCount + 1, addressList.Count, "Creating a memory record should increment address-list count.");
            AssertEqual(description, record.Description, "Memory-record description should round-trip.");
            AssertEqual(record.ID, addressList.GetMemoryRecordByDescription(description)?.ID, "Description lookup should find the created record.");
            AssertEqual(record.ID, addressList.GetMemoryRecordByID(record.ID)?.ID, "ID lookup should find the created record.");
            AssertTrue(addressList.GetMemoryRecordsWithDescription(description).Count >= 1, "Description-list lookup should find the created record.");
        }
        finally
        {
            addressList.DeleteMemoryRecord(record);
        }

        AssertEqual(initialCount, addressList.Count, "Deleting the memory record should restore address-list count.");
    }

    private static void StructureManagerLifecycle()
    {
        string name = $"CESDK_LIVE_{Guid.NewGuid():N}";
        try
        {
            StructureInfo created = StructureManager.Create(
                name,
                [new StructureElementDefinition(0, "Value", (int)VariableType.vtDword, 4)]);
            AssertEqual(name, created.Name, "Created structure should preserve its name.");
            AssertEqual(1, created.Elements.Count, "Created structure should contain one element.");
            AssertEqual(name, StructureManager.Get(name)?.Name, "Structure lookup should find the created structure.");

            StructureInfo extended = StructureManager.AddElement(
                name,
                new StructureElementDefinition(4, "Next", (int)VariableType.vtDword, 4));
            AssertEqual(2, extended.Elements.Count, "Adding an element should update the structure.");

            StructureInfo reduced = StructureManager.RemoveElement(name, 1);
            AssertEqual(1, reduced.Elements.Count, "Removing an element should update the structure.");
            AssertTrue(StructureManager.List().Any(item => item.Name == name), "Structure list should contain the created structure.");
        }
        finally
        {
            if (StructureManager.Get(name) != null)
                StructureManager.Remove(name);
        }

        AssertTrue(StructureManager.Get(name) == null, "Removed structure should no longer be returned.");
    }

    private static void CheatTableSavesFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cesdk-live-{Guid.NewGuid():N}.CT");
        try
        {
            CheatTable.Save(path);
            AssertTrue(File.Exists(path), "CheatTable.Save should create the requested file.");
            AssertTrue(new FileInfo(path).Length > 0, "Saved Cheat Engine table should not be empty.");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void SymbolRegistryLifecycle()
    {
        string name = $"CESDK_LIVE_SYMBOL_{Guid.NewGuid():N}";
        ulong address = GetMainModule().Address;
        try
        {
            SymbolRegistry.Register(name, address, doNotSave: true);
            AssertEqual(address, AddressResolver.GetAddress(name), "Registered symbol should resolve to its address.");
            AssertTrue(SymbolRegistry.Enumerate() != null, "Registered-symbol enumeration should return a value.");
        }
        finally
        {
            SymbolRegistry.Unregister(name);
        }

        AssertTrue(AddressResolver.GetAddressSafe(name) == null, "Unregistered symbol should no longer resolve.");
    }

    private static void MemoryAccessRoundTripsValues()
    {
        ulong address = AdvancedMemory.Allocate(128);
        try
        {
            AssertTrue(MemoryAccess.WriteByte(address, 0xA5), "writeByte should succeed.");
            AssertTrue(MemoryAccess.WriteSmallInteger(address + 2, -1234), "writeSmallInteger should succeed.");
            AssertTrue(MemoryAccess.WriteInteger(address + 4, 0x12345678), "writeInteger should succeed.");
            AssertTrue(MemoryAccess.WriteQword(address + 8, 0x123456789ABCDE), "writeQword should succeed.");
            AssertTrue(MemoryAccess.WriteFloat(address + 16, 12.5f), "writeFloat should succeed.");
            AssertTrue(MemoryAccess.WriteDouble(address + 24, 123.25), "writeDouble should succeed.");
            AssertTrue(MemoryAccess.WriteString(address + 40, "CESDK"), "writeString should succeed.");
            AssertTrue(MemoryAccess.WriteBytes(address + 64, [1, 2, 3, 4]), "writeBytes should succeed.");

            AssertEqual((byte)0xA5, MemoryAccess.ReadByte(address), "Byte should round-trip.");
            AssertEqual((short)-1234, MemoryAccess.ReadSmallInteger(address + 2), "Small integer should round-trip.");
            AssertEqual(0x12345678, MemoryAccess.ReadInteger(address + 4), "Integer should round-trip.");
            AssertEqual(0x123456789ABCDEL, MemoryAccess.ReadQword(address + 8), "Qword should round-trip.");
            AssertEqual((ulong)0x123456789ABCDE, MemoryAccess.ReadPointer(address + 8), "Pointer should read the stored qword.");
            AssertTrue(Math.Abs(MemoryAccess.ReadFloat(address + 16) - 12.5f) < 0.001f, "Float should round-trip.");
            AssertTrue(Math.Abs(MemoryAccess.ReadDouble(address + 24) - 123.25) < 0.001, "Double should round-trip.");
            AssertEqual("CESDK", MemoryAccess.ReadString(address + 40, 16), "String should round-trip.");
            AssertTrue(MemoryAccess.ReadBytes(address + 64, 4).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "Byte array should round-trip.");
        }
        finally
        {
            AdvancedMemory.Free(address);
        }
    }

    private static void AdvancedMemoryRoundTrips()
    {
        ulong source = AdvancedMemory.Allocate(64);
        ulong destination = AdvancedMemory.Allocate(64);
        string path = Path.Combine(Path.GetTempPath(), $"cesdk-memory-{Guid.NewGuid():N}.bin");
        byte[] bytes = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

        try
        {
            AssertTrue(MemoryAccess.WriteBytes(source, bytes), "Source bytes should be writable.");
            AdvancedMemory.FullAccess(source, 64);
            AdvancedMemory.SetProtection(source, 64, read: true, write: true, execute: false);

            AssertEqual(destination, AdvancedMemory.Copy(source, (ulong)bytes.Length, destination), "Copy should report the supplied destination.");
            MemoryComparison equal = AdvancedMemory.Compare(source, destination, (ulong)bytes.Length);
            AssertTrue(equal.Equal, "Copied memory should compare equal.");
            AssertTrue(equal.FirstDifferenceOffset == null, "Equal memory should not report a difference.");

            AssertTrue(MemoryAccess.WriteByte(destination + 5, 0xFF), "Destination mutation should succeed.");
            MemoryComparison different = AdvancedMemory.Compare(source, destination, (ulong)bytes.Length);
            AssertFalse(different.Equal, "Mutated memory should compare different.");
            AssertEqual((ulong?)5, different.FirstDifferenceOffset, "Comparison should report the first differing offset.");

            string md5 = AdvancedMemory.Md5(source, (ulong)bytes.Length);
            AssertEqual(32, md5.Length, "Memory MD5 should contain 32 hexadecimal characters.");
            AssertEqual((long)bytes.Length, AdvancedMemory.DumpToFile(path, source, (ulong)bytes.Length), "Dump should report every written byte.");
            AssertTrue(File.ReadAllBytes(path).SequenceEqual(bytes), "Dumped file should match source memory.");

            AssertTrue(MemoryAccess.WriteBytes(destination, new byte[bytes.Length]), "Destination clearing should succeed.");
            AdvancedMemory.LoadFromFile(path, destination);
            AssertTrue(MemoryAccess.ReadBytes(destination, bytes.Length).SequenceEqual(bytes), "Loaded file should restore destination bytes.");
        }
        finally
        {
            AdvancedMemory.Free(source);
            AdvancedMemory.Free(destination);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void PointerChainsResolveAllocatedMemory()
    {
        ulong holder = AdvancedMemory.Allocate(16);
        ulong target = AdvancedMemory.Allocate(16);
        try
        {
            AssertTrue(MemoryAccess.WriteQword(holder, unchecked((long)target)), "Pointer storage should be writable.");

            PointerChainResult result = PointerChains.Resolve(holder, [0]);
            AssertTrue(result.Valid, "Allocated pointer chain should resolve as readable.");
            AssertEqual(target, result.Address, "Pointer chain should resolve to the allocated target.");
            AssertEqual(1, result.Steps.Count, "One offset should produce one pointer-chain step.");

            List<ulong> references = PointerChains.FindDirectReferences(
                target,
                holder,
                holder + 8,
                SymbolManager.GetPointerSize(),
                string.Empty,
                10);
            AssertTrue(references.Contains(holder), "Bounded pointer scan should find the direct reference.");
        }
        finally
        {
            AdvancedMemory.Free(holder);
            AdvancedMemory.Free(target);
        }
    }

    private static void AobScannerFindsAllocatedMarker()
    {
        byte[] marker = CreateMarker();
        string pattern = ToAobPattern(marker);
        ulong address = AdvancedMemory.Allocate((ulong)marker.Length);
        try
        {
            AssertTrue(MemoryAccess.WriteBytes(address, marker), "AOB marker should be writable.");
            List<ulong> matches = AobScanner.Scan(pattern, string.Empty, 0, string.Empty);
            AssertTrue(matches.Contains(address), "AOBScan should return the allocated marker address.");
            AssertEqual((ulong?)address, AobScanner.ScanUnique(pattern, string.Empty, 0, string.Empty), "AOBScanUnique should return the marker address.");
        }
        finally
        {
            AdvancedMemory.Free(address);
        }
    }

    private static void MemScanFindsBoundedMarker()
    {
        byte[] marker = CreateMarker();
        ulong address = AdvancedMemory.Allocate((ulong)marker.Length);
        try
        {
            AssertTrue(MemoryAccess.WriteBytes(address, marker), "MemScan marker should be writable.");
            using var scanner = new MemScan();
            scanner.NewScan();
            scanner.FirstScan(CreateByteScan(address, marker[0]));
            scanner.WaitTillDone();
            scanner.InitializeResults();
            try
            {
                AssertTrue(scanner.GetResultCount() >= 1, "Bounded MemScan should find the marker.");
                AssertEqual(address, ParseAddress(scanner.GetResultAddress(0)), "First bounded MemScan result should be the marker address.");
                AssertFalse(string.IsNullOrWhiteSpace(scanner.GetResultValue(0)), "Bounded MemScan result should expose a value.");
            }
            finally
            {
                scanner.DeinitializeResults();
            }
        }
        finally
        {
            AdvancedMemory.Free(address);
        }
    }

    private static void MemScanNextScanReusesFoundList()
    {
        const byte firstValue = 109;
        const byte nextValue = 108;
        ulong address = AdvancedMemory.Allocate(1);
        try
        {
            AssertTrue(MemoryAccess.WriteByte(address, firstValue), "First-scan value should be writable.");
            using var scanner = new MemScan();
            scanner.NewScan();
            scanner.Scan(CreateByteScan(address, firstValue));
            scanner.WaitTillDone();
            scanner.InitializeResults();
            AssertTrue(scanner.GetResultCount() >= 1, "First scan should find the bounded byte.");
            AssertEqual(address, ParseAddress(scanner.GetResultAddress(0)), "First scan should return the bounded byte.");

            scanner.DeinitializeResults();
            AssertTrue(MemoryAccess.WriteByte(address, nextValue), "Next-scan value should be writable.");
            scanner.Scan(CreateByteScan(address, nextValue));
            scanner.WaitTillDone();
            scanner.InitializeResults();
            try
            {
                AssertTrue(scanner.GetResultCount() >= 1, "Next scan should retain the changed bounded byte.");
                AssertEqual(address, ParseAddress(scanner.GetResultAddress(0)), "Next scan should return the bounded byte.");
            }
            finally
            {
                scanner.DeinitializeResults();
            }
        }
        finally
        {
            AdvancedMemory.Free(address);
        }
    }

    private static void FoundListReadsBoundedResults()
    {
        byte[] marker = CreateMarker();
        ulong address = AdvancedMemory.Allocate((ulong)marker.Length);
        try
        {
            AssertTrue(MemoryAccess.WriteBytes(address, marker), "FoundList marker should be writable.");
            using var scanner = new MemScan();
            scanner.NewScan();
            scanner.FirstScan(CreateByteScan(address, marker[0]));
            scanner.WaitTillDone();

            using var foundList = new FoundList(scanner);
            foundList.Initialize();
            try
            {
                AssertTrue(foundList.IsInitialized, "FoundList should report initialized state.");
                AssertTrue(foundList.Count >= 1, "FoundList should contain the bounded marker.");
                AssertEqual(address, ParseAddress(foundList.GetAddress(0)), "FoundList should return the marker address.");
                AssertEqual(foundList.GetAddress(0), foundList[0], "FoundList indexer should match GetAddress.");
                AssertFalse(string.IsNullOrWhiteSpace(foundList.GetValue(0)), "FoundList should return a result value.");
            }
            finally
            {
                foundList.Deinitialize();
            }
            AssertFalse(foundList.IsInitialized, "FoundList should report deinitialized state.");
        }
        finally
        {
            AdvancedMemory.Free(address);
        }
    }

    private static void InjectionGeneratesScript()
    {
        string address = GetMainModule().Address.ToString("X");
        string script = Injection.GenerateCodeInjectionScript(address);
        AssertFalse(string.IsNullOrWhiteSpace(script), "Generated injection script should not be empty.");
        AssertTrue(script.Contains("newmem", StringComparison.OrdinalIgnoreCase), "Generated injection script should define the standard newmem block.");
        AssertTrue(script.Contains(address, StringComparison.OrdinalIgnoreCase), "Generated injection script should reference the requested address.");
    }

    private static void CeObjectWrapperDoubleDisposeIsSafe()
    {
        var addressList = new AddressList();
        addressList.Dispose();
        addressList.Dispose();
    }

    private static ModuleInfo GetMainModule()
    {
        List<ModuleInfo> modules = SymbolManager.EnumModules();
        AssertTrue(modules.Count > 0, "Attached process should expose at least one module.");
        return modules.First(module => module.Address != 0 && module.Size > 0);
    }

    private static byte[] CreateMarker() =>
        Guid.NewGuid().ToByteArray();

    private static string ToAobPattern(byte[] bytes) =>
        string.Join(" ", bytes.Select(value => value.ToString("X2")));

    private static ScanParameters CreateByteScan(ulong address, byte value) =>
        new()
        {
            ScanOption = ScanOption.soExactValue,
            VarType = VariableType.vtByte,
            RoundingType = RoundingType.rtRounded,
            Input1 = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Input2 = string.Empty,
            StartAddress = address,
            StopAddress = address + 1,
            ProtectionFlags = string.Empty,
            AlignmentType = AlignmentType.fsmNotAligned,
            AlignmentParam = string.Empty,
            IsHexadecimalInput = false,
        };

    private static ulong ParseAddress(string value)
    {
        string text = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value.Substring(2)
            : value;
        return ulong.Parse(text, System.Globalization.NumberStyles.HexNumber);
    }

    private static void RunForTarget(
        LiveTestReport report,
        bool targetReady,
        string name,
        Action test)
    {
        if (targetReady)
            Run(report, name, test);
        else
            Skip(report, name, "No disposable CESDK_LIVE_TARGET_PID was configured and attached.");
    }

    private static void Skip(LiveTestReport report, string name, string reason)
    {
        report.Tests.Add(new LiveTestCase
        {
            Name = name,
            Skipped = true,
            SkipReason = reason
        });
        LuaLogger.TryPrint($"SKIPPED: {name} ({reason})");
    }

    private static void Run(LiveTestReport report, string name, Action test)
    {
        LuaLogger.TryPrint($"RUNNING: {name}");

        Stopwatch stopwatch = Stopwatch.StartNew();
        LiveTestCase result = new()
        {
            Name = name
        };

        try
        {
            test();
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.ToString();
        }
        finally
        {
            stopwatch.Stop();
            result.DurationMs = stopwatch.ElapsedMilliseconds;
            report.Tests.Add(result);
        }

        string status = result.Success ? "PASSED" : "FAILED";
        LuaLogger.TryPrint($"{status}: {name} ({result.DurationMs} ms)");
    }

    private static void WriteReport(LiveTestReport report, string resultPath)
    {
        string? directory = Path.GetDirectoryName(resultPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(resultPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static void PrintCompletion(LiveTestReport report, string resultPath)
    {
        int passed = report.Tests.Count(test => test.Success && !test.Skipped);
        int skipped = report.Tests.Count(test => test.Skipped);
        int failed = report.Tests.Count(test => !test.Success && !test.Skipped);

        PrintSeparator();
        LuaLogger.TryPrint("ALL CESDK LIVE TESTS ARE DONE");
        LuaLogger.TryPrint(report.Success ? "FINAL RESULT: ALL EXECUTED TESTS PASSED" : "FINAL RESULT: SOME TESTS FAILED");
        LuaLogger.TryPrint($"SUMMARY: {passed} passed, {skipped} skipped, {failed} failed");
        LuaLogger.TryPrint($"JSON result file: {resultPath}");
        PrintSeparator();
    }

    private static void PrintSeparator() =>
        LuaLogger.TryPrint("============================================================");

    private static string GetResultPath() =>
        Environment.GetEnvironmentVariable("CESDK_LIVE_RESULT")
        ?? Path.Combine(Path.GetTempPath(), DefaultResultFileName);

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }

    private static T AssertIs<T>(object? value, string message)
    {
        if (value is T typedValue)
            return typedValue;

        string actualType = value?.GetType().FullName ?? "<null>";
        throw new InvalidOperationException($"{message} Actual type was {actualType}.");
    }

    private sealed class LiveTestReport
    {
        public string Plugin { get; set; } = "";
        public bool Success { get; set; }
        public bool MutatingTestsEnabled { get; set; }
        public int? AttachedTargetProcessId { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset FinishedAtUtc { get; set; }
        public List<LiveTestCase> Tests { get; set; } = [];
    }

    private sealed class LiveTestCase
    {
        public string Name { get; set; } = "";
        public bool Success { get; set; }
        public bool Skipped { get; set; }
        public string? SkipReason { get; set; }
        public long DurationMs { get; set; }
        public string? Error { get; set; }
    }
}
