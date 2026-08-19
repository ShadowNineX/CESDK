using System;
using System.Collections;
using System.Collections.Generic;
using CESDK.Lua;

namespace CESDK.Utils
{
    public static class LuaUtils
    {
        private static readonly LuaNative lua = PluginContext.Lua;

        /// <summary>
        /// Generic method to call Lua functions with automatic parameter handling and error management
        /// </summary>
        public static T CallLuaFunction<T>(
            string functionName,
            string operationName,
            Func<T> valueExtractor,
            params object?[] args) =>
            CallLuaFunctionCore(functionName, operationName, valueExtractor, 1, args);


        /// <summary>
        /// Helper for void functions that don't return values
        /// </summary>
        public static void CallVoidLuaFunction(
            string functionName,
            string operationName,
            params object?[] args) =>
            CallLuaFunctionCore(functionName, operationName, VoidExtractor, 0, args);


        /// <summary>
        /// Extracts byte array from Lua table
        /// </summary>
        public static byte[] ExtractBytesFromTable()
        {
            if (!lua.IsTable(-1))
                throw new InvalidOperationException("Expected a byte table");

            int length = lua.RawLen(-1);
            var bytes = new byte[length];
            for (int index = 1; index <= length; index++)
            {
                lua.PushInteger(index);
                lua.GetTable(-2);
                try
                {
                    if (!lua.IsInteger(-1))
                        throw new InvalidOperationException($"Byte table item {index} is not an integer");

                    long value = lua.ToInteger64(-1);
                    if (value is < byte.MinValue or > byte.MaxValue)
                        throw new InvalidOperationException($"Byte table item {index} is outside the byte range");

                    bytes[index - 1] = (byte)value;
                }
                finally
                {
                    lua.Pop(1);
                }
            }
            return bytes;
        }

        /// <summary>
        /// Parses address from Lua stack (handles both number and string formats)
        /// </summary>
        public static ulong? ParseAddressFromStack()
        {
            ulong? address = null;

            if (lua.IsNumber(-1))
            {
                address = (ulong)lua.ToInteger64(-1);
            }
            else if (lua.IsString(-1))
            {
                var addressStr = lua.ToString(-1);
                if (ulong.TryParse(addressStr, System.Globalization.NumberStyles.HexNumber, null, out var parsedAddress))
                {
                    address = parsedAddress;
                }
            }

            return address;
        }

        private static T CallLuaFunctionCore<T>(
            string functionName,
            string operationName,
            Func<T> valueExtractor,
            int expectedReturnValues,
            object?[] args)
        {
            int initialTop = lua.GetTop();
            try
            {
                lua.GetGlobal(functionName);
                if (!lua.IsFunction(-1))
                    throw new InvalidOperationException($"{functionName} function not available in this CE version");

                PushArguments(args);

                int result = lua.PCall(args.Length, expectedReturnValues);
                if (result != 0)
                {
                    string error = lua.ToString(-1);
                    throw new InvalidOperationException($"{functionName}() call failed: {error}");
                }

                return valueExtractor();
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException($"Failed to {operationName}", ex);
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        private static void PushArguments(object?[] args)
        {
            foreach (object? arg in args)
                PushArgument(arg);
        }

        private static void PushArgument(object? arg)
        {
            switch (arg)
            {
                case null:
                    lua.PushNil();
                    break;
                case long value:
                    lua.PushInteger(value);
                    break;
                case int value:
                    lua.PushInteger(value);
                    break;
                case ulong value:
                    lua.PushInteger((long)value);
                    break;
                case uint value:
                    lua.PushInteger(value);
                    break;
                case short value:
                    lua.PushInteger(value);
                    break;
                case ushort value:
                    lua.PushInteger(value);
                    break;
                case byte value:
                    lua.PushInteger(value);
                    break;
                case sbyte value:
                    lua.PushInteger(value);
                    break;
                case float value:
                    lua.PushNumber(value);
                    break;
                case double value:
                    lua.PushNumber(value);
                    break;
                case decimal value:
                    lua.PushNumber((double)value);
                    break;
                case string value:
                    lua.PushString(value);
                    break;
                case char value:
                    lua.PushString(value.ToString());
                    break;
                case bool value:
                    lua.PushBoolean(value);
                    break;
                case Enum value:
                    lua.PushInteger(Convert.ToInt64(value));
                    break;
                case IntPtr value:
                    lua.PushInteger(value.ToInt64());
                    break;
                case byte[] bytes:
                    PushByteTable(bytes);
                    break;
                case IReadOnlyDictionary<string, object?> dictionary:
                    lua.CreateTable();
                    foreach (KeyValuePair<string, object?> entry in dictionary)
                    {
                        PushArgument(entry.Value);
                        lua.SetField(-2, entry.Key);
                    }
                    break;
                case IDictionary dictionary:
                    lua.CreateTable();
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        PushArgument(entry.Key);
                        PushArgument(entry.Value);
                        lua.SetTable(-3);
                    }
                    break;
                case IEnumerable values:
                    lua.CreateTable();
                    int index = 1;
                    foreach (object? value in values)
                    {
                        lua.PushInteger(index++);
                        PushArgument(value);
                        lua.SetTable(-3);
                    }
                    break;
                default:
                    throw new ArgumentException($"Unsupported argument type: {arg.GetType()}");
            }
        }

        private static void PushByteTable(byte[] bytes)
        {
            lua.CreateTable();
            for (int i = 0; i < bytes.Length; i++)
            {
                lua.PushInteger(i + 1);
                lua.PushInteger(bytes[i]);
                lua.SetTable(-3);
            }
        }


        private static object VoidExtractor() => null!;
    }
}