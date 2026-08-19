using System;
using System.Collections.Generic;
using CESDK.Utils;
using CESDK.Lua;

namespace CESDK.Classes
{
    public class AobScanException : CesdkException
    {
        public AobScanException(string message) : base(message) { }
        public AobScanException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static class AobScanner
    {
        public static List<ulong> Scan(string pattern, string? protectionFlags = null, int alignmentType = 0, string? alignmentParam = null) =>
            WrapException(() => LuaUtils.CallLuaFunction(
                "AOBScan",
                "perform AOB scan",
                ProcessScanResults,
                pattern,
                protectionFlags ?? string.Empty,
                alignmentType,
                alignmentParam ?? string.Empty));

        public static ulong? ScanUnique(string pattern, string? protectionFlags = null, int alignmentType = 0, string? alignmentParam = null) =>
            WrapException(() => LuaUtils.CallLuaFunction(
                "AOBScanUnique",
                "perform unique AOB scan",
                LuaUtils.ParseAddressFromStack,
                pattern,
                protectionFlags ?? string.Empty,
                alignmentType,
                alignmentParam ?? string.Empty));

        public static ulong? ScanModuleUnique(string moduleName, string pattern, string? protectionFlags = null, int alignmentType = 0, string? alignmentParam = null) =>
            WrapException(() => LuaUtils.CallLuaFunction("AOBScanModuleUnique", "perform module unique AOB scan", LuaUtils.ParseAddressFromStack, moduleName, pattern, protectionFlags ?? string.Empty, alignmentType, alignmentParam ?? string.Empty));

        private static List<ulong> ProcessScanResults()
        {
            var addresses = new List<ulong>();
            var lua = PluginContext.Lua;

            if (lua.IsNil(-1))
                return addresses;
            if (!lua.IsCEObject(-1))
                throw new InvalidOperationException("AOBScan returned an invalid result list");

            int resultTop = lua.GetTop();
            try
            {
                lua.GetField(-1, "Count");
                if (!lua.IsNumber(-1))
                    throw new InvalidOperationException("AOBScan result count is unavailable");

                int count = lua.ToInteger(-1);
                lua.Pop(1);
                for (int index = 0; index < count; index++)
                    ProcessSingleResult(addresses, index, lua);
                return addresses;
            }
            finally
            {
                lua.SetTop(resultTop);
                DestroyScanResults(lua);
            }
        }

        private static void ProcessSingleResult(List<ulong> addresses, int index, LuaNative lua)
        {
            lua.PushInteger(index);
            lua.GetTable(-2);
            try
            {
                if (!lua.IsString(-1))
                    throw new InvalidOperationException($"AOBScan returned no address at index {index}");

                string addressText = lua.ToString(-1) ?? "";
                if (!ulong.TryParse(
                        addressText,
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out ulong address))
                    throw new InvalidOperationException($"AOBScan returned invalid address '{addressText}'");
                addresses.Add(address);
            }
            finally
            {
                lua.Pop(1);
            }
        }

        private static void DestroyScanResults(LuaNative lua)
        {
            lua.GetField(-1, "destroy");
            if (!lua.IsFunction(-1))
                throw new InvalidOperationException("AOBScan result list cannot be destroyed");
            int result = lua.PCall(0, 0);
            if (result != 0)
                throw new InvalidOperationException($"AOBScan result cleanup failed: {lua.ToString(-1)}");
        }

        private static T WrapException<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (InvalidOperationException ex)
            {
                throw new AobScanException(ex.Message, ex);
            }
        }
    }
}