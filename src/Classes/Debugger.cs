using System;
using System.Collections.Generic;
using CESDK.Lua;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class DebuggerException : CesdkException
    {
        public DebuggerException(string message) : base(message) { }
        public DebuggerException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>Describes the debugger interface CE actually activated.</summary>
    public sealed class DebuggerAttachResult
    {
        public DebuggerAttachResult(
            int processId,
            int requestedInterface,
            int actualInterface,
            bool alreadyAttached)
        {
            ProcessId = processId;
            RequestedInterface = requestedInterface;
            ActualInterface = actualInterface;
            AlreadyAttached = alreadyAttached;
        }

        public int ProcessId { get; }
        public int RequestedInterface { get; }
        public int ActualInterface { get; }
        public bool AlreadyAttached { get; }
        public bool UsedFallback =>
            RequestedInterface != 0 && RequestedInterface != ActualInterface;
    }

    /// <summary>
    /// Debugger-specific operations: attach, detach, breakpoints, register access, stepping.
    /// For disassembly, use <see cref="Disassembler"/>.
    /// </summary>
    public static class Debugger
    {
        private static readonly LuaNative lua = PluginContext.Lua;
        private static int lastProcessId;
        private static int? lastRequestedInterface;
        private static int? lastActualInterface;

        // ── Process / Attach ─────────────────────────────────────────────────

        /// <summary>
        /// Starts the debugger for the currently opened process.
        /// debugInterface: 0=default, 1=Windows, 2=VEH, 3=kernel.
        /// Matching active interfaces and previously observed CE fallbacks are idempotent.
        /// </summary>
        public static DebuggerAttachResult DebugProcess(int debugInterface = 0) =>
            WrapException(() =>
            {
                if (debugInterface is < 0 or > 3)
                    throw new ArgumentOutOfRangeException(
                        nameof(debugInterface),
                        "Debugger interface must be between 0 and 3");

                int processId = RequireOpenedProcess();
                RequireLiveProcess(processId);
                bool active = IsDebuggingCore();
                int? currentInterface = active ? GetCurrentDebuggerInterfaceCore() : null;
                bool cachedFallback =
                    active &&
                    lastProcessId == processId &&
                    lastRequestedInterface == debugInterface &&
                    lastActualInterface == currentInterface;

                if (active &&
                    currentInterface.HasValue &&
                    (debugInterface == 0 ||
                     currentInterface == debugInterface ||
                     cachedFallback))
                {
                    RememberAttach(processId, debugInterface, currentInterface.Value);
                    return new DebuggerAttachResult(
                        processId,
                        debugInterface,
                        currentInterface.Value,
                        alreadyAttached: true);
                }

                if (active)
                    DetachDebugger(processId);
                else
                    LuaUtils.CallVoidLuaFunction(
                        "unpause",
                        "resume process before debugger attach");

                RequireLiveProcess(processId);
                if (debugInterface == 0)
                    LuaUtils.CallVoidLuaFunction("debugProcess", "start debugging process");
                else
                    LuaUtils.CallVoidLuaFunction(
                        "debugProcess",
                        "start debugging process",
                        debugInterface);

                if (!IsDebuggingCore())
                    throw new InvalidOperationException("Cheat Engine did not activate the debugger");

                int actualInterface = GetCurrentDebuggerInterfaceCore()
                    ?? throw new InvalidOperationException(
                        "Cheat Engine activated a debugger without reporting its interface");
                RememberAttach(processId, debugInterface, actualInterface);
                return new DebuggerAttachResult(
                    processId,
                    debugInterface,
                    actualInterface,
                    alreadyAttached: false);
            });

        /// <summary>
        /// Unpauses and detaches the debugger while preserving CE's live process handle,
        /// matching the same-PID reattach path in Cheat Engine's process picker.
        /// </summary>
        public static void DetachIfPossible() =>
            WrapException(() =>
            {
                int processId = RequireOpenedProcess();
                RequireLiveProcess(processId);
                DetachDebugger(processId);
                if (IsDebuggingCore())
                    throw new InvalidOperationException(
                        "Cheat Engine still reports an active debugger after detach");

                lastProcessId = processId;
                lastRequestedInterface = null;
                lastActualInterface = null;
            });

        /// <summary>Pauses the currently opened process.</summary>
        public static void Pause() =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("pause", "pause process"));

        /// <summary>Resumes the currently opened process.</summary>
        public static void Unpause() =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("unpause", "unpause process"));

        private static int RequireOpenedProcess()
        {
            int processId = Process.GetOpenedProcessID();
            if (processId <= 0)
                throw new InvalidOperationException("No process is attached");
            return processId;
        }

        private static void RequireLiveProcess(int processId)
        {
            if (!Process.GetProcessList().ContainsKey(processId))
            {
                throw new InvalidOperationException(
                    $"The attached process {processId} is no longer running; open a live process before starting the debugger");
            }
        }

        private static void DetachDebugger(int processId)
        {
            LuaUtils.CallVoidLuaFunction("unpause", "resume process before debugger detach");
            LuaUtils.CallVoidLuaFunction("detachIfPossible", "detach debugger");
            RequireLiveProcess(processId);
            if (Process.GetOpenedProcessID() != processId)
            {
                throw new InvalidOperationException(
                    $"Cheat Engine changed the opened process while detaching debugger from {processId}");
            }
        }

        private static void RememberAttach(
            int processId,
            int requestedInterface,
            int actualInterface)
        {
            lastProcessId = processId;
            lastRequestedInterface = requestedInterface;
            lastActualInterface = actualInterface;
        }

        private static bool IsDebuggingCore() =>
            LuaUtils.CallLuaFunction(
                "debug_isDebugging",
                "check if debugging",
                () => lua.ToBoolean(-1));

        private static int? GetCurrentDebuggerInterfaceCore() =>
            LuaUtils.CallLuaFunction(
                "debug_getCurrentDebuggerInterface",
                "get current debugger interface",
                () => lua.IsNil(-1) ? (int?)null : lua.ToInteger(-1));

        // ── Status Queries ───────────────────────────────────────────────────

        /// <summary>Returns true if the debugger has been started.</summary>
        public static bool IsDebugging() =>
            WrapException(() => LuaUtils.CallLuaFunction(
                "debug_isDebugging", "check if debugging", () => lua.ToBoolean(-1)));

        /// <summary>Returns true if the debugger is currently halted on a thread.</summary>
        public static bool IsBroken() =>
            WrapException(() => LuaUtils.CallLuaFunction(
                "debug_isBroken", "check if broken", () => lua.ToBoolean(-1)));

        /// <summary>Returns true if the debugger was single-stepping.</summary>
        public static bool IsStepping() =>
            WrapException(() => LuaUtils.CallLuaFunction(
                "debug_isStepping", "check if stepping", () => lua.ToBoolean(-1)));

        /// <summary>Returns true if there is a possibility the target can stop on a breakpoint.</summary>
        public static bool CanBreak() =>
            WrapException(() => LuaUtils.CallLuaFunction(
                "debug_canBreak", "check if can break", () => lua.ToBoolean(-1)));

        /// <summary>Returns true if the target is paused by CE or broken on a breakpoint.</summary>
        public static bool IsPaused() =>
            WrapException(() => LuaUtils.CallLuaFunction(
                "isPaused", "check if paused", () => lua.ToBoolean(-1)));

        /// <summary>
        /// Returns the current debugger interface: 1=Windows, 2=VEH, 3=kernel,
        /// 4=macOS native, 5=GDB, or null when no debugger is active.
        /// </summary>
        public static int? GetCurrentDebuggerInterface() =>
            WrapException(() => LuaUtils.CallLuaFunction(
                "debug_getCurrentDebuggerInterface",
                "get current debugger interface",
                () => lua.IsNil(-1) ? (int?)null : lua.ToInteger(-1)));

        // ── Breakpoints ──────────────────────────────────────────────────────

        /// <summary>Returns a list of all active breakpoint addresses.</summary>
        public static List<ulong> GetBreakpointList() =>
            WrapException(() => LuaUtils.CallLuaFunction(
                "debug_getBreakpointList",
                "get breakpoint list",
                () =>
                {
                    if (lua.IsNil(-1))
                        return [];

                    if (!lua.IsTable(-1))
                        throw new InvalidOperationException("debug_getBreakpointList did not return a table");

                    var list = new List<ulong>();
                    lua.PushNil();
                    while (lua.Next(-2) != 0)
                    {
                        try
                        {
                            if (!lua.IsNumber(-1))
                                throw new InvalidOperationException("Breakpoint list contains a non-address value");
                            list.Add((ulong)lua.ToInteger64(-1));
                        }
                        finally
                        {
                            lua.Pop(1);
                        }
                    }
                    return list;
                }));

        /// <summary>
        /// Sets a breakpoint at the given address without a callback (breaking breakpoint).
        /// trigger: "bptExecute" (default), "bptWrite", or "bptAccess".
        /// For bptExecute, size is ignored. For bptWrite/bptAccess, size is the watch size in bytes.
        /// </summary>
        public static void SetBreakpoint(ulong address, int size = 1, string trigger = "bptExecute") =>
            SetBreakpointCore("debug_setBreakpoint", null, address, size, trigger);

        /// <summary>Removes the breakpoint at the given address.</summary>
        public static void RemoveBreakpoint(ulong address) =>
            WrapException(() => LuaUtils.CallVoidLuaFunction(
                "debug_removeBreakpoint", "remove breakpoint", address));

        // ── Continue / Step ──────────────────────────────────────────────────

        /// <summary>
        /// Continues from the current breakpoint.
        /// method: "co_run" (default), "co_stepinto", or "co_stepover".
        /// </summary>
        public static void ContinueFromBreakpoint(string method = "co_run") =>
            WrapException(() =>
            {
                int initialTop = lua.GetTop();
                try
                {
                    lua.GetGlobal("debug_continueFromBreakpoint");
                    if (!lua.IsFunction(-1))
                        throw new InvalidOperationException("debug_continueFromBreakpoint function not available");
                    PushRequiredGlobal(method);
                    int result = lua.PCall(1, 0);
                    if (result != 0)
                        throw new InvalidOperationException($"debug_continueFromBreakpoint() failed: {lua.ToString(-1)}");
                }
                finally
                {
                    lua.SetTop(initialTop);
                }
            });

        // ── Context / Registers ──────────────────────────────────────────────

        /// <summary>
        /// Fills the global register variables (EAX/RAX, EBX/RBX, etc.) from the broken thread's context.
        /// If extraRegs is true, also fills FP0–FP7 and XMM0–XMM15.
        /// Call this before reading individual registers with GetRegister().
        /// </summary>
        public static void GetContext(bool extraRegs = false) =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("debug_getContext", "get debug context", extraRegs));

        /// <summary>
        /// Applies the current global register variables back to the broken thread's context.
        /// Call SetRegister() to modify values, then call this to commit them.
        /// </summary>
        public static void SetContext(bool extraRegs = false) =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("debug_setContext", "set debug context", extraRegs));

        /// <summary>Refreshes the CE memory-view UI to reflect the current context.</summary>
        public static void UpdateGUI() =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("debug_updateGUI", "update debug GUI"));

        /// <summary>
        /// Reads a CPU register global variable after GetContext() has been called.
        /// Valid names: EAX/RAX, EBX/RBX, ECX/RCX, EDX/RDX, ESI/RSI, EDI/RDI,
        ///              EBP/RBP, ESP/RSP, EIP/RIP, R8–R15, EFLAGS.
        /// </summary>
        public static ulong GetRegister(string name) =>
            WrapException(() =>
            {
                int initialTop = lua.GetTop();
                try
                {
                    lua.GetGlobal(name);
                    if (!lua.IsNumber(-1))
                        throw new InvalidOperationException($"Register '{name}' is not available");
                    return (ulong)lua.ToInteger64(-1);
                }
                finally
                {
                    lua.SetTop(initialTop);
                }
            });

        /// <summary>
        /// Sets a CPU register global variable. Call SetContext() afterwards to commit.
        /// </summary>
        public static void SetRegister(string name, ulong value) =>
            WrapException(() =>
            {
                lua.PushInteger((long)value);
                lua.SetGlobal(name);
            });

        /// <summary>Requests that the debugger break a specific target thread.</summary>
        public static void BreakThread(int threadId) =>
            WrapException(() => LuaUtils.CallVoidLuaFunction(
                "debug_breakThread", "break target thread", threadId));

        /// <summary>Excludes a target thread from breakpoint handling.</summary>
        public static void AddThreadToNoBreakList(int threadId) =>
            WrapException(() => LuaUtils.CallVoidLuaFunction(
                "debug_addThreadToNoBreakList", "exclude thread from breakpoints", threadId));

        /// <summary>Removes a target thread from the breakpoint exclusion list.</summary>
        public static void RemoveThreadFromNoBreakList(int threadId) =>
            WrapException(() => LuaUtils.CallVoidLuaFunction(
                "debug_removeThreadFromNoBreakList", "include thread in breakpoints", threadId));

        /// <summary>Sets a breakpoint that only applies to one target thread.</summary>
        public static void SetBreakpointForThread(
            int threadId,
            ulong address,
            int size = 1,
            string trigger = "bptExecute") =>
            SetBreakpointCore("debug_setBreakpointForThread", threadId, address, size, trigger);

        /// <summary>Returns the local Cheat Engine address of an XMM register.</summary>
        public static ulong GetXmmPointer(int register) =>
            WrapException(() => LuaUtils.CallLuaFunction(
                "debug_getXMMPointer",
                "get XMM register pointer",
                () => (ulong)lua.ToInteger64(-1),
                register));

        /// <summary>Returns the current broken-thread context as a Lua table.</summary>
        public static object? GetCurrentContextTable(bool extraRegisters = false) =>
            WrapException(() => LuaUtils.CallLuaFunction(
                "debug_getCurrentContextTable",
                "get current debug context table",
                () => LuaExecutor.ReadStackValue(-1),
                extraRegisters));

        /// <summary>Enables or disables CPU last-branch recording.</summary>
        public static void SetLastBranchRecording(bool enabled) =>
            WrapException(() => LuaUtils.CallVoidLuaFunction(
                "debug_setLastBranchRecording", "set last branch recording", enabled));

        /// <summary>Returns the maximum available last-branch record count.</summary>
        public static int GetMaxLastBranchRecord() =>
            WrapException(() => LuaUtils.CallLuaFunction(
                "debug_getMaxLastBranchRecord",
                "get maximum last branch record",
                () => lua.ToInteger(-1)));

        /// <summary>Returns one last-branch record address.</summary>
        public static ulong GetLastBranchRecord(int index) =>
            WrapException(() => LuaUtils.CallLuaFunction(
                "debug_getLastBranchRecord",
                "get last branch record",
                () => (ulong)lua.ToInteger64(-1),
                index));

        // ── Debug Output ─────────────────────────────────────────────────────

        /// <summary>Outputs a message via Windows OutputDebugString (readable with DebugView).</summary>
        public static void OutputDebugString(string message) =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("outputDebugString", "output debug string", message));

        // ── Private helpers ──────────────────────────────────────────────────

        private static void SetBreakpointCore(
            string functionName,
            int? threadId,
            ulong address,
            int size,
            string trigger)
        {
            WrapException(() =>
            {
                int initialTop = lua.GetTop();
                try
                {
                    lua.GetGlobal(functionName);
                    if (!lua.IsFunction(-1))
                        throw new InvalidOperationException($"{functionName} function not available");

                    int argumentCount = 3;
                    if (threadId.HasValue)
                    {
                        lua.PushInteger(threadId.Value);
                        argumentCount++;
                    }
                    lua.PushInteger((long)address);
                    lua.PushInteger(size);
                    PushRequiredGlobal(trigger);

                    int result = lua.PCall(argumentCount, 0);
                    if (result != 0)
                        throw new InvalidOperationException($"{functionName}() failed: {lua.ToString(-1)}");
                }
                finally
                {
                    lua.SetTop(initialTop);
                }
            });
        }

        private static void PushRequiredGlobal(string name)
        {
            lua.GetGlobal(name);
            if (!lua.IsNumber(-1))
                throw new InvalidOperationException($"Cheat Engine constant '{name}' is not available");
        }

        private static void WrapException(Action operation)
        {
            try { operation(); }
            catch (InvalidOperationException ex) { throw new DebuggerException(ex.Message, ex); }
        }

        private static T WrapException<T>(Func<T> operation)
        {
            try { return operation(); }
            catch (InvalidOperationException ex) { throw new DebuggerException(ex.Message, ex); }
        }
    }
}