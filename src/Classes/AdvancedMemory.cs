using System;
using System.Collections.Generic;
using CESDK.Utils;

namespace CESDK.Classes
{
    /// <summary>Result of comparing two target-process memory ranges.</summary>
    public sealed record MemoryComparison(bool Equal, ulong? FirstDifferenceOffset);

    /// <summary>Advanced target-process memory allocation, protection, transfer, and file operations.</summary>
    public static class AdvancedMemory
    {
        private static readonly Lua.LuaNative lua = PluginContext.Lua;

        /// <summary>Allocates memory in the attached process and returns its base address.</summary>
        public static ulong Allocate(ulong size, ulong? preferredAddress = null, int? protection = null)
        {
            object?[] args = protection.HasValue
                ? [size, preferredAddress, protection.Value]
                : preferredAddress.HasValue ? [size, preferredAddress.Value] : [size];
            return RequireAddress(LuaUtils.CallLuaFunction(
                "allocateMemory", "allocate target memory", LuaUtils.ParseAddressFromStack, args));
        }

        /// <summary>Frees memory previously allocated in the attached process.</summary>
        public static void Free(ulong address, ulong? size = null)
        {
            if (size.HasValue)
                LuaUtils.CallVoidLuaFunction("deAlloc", "free target memory", address, size.Value);
            else
                LuaUtils.CallVoidLuaFunction("deAlloc", "free target memory", address);
        }

        /// <summary>Maps a named shared-memory region into the attached process.</summary>
        public static ulong AllocateShared(string name, ulong? size = null)
        {
            ulong? address = size.HasValue
                ? LuaUtils.CallLuaFunction("allocateSharedMemory", "allocate shared memory", LuaUtils.ParseAddressFromStack, name, size.Value)
                : LuaUtils.CallLuaFunction("allocateSharedMemory", "allocate shared memory", LuaUtils.ParseAddressFromStack, name);
            return RequireAddress(address);
        }

        /// <summary>Changes memory protection to readable, writable, and executable.</summary>
        public static void FullAccess(ulong address, ulong size) =>
            LuaUtils.CallVoidLuaFunction("fullAccess", "grant full memory access", address, size);

        /// <summary>Sets explicit read, write, and execute protection on a memory range.</summary>
        public static void SetProtection(ulong address, ulong size, bool read, bool write, bool execute) =>
            LuaUtils.CallVoidLuaFunction(
                "setMemoryProtection",
                "set memory protection",
                address,
                size,
                new Dictionary<string, object?>
                {
                    ["r"] = read,
                    ["w"] = write,
                    ["x"] = execute
                });

        /// <summary>Copies target memory, optionally allocating the destination.</summary>
        public static ulong Copy(ulong sourceAddress, ulong size, ulong? destinationAddress = null, int method = 0)
        {
            ulong? result = LuaUtils.CallLuaFunction(
                "copyMemory",
                "copy target memory",
                LuaUtils.ParseAddressFromStack,
                sourceAddress,
                size,
                destinationAddress,
                method);
            return RequireAddress(result);
        }

        /// <summary>Compares two target memory ranges and reports the first differing byte offset.</summary>
        public static MemoryComparison Compare(ulong address1, ulong address2, ulong size, int method = 0)
        {
            int initialTop = lua.GetTop();
            try
            {
                lua.GetGlobal("compareMemory");
                if (!lua.IsFunction(-1))
                    throw new InvalidOperationException("compareMemory function not available in this CE version");

                lua.PushInteger((long)address1);
                lua.PushInteger((long)address2);
                lua.PushInteger((long)size);
                lua.PushInteger(method);
                int result = lua.PCall(4, 2);
                if (result != 0)
                    throw new InvalidOperationException($"compareMemory() call failed: {lua.ToString(-1)}");
                if (!lua.IsBoolean(-2))
                    throw new InvalidOperationException("compareMemory did not return a comparison result");

                bool equal = lua.ToBoolean(-2);
                ulong? offset = lua.IsNil(-1)
                    ? null
                    : lua.IsNumber(-1)
                        ? (ulong)lua.ToInteger64(-1)
                        : throw new InvalidOperationException("compareMemory returned an invalid difference offset");
                return new MemoryComparison(equal, offset);
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        /// <summary>Computes the MD5 hash of a target memory range.</summary>
        public static string Md5(ulong address, ulong size) =>
            LuaUtils.CallLuaFunction(
                "md5memory",
                "hash target memory",
                () => lua.ToString(-1),
                address,
                size);

        /// <summary>Writes a target memory range to a file and returns the byte count.</summary>
        public static long DumpToFile(string filename, ulong address, ulong size) =>
            LuaUtils.CallLuaFunction(
                "writeRegionToFile",
                "write memory region to file",
                () => lua.ToInteger64(-1),
                filename,
                address,
                size);

        /// <summary>Loads file bytes into an existing target memory range.</summary>
        public static void LoadFromFile(string filename, ulong destinationAddress) =>
            LuaUtils.CallVoidLuaFunction(
                "readRegionFromFile",
                "read file into memory region",
                filename,
                destinationAddress);

        private static ulong RequireAddress(ulong? address) =>
            address ?? throw new InvalidOperationException("Cheat Engine did not return an address");
    }
}
