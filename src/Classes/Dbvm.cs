using System;
using CESDK.Utils;

namespace CESDK.Classes
{
    /// <summary>Optional DBVM physical-memory and hardware watch operations.</summary>
    public static class Dbvm
    {
        private static readonly Lua.LuaNative lua = PluginContext.Lua;

        /// <summary>Returns whether DBVM is loaded and usable.</summary>
        public static bool IsAvailable() => LuaUtils.CallLuaFunction(
            "dbvm_initialized",
            "check DBVM availability",
            () => lua.ToBoolean(-1));

        /// <summary>Initializes DBVM without offloading the operating system unless explicitly requested.</summary>
        public static bool Initialize(bool offloadOperatingSystem = false, string? reason = null) =>
            LuaUtils.CallLuaFunction(
                "dbvm_initialize",
                "initialize DBVM",
                () => lua.ToBoolean(-1),
                offloadOperatingSystem,
                reason ?? string.Empty);

        /// <summary>Reads physical memory through DBVM.</summary>
        public static byte[] ReadPhysical(ulong address, int size)
        {
            EnsureAvailable();
            return LuaUtils.CallLuaFunction(
                "dbvm_readPhysicalMemory",
                "read physical memory",
                LuaUtils.ExtractBytesFromTable,
                address,
                size);
        }

        /// <summary>Writes physical memory through DBVM.</summary>
        public static void WritePhysical(ulong address, byte[] bytes)
        {
            EnsureAvailable();
            LuaUtils.CallVoidLuaFunction(
                "dbvm_writePhysicalMemory",
                "write physical memory",
                address,
                bytes);
        }

        /// <summary>Starts a DBVM physical-memory watch and returns its identifier.</summary>
        public static long StartWatch(
            string access,
            ulong physicalAddress,
            int byteSize = 1,
            int options = 0,
            int internalEntryCount = 8192)
        {
            EnsureAvailable();
            string function = access.ToLowerInvariant() switch
            {
                "write" => "dbvm_watch_writes",
                "read" => "dbvm_watch_reads",
                "execute" => "dbvm_watch_executes",
                _ => throw new ArgumentException("Access must be 'read', 'write', or 'execute'", nameof(access))
            };

            return LuaUtils.CallLuaFunction(
                function,
                $"start DBVM {access} watch",
                () => lua.ToInteger64(-1),
                physicalAddress,
                byteSize,
                options,
                internalEntryCount);
        }

        /// <summary>Returns the bounded event log for a DBVM watch.</summary>
        public static object? GetWatchLog(long id)
        {
            EnsureAvailable();
            return LuaUtils.CallLuaFunction(
                "dbvm_watch_retrievelog",
                "retrieve DBVM watch log",
                () => LuaExecutor.ReadStackValue(-1),
                id);
        }

        /// <summary>Stops a DBVM watch.</summary>
        public static void StopWatch(long id)
        {
            EnsureAvailable();
            LuaUtils.CallVoidLuaFunction("dbvm_watch_disable", "disable DBVM watch", id);
        }

        private static void EnsureAvailable()
        {
            if (!IsAvailable())
                throw new InvalidOperationException("DBVM is not initialized or available in this Cheat Engine session");
        }
    }
}
