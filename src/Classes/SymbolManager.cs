using System;
using System.Collections.Generic;
using CESDK.Lua;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class SymbolManagerException : CesdkException
    {
        public SymbolManagerException(string message) : base(message) { }
        public SymbolManagerException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class ModuleInfo
    {
        public string Name { get; init; } = "";
        public ulong Address { get; init; }
        public ulong Size { get; init; }
        public bool Is64Bit { get; init; }
        public string PathToFile { get; init; } = "";
    }

    public class SymbolInfo
    {
        public string ModuleName { get; init; } = "";
        public string SearchKey { get; init; } = "";
        public ulong Address { get; init; }
        public ulong Size { get; init; }
    }

    public static class SymbolManager
    {
        private static readonly LuaNative lua = PluginContext.Lua;

        /// <summary>
        /// Enumerates all loaded modules in the target process.
        /// </summary>
        public static List<ModuleInfo> EnumModules()
        {
            return WrapException(() =>
            {
                int initialTop = lua.GetTop();
                try
                {
                    lua.GetGlobal("enumModules");
                    if (!lua.IsFunction(-1))
                        throw new InvalidOperationException("enumModules function not available");

                    int result = lua.PCall(0, 1);
                    if (result != 0)
                        throw new InvalidOperationException($"enumModules() failed: {lua.ToString(-1)}");
                    if (!lua.IsTable(-1))
                        throw new InvalidOperationException("enumModules did not return a table");

                    var modules = new List<ModuleInfo>();
                    lua.PushNil();
                    while (lua.Next(-2) != 0)
                    {
                        try
                        {
                            if (!lua.IsTable(-1))
                                throw new InvalidOperationException("enumModules returned an invalid entry");

                            modules.Add(new ModuleInfo
                            {
                                Name = GetTableString(-1, "Name"),
                                Address = GetTableUlong(-1, "Address"),
                                Size = GetTableUlong(-1, "Size"),
                                Is64Bit = GetTableBool(-1, "Is64Bit"),
                                PathToFile = GetTableString(-1, "PathToFile")
                            });
                        }
                        finally
                        {
                            lua.Pop(1);
                        }
                    }
                    return modules;
                }
                finally
                {
                    lua.SetTop(initialTop);
                }
            });
        }

        /// <summary>
        /// Returns the size of a given module.
        /// </summary>
        public static ulong GetModuleSize(string moduleName) =>
            WrapException(() => LuaUtils.CallLuaFunction("getModuleSize", $"get module size for '{moduleName}'",
                () =>
                {
                    if (!lua.IsNumber(-1))
                        throw new InvalidOperationException($"Module '{moduleName}' was not found");
                    return (ulong)lua.ToInteger64(-1);
                }, moduleName));

        /// <summary>
        /// Returns symbol info for a given symbol name.
        /// </summary>
        public static SymbolInfo? GetSymbolInfo(string symbolName)
        {
            return WrapException(() =>
            {
                int initialTop = lua.GetTop();
                try
                {
                    lua.GetGlobal("getSymbolInfo");
                    if (!lua.IsFunction(-1))
                        throw new InvalidOperationException("getSymbolInfo function not available");

                    lua.PushString(symbolName);
                    int result = lua.PCall(1, 1);
                    if (result != 0)
                        throw new InvalidOperationException($"getSymbolInfo() failed: {lua.ToString(-1)}");
                    if (lua.IsNil(-1))
                        return null;
                    if (!lua.IsTable(-1))
                        throw new InvalidOperationException("getSymbolInfo returned an invalid value");

                    return new SymbolInfo
                    {
                        ModuleName = GetTableString(-1, "modulename"),
                        SearchKey = GetTableString(-1, "searchkey"),
                        Address = GetTableUlong(-1, "address"),
                        Size = GetTableUlong(-1, "size")
                    };
                }
                finally
                {
                    lua.SetTop(initialTop);
                }
            });
        }

        /// <summary>
        /// Downloads Windows PDB files and loads them. Takes a long time first run.
        /// </summary>
        public static void EnableWindowsSymbols() =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("enableWindowsSymbols", "enable Windows symbols"));

        /// <summary>
        /// Enables kernel mode symbols in the memory view.
        /// </summary>
        public static void EnableKernelSymbols() =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("enableKernelSymbols", "enable kernel symbols"));

        /// <summary>
        /// Reinitializes the symbol handler (e.g. when new modules have been loaded).
        /// </summary>
        public static void ReinitializeSymbolHandler(bool waitTillDone = true) =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("reinitializeSymbolhandler", "reinitialize symbol handler", waitTillDone));

        /// <summary>
        /// Returns true when all symbols have finished loading.
        /// </summary>
        public static bool SymbolsDoneLoading() =>
            WrapException(() => LuaUtils.CallLuaFunction("symbolsDoneLoading", "check if symbols done loading",
                () => lua.ToBoolean(-1)));

        /// <summary>
        /// Gets the pointer size CE is using (in bytes).
        /// </summary>
        public static int GetPointerSize() =>
            WrapException(() => LuaUtils.CallLuaFunction("getPointerSize", "get pointer size",
                () => lua.ToInteger(-1)));

        /// <summary>
        /// Sets the pointer size CE should use (in bytes). Some 64-bit processes only use 32-bit addresses.
        /// </summary>
        public static void SetPointerSize(int size) =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("setPointerSize", $"set pointer size to {size}", size));

        private static string GetTableString(int tableIndex, string key)
        {
            lua.GetField(tableIndex, key);
            var value = lua.IsString(-1) ? lua.ToString(-1) ?? "" : "";
            lua.Pop(1);
            return value;
        }

        private static ulong GetTableUlong(int tableIndex, string key)
        {
            lua.GetField(tableIndex, key);
            try
            {
                return lua.IsNumber(-1) ? (ulong)lua.ToInteger64(-1) : 0UL;
            }
            finally
            {
                lua.Pop(1);
            }
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
            catch (InvalidOperationException ex) { throw new SymbolManagerException(ex.Message, ex); }
        }

        private static void WrapException(Action operation)
        {
            try { operation(); }
            catch (InvalidOperationException ex) { throw new SymbolManagerException(ex.Message, ex); }
        }
    }
}
