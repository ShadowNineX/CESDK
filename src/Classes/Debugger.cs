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

    /// <summary>
    /// Debugger-specific operations: attach, detach, breakpoints, register access, stepping.
    /// For disassembly, use <see cref="Disassembler"/>.
    /// </summary>
    public static class Debugger
    {
        private static readonly LuaNative lua = PluginContext.Lua;

        // ── Process / Attach ─────────────────────────────────────────────────

        /// <summary>
        /// Starts the debugger for the currently opened process.
        /// debugInterface: 0=default, 1=windows, 2=VEH, 3=kernel.
        /// </summary>
        public static void DebugProcess(int debugInterface = 0) =>
            WrapException(() => LuaUtils.CallVoidLuaFunctionWithOptionalParams(
                "debugProcess", "start debugging process",
                debugInterface == 0 ? null : debugInterface));

        /// <summary>Detaches the debugger if possible.</summary>
        public static void DetachIfPossible() =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("detachIfPossible", "detach debugger"));

        /// <summary>Pauses the currently opened process.</summary>
        public static void Pause() =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("pause", "pause process"));

        /// <summary>Resumes the currently opened process.</summary>
        public static void Unpause() =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("unpause", "unpause process"));

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
        /// Returns the current debugger interface: 1=windows, 2=VEH, 3=kernel, null=none.
        /// </summary>
        public static int? GetCurrentDebuggerInterface() =>
            WrapException(() =>
            {
                lua.GetGlobal("debug_getCurrentDebuggerInterface");
                if (!lua.IsFunction(-1)) { lua.Pop(1); return (int?)null; }
                lua.PCall(0, 1);
                var result = lua.IsNil(-1) ? (int?)null : (int?)lua.ToInteger(-1);
                lua.Pop(1);
                return result;
            });

        // ── Breakpoints ──────────────────────────────────────────────────────

        /// <summary>Returns a list of all active breakpoint addresses.</summary>
        public static List<ulong> GetBreakpointList() =>
            WrapException(() =>
            {
                lua.GetGlobal("debug_getBreakpointList");
                if (!lua.IsFunction(-1)) { lua.Pop(1); return []; }
                lua.PCall(0, 1);
                var list = new List<ulong>();
                if (lua.IsTable(-1))
                {
                    lua.PushNil();
                    while (lua.Next(-2) != 0)
                    {
                        list.Add((ulong)lua.ToInteger(-1));
                        lua.Pop(1);
                    }
                }
                lua.Pop(1);
                return list;
            });

        /// <summary>
        /// Sets a breakpoint at the given address without a callback (breaking breakpoint).
        /// trigger: "bptExecute" (default), "bptWrite", or "bptAccess".
        /// For bptExecute, size is ignored. For bptWrite/bptAccess, size is the watch size in bytes.
        /// </summary>
        public static void SetBreakpoint(ulong address, int size = 1, string trigger = "bptExecute") =>
            WrapException(() =>
            {
                lua.GetGlobal("debug_setBreakpoint");
                if (!lua.IsFunction(-1)) { lua.Pop(1); throw new InvalidOperationException("debug_setBreakpoint not available"); }
                lua.PushInteger((long)address);
                lua.PushInteger(size);
                lua.GetGlobal(trigger);   // push the CE constant (bptExecute / bptWrite / bptAccess)
                lua.PCall(3, 0);
            });

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
                lua.GetGlobal("debug_continueFromBreakpoint");
                if (!lua.IsFunction(-1)) { lua.Pop(1); throw new InvalidOperationException("debug_continueFromBreakpoint not available"); }
                lua.GetGlobal(method);    // push CE constant co_run / co_stepinto / co_stepover
                lua.PCall(1, 0);
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
                lua.GetGlobal(name);
                var val = (ulong)lua.ToInteger(-1);
                lua.Pop(1);
                return val;
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

        // ── Debug Output ─────────────────────────────────────────────────────

        /// <summary>Outputs a message via Windows OutputDebugString (readable with DebugView).</summary>
        public static void OutputDebugString(string message) =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("outputDebugString", "output debug string", message));

        // ── Private helpers ──────────────────────────────────────────────────

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