using System;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class MemoryAccessException : CesdkException
    {
        public MemoryAccessException(string message) : base(message) { }
        public MemoryAccessException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static class MemoryAccess
    {
        // Read methods for target process
        public static byte ReadByte(ulong address) =>
            ReadRequired("readByte", $"read byte at 0x{address:X}", () => (byte)PluginContext.Lua.ToInteger(-1), address);

        public static short ReadSmallInteger(ulong address) =>
            ReadRequired("readSmallInteger", $"read small integer at 0x{address:X}", () => (short)PluginContext.Lua.ToInteger(-1), address);

        public static int ReadInteger(ulong address) =>
            ReadRequired("readInteger", $"read integer at 0x{address:X}", () => PluginContext.Lua.ToInteger(-1), address);

        public static long ReadQword(ulong address) =>
            ReadRequired("readQword", $"read qword at 0x{address:X}", () => PluginContext.Lua.ToInteger64(-1), address);

        public static ulong ReadPointer(ulong address) =>
            ReadRequired("readPointer", $"read pointer at 0x{address:X}", () => (ulong)PluginContext.Lua.ToInteger64(-1), address);

        public static float ReadFloat(ulong address) =>
            ReadRequired("readFloat", $"read float at 0x{address:X}", () => (float)PluginContext.Lua.ToNumber(-1), address);

        public static double ReadDouble(ulong address) =>
            ReadRequired("readDouble", $"read double at 0x{address:X}", () => PluginContext.Lua.ToNumber(-1), address);

        public static string ReadString(ulong address, int maxLength = 1000, bool isWideChar = false) =>
            ReadRequired("readString", $"read string at 0x{address:X}", () => PluginContext.Lua.ToString(-1)!, address, maxLength, isWideChar);

        public static byte[] ReadBytes(ulong address, int count) =>
            ReadRequired("readBytes", $"read {count} bytes at 0x{address:X}", LuaUtils.ExtractBytesFromTable, address, count, true);

        // Write methods for target process
        public static bool WriteByte(ulong address, byte value) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeByte", $"write byte at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, value));

        public static bool WriteSmallInteger(ulong address, short value) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeSmallInteger", $"write small integer at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, value));

        public static bool WriteInteger(ulong address, int value) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeInteger", $"write integer at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, value));

        public static bool WriteQword(ulong address, long value) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeQword", $"write qword at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, value));

        public static bool WriteFloat(ulong address, float value) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeFloat", $"write float at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, value));

        public static bool WriteDouble(ulong address, double value) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeDouble", $"write double at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, value));

        public static bool WriteString(ulong address, string value, bool isWideChar = false) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeString", $"write string at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, value, isWideChar));

        public static bool WriteBytes(ulong address, byte[] bytes) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeBytes", $"write {bytes.Length} bytes at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, bytes));

        // Local CE memory read methods
        public static short ReadSmallIntegerLocal(ulong address) =>
            ReadRequired("readSmallIntegerLocal", $"read local small integer at 0x{address:X}", () => (short)PluginContext.Lua.ToInteger(-1), address);

        public static int ReadIntegerLocal(ulong address) =>
            ReadRequired("readIntegerLocal", $"read local integer at 0x{address:X}", () => PluginContext.Lua.ToInteger(-1), address);

        public static long ReadQwordLocal(ulong address) =>
            ReadRequired("readQwordLocal", $"read local qword at 0x{address:X}", () => PluginContext.Lua.ToInteger64(-1), address);

        public static ulong ReadPointerLocal(ulong address) =>
            ReadRequired("readPointerLocal", $"read local pointer at 0x{address:X}", () => (ulong)PluginContext.Lua.ToInteger64(-1), address);

        public static float ReadFloatLocal(ulong address) =>
            ReadRequired("readFloatLocal", $"read local float at 0x{address:X}", () => (float)PluginContext.Lua.ToNumber(-1), address);

        public static double ReadDoubleLocal(ulong address) =>
            ReadRequired("readDoubleLocal", $"read local double at 0x{address:X}", () => PluginContext.Lua.ToNumber(-1), address);

        public static string ReadStringLocal(ulong address, int maxLength = 1000, bool isWideChar = false) =>
            ReadRequired("readStringLocal", $"read local string at 0x{address:X}", () => PluginContext.Lua.ToString(-1)!, address, maxLength, isWideChar);

        public static byte[] ReadBytesLocal(ulong address, int count) =>
            ReadRequired("readBytesLocal", $"read local {count} bytes at 0x{address:X}", LuaUtils.ExtractBytesFromTable, address, count, true);

        // Local CE memory write methods
        public static bool WriteSmallIntegerLocal(ulong address, short value) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeSmallIntegerLocal", $"write local small integer at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, value));

        public static bool WriteIntegerLocal(ulong address, int value) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeIntegerLocal", $"write local integer at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, value));

        public static bool WriteQwordLocal(ulong address, long value) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeQwordLocal", $"write local qword at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, value));

        public static bool WriteFloatLocal(ulong address, float value) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeFloatLocal", $"write local float at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, value));

        public static bool WriteDoubleLocal(ulong address, double value) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeDoubleLocal", $"write local double at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, value));

        public static bool WriteStringLocal(ulong address, string value, bool isWideChar = false) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeStringLocal", $"write local string at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, value, isWideChar));

        public static bool WriteBytesLocal(ulong address, byte[] bytes) =>
            WrapException(() => LuaUtils.CallLuaFunction("writeBytesLocal", $"write local {bytes.Length} bytes at 0x{address:X}", () => PluginContext.Lua.ToBoolean(-1), address, bytes));

        private static T ReadRequired<T>(string functionName, string operationName, Func<T> extractor, params object?[] args) =>
            WrapException(() => LuaUtils.CallLuaFunction(
                functionName,
                operationName,
                () =>
                {
                    if (PluginContext.Lua.IsNil(-1))
                        throw new InvalidOperationException($"{functionName} returned nil");
                    return extractor();
                },
                args));

        private static T WrapException<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (InvalidOperationException ex)
            {
                throw new MemoryAccessException(ex.Message, ex);
            }
        }
    }
}