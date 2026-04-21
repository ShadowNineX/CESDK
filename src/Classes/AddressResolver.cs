using System;
using System.Globalization;
using CESDK.Lua;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class AddressResolutionException : CesdkException
    {
        public AddressResolutionException(string message) : base(message) { }
        public AddressResolutionException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static class AddressResolver
    {
        public static ulong GetAddress(string symbolName, bool searchLocal = false) =>
            WrapException(() =>
            {
                var args = searchLocal ? new object[] { symbolName, true } : new object[] { symbolName };
                return LuaUtils.CallLuaFunction("getAddress", $"resolve address for '{symbolName}'", ParseRequiredAddress, args);
            });

        private static ulong ParseRequiredAddress()
        {
            var lua = PluginContext.Lua;

            if (lua.IsNumber(-1))
            {
                return (ulong)lua.ToNumber(-1);
            }
            else if (lua.IsString(-1))
            {
                var addressStr = lua.ToString(-1);
                if (ulong.TryParse(addressStr, NumberStyles.HexNumber, null, out var hexAddress))
                {
                    return hexAddress;
                }
                else if (ulong.TryParse(addressStr, out var decAddress))
                {
                    return decAddress;
                }
                throw new InvalidOperationException($"Invalid address format returned: {addressStr}");
            }
            else
            {
                throw new InvalidOperationException("Symbol not found or address could not be resolved");
            }
        }

        public static ulong? GetAddressSafe(string symbolName, bool searchLocal = false) =>
            WrapException(() =>
            {
                var args = searchLocal ? new object[] { symbolName, true } : new object[] { symbolName };
                return LuaUtils.CallLuaFunction("getAddressSafe", $"safely resolve address for '{symbolName}'", ParseOptionalAddress, args);
            });

        private static ulong? ParseOptionalAddress()
        {
            var lua = PluginContext.Lua;

            if (lua.IsNumber(-1))
            {
                return (ulong)lua.ToNumber(-1);
            }
            else if (lua.IsString(-1))
            {
                var addressStr = lua.ToString(-1);
                if (ulong.TryParse(addressStr, NumberStyles.HexNumber, null, out var hexAddress))
                {
                    return hexAddress;
                }
                else if (ulong.TryParse(addressStr, out var decAddress))
                {
                    return decAddress;
                }
            }
            return null;
        }

        public static string GetNameFromAddress(ulong address, bool moduleNames = true, bool symbols = true, bool sections = false) =>
            WrapException(() => LuaUtils.CallLuaFunction("getNameFromAddress", $"get name from address 0x{address:X}", () => PluginContext.Lua.ToString(-1) ?? "", (long)address, moduleNames, symbols, sections));

        public static bool InModule(ulong address) =>
            WrapException(() => LuaUtils.CallLuaFunction("inModule", $"check if address 0x{address:X} is in module", () => PluginContext.Lua.ToBoolean(-1), (long)address));

        public static bool InSystemModule(ulong address) =>
            WrapException(() => LuaUtils.CallLuaFunction("inSystemModule", $"check if address 0x{address:X} is in system module", () => PluginContext.Lua.ToBoolean(-1), (long)address));

        private static T WrapException<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (InvalidOperationException ex)
            {
                throw new AddressResolutionException(ex.Message, ex);
            }
        }
    }
}