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
                int initialTop = lua.GetTop();
                try
                {
                    lua.GetGlobal("enumMemoryRegions");
                    if (!lua.IsFunction(-1))
                        throw new InvalidOperationException("enumMemoryRegions function not available");

                    int result = lua.PCall(0, 1);
                    if (result != 0)
                        throw new InvalidOperationException($"enumMemoryRegions() failed: {lua.ToString(-1)}");
                    if (!lua.IsTable(-1))
                        throw new InvalidOperationException("enumMemoryRegions did not return a table");

                    var regions = new List<MemoryRegion>();
                    lua.PushNil();
                    while (lua.Next(-2) != 0)
                    {
                        try
                        {
                            if (!lua.IsTable(-1))
                                throw new InvalidOperationException("enumMemoryRegions returned an invalid entry");

                            regions.Add(new MemoryRegion
                            {
                                BaseAddress = GetTableUlong(-1, "BaseAddress"),
                                AllocationBase = GetTableUlong(-1, "AllocationBase"),
                                AllocationProtect = GetTableInt(-1, "AllocationProtect"),
                                RegionSize = GetTableUlong(-1, "RegionSize"),
                                State = GetTableInt(-1, "State"),
                                Protect = GetTableInt(-1, "Protect"),
                                Type = GetTableInt(-1, "Type")
                            });
                        }
                        finally
                        {
                            lua.Pop(1);
                        }
                    }
                    return regions;
                }
                finally
                {
                    lua.SetTop(initialTop);
                }
            });
        }

        /// <summary>
        /// Gets the memory protection flags for an address.
        /// </summary>
        public static MemoryProtection GetMemoryProtection(ulong address)
        {
            return WrapException(() =>
            {
                int initialTop = lua.GetTop();
                try
                {
                    lua.GetGlobal("getMemoryProtection");
                    if (!lua.IsFunction(-1))
                        throw new InvalidOperationException("getMemoryProtection function not available");

                    lua.PushInteger((long)address);
                    int result = lua.PCall(1, 1);
                    if (result != 0)
                        throw new InvalidOperationException($"getMemoryProtection() failed: {lua.ToString(-1)}");
                    if (!lua.IsTable(-1))
                        throw new InvalidOperationException($"No memory protection is available at 0x{address:X}");

                    return new MemoryProtection
                    {
                        Read = GetTableBool(-1, "r"),
                        Write = GetTableBool(-1, "w"),
                        Execute = GetTableBool(-1, "x")
                    };
                }
                finally
                {
                    lua.SetTop(initialTop);
                }
            });
        }

        private static ulong GetTableUlong(int tableIndex, string key)
        {
            lua.GetField(tableIndex, key);
            var value = lua.IsNumber(-1) ? (ulong)lua.ToInteger64(-1) : 0UL;
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
