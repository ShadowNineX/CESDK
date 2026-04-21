using System;
using System.Collections.Generic;
using CESDK.Lua;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class MemoryRegionException : CesdkException
    {
        public MemoryRegionException(string message) : base(message) { }
        public MemoryRegionException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class MemoryRegion
    {
        public ulong BaseAddress { get; init; }
        public ulong AllocationBase { get; init; }
        public int AllocationProtect { get; init; }
        public ulong RegionSize { get; init; }
        public int State { get; init; }
        public int Protect { get; init; }
        public int Type { get; init; }
    }

    public class MemoryProtection
    {
        public bool Read { get; init; }
        public bool Write { get; init; }
        public bool Execute { get; init; }
    }

    public static class MemoryRegions
    {
        private static readonly LuaNative lua = PluginContext.Lua;

        /// <summary>
        /// Enumerates all memory regions of the target process.
        /// </summary>
        public static List<MemoryRegion> EnumMemoryRegions()
        {
            return WrapException(() =>
            {
                lua.GetGlobal("enumMemoryRegions");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(1);
                    throw new InvalidOperationException("enumMemoryRegions function not available");
                }

                var result = lua.PCall(0, 1);
                if (result != 0)
                {
                    var error = lua.ToString(-1);
                    lua.Pop(1);
                    throw new InvalidOperationException($"enumMemoryRegions() failed: {error}");
                }

                var regions = new List<MemoryRegion>();
                if (!lua.IsTable(-1))
                {
                    lua.Pop(1);
                    return regions;
                }

                lua.PushNil();
                while (lua.Next(-2) != 0)
                {
                    if (lua.IsTable(-1))
                    {
                        var region = new MemoryRegion
                        {
                            BaseAddress = GetTableUlong(-1, "BaseAddress"),
                            AllocationBase = GetTableUlong(-1, "AllocationBase"),
                            AllocationProtect = GetTableInt(-1, "AllocationProtect"),
                            RegionSize = GetTableUlong(-1, "RegionSize"),
                            State = GetTableInt(-1, "State"),
                            Protect = GetTableInt(-1, "Protect"),
                            Type = GetTableInt(-1, "Type")
                        };
                        regions.Add(region);
                    }
                    lua.Pop(1);
                }

                lua.Pop(1);
                return regions;
            });
        }

        /// <summary>
        /// Gets the memory protection flags for an address.
        /// </summary>
        public static MemoryProtection GetMemoryProtection(ulong address)
        {
            return WrapException(() =>
            {
                lua.GetGlobal("getMemoryProtection");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(1);
                    throw new InvalidOperationException("getMemoryProtection function not available");
                }

                lua.PushInteger((long)address);
                var result = lua.PCall(1, 1);
                if (result != 0)
                {
                    var error = lua.ToString(-1);
                    lua.Pop(1);
                    throw new InvalidOperationException($"getMemoryProtection() failed: {error}");
                }

                var prot = new MemoryProtection
                {
                    Read = GetTableBool(-1, "r"),
                    Write = GetTableBool(-1, "w"),
                    Execute = GetTableBool(-1, "x")
                };
                lua.Pop(1);
                return prot;
            });
        }

        /// <summary>
        /// Changes memory protection to writable and executable.
        /// </summary>
        public static void FullAccess(ulong address, int size) =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("fullAccess", $"set full access at 0x{address:X}", address, size));

        private static ulong GetTableUlong(int tableIndex, string key)
        {
            lua.GetField(tableIndex, key);
            var value = lua.IsNumber(-1) ? (ulong)lua.ToInteger(-1) : 0UL;
            lua.Pop(1);
            return value;
        }

        private static int GetTableInt(int tableIndex, string key)
        {
            lua.GetField(tableIndex, key);
            var value = lua.IsNumber(-1) ? lua.ToInteger(-1) : 0;
            lua.Pop(1);
            return value;
        }

        private static bool GetTableBool(int tableIndex, string key)
        {
            lua.GetField(tableIndex, key);
            var value = lua.ToBoolean(-1);
            lua.Pop(1);
            return value;
        }

        private static T WrapException<T>(Func<T> operation)
        {
            try { return operation(); }
            catch (InvalidOperationException ex) { throw new MemoryRegionException(ex.Message, ex); }
        }

        private static void WrapException(Action operation)
        {
            try { operation(); }
            catch (InvalidOperationException ex) { throw new MemoryRegionException(ex.Message, ex); }
        }
    }
}
