using System;
using System.Collections.Generic;
using CESDK.Utils;

namespace CESDK.Classes
{
    /// <summary>Definition used when creating or extending a Cheat Engine structure.</summary>
    public sealed record StructureElementDefinition(
        long Offset,
        string Name,
        int VariableType,
        int ByteSize = 0,
        string ChildClassName = "");

    /// <summary>One element in a global Cheat Engine structure.</summary>
    public sealed record StructureElementInfo(
        int Index,
        long Offset,
        string Name,
        int VariableType,
        int ByteSize,
        string ChildClassName);

    /// <summary>A global Cheat Engine structure and its elements.</summary>
    public sealed record StructureInfo(
        string Name,
        long Size,
        bool Internal,
        IReadOnlyList<StructureElementInfo> Elements);

    /// <summary>One differing structure field between two base addresses.</summary>
    public sealed record StructureDifference(
        string Element,
        long Offset,
        object? FirstValue,
        object? SecondValue);

    /// <summary>Global structure creation, inspection, autoguess, and comparison.</summary>
    public static class StructureManager
    {
        private static readonly Lua.LuaNative lua = PluginContext.Lua;

        /// <summary>Lists every global structure.</summary>
        public static List<StructureInfo> List()
        {
            int count = LuaUtils.CallLuaFunction(
                "getStructureCount",
                "get structure count",
                () => lua.ToInteger(-1));
            var structures = new List<StructureInfo>(count);
            for (int index = 0; index < count; index++)
            {
                IntPtr pointer = GetStructurePointer(index);
                if (pointer != IntPtr.Zero)
                    structures.Add(ReadStructure(pointer));
            }
            return structures;
        }

        /// <summary>Gets a named global or internal structure.</summary>
        public static StructureInfo? Get(string name)
        {
            IntPtr pointer = GetStructurePointer(name);
            return pointer == IntPtr.Zero ? null : ReadStructure(pointer);
        }

        /// <summary>Creates a structure, populates its elements, and adds it to the global list.</summary>
        public static StructureInfo Create(
            string name,
            IReadOnlyList<StructureElementDefinition> elements,
            bool isInternal = false)
        {
            if (GetStructurePointer(name) != IntPtr.Zero)
                throw new InvalidOperationException($"A structure named '{name}' already exists");

            IntPtr pointer = LuaUtils.CallLuaFunction(
                "createStructure",
                "create structure",
                ReadObjectPointer,
                name);
            if (pointer == IntPtr.Zero)
                throw new InvalidOperationException("Cheat Engine did not create the structure");

            try
            {
                foreach (StructureElementDefinition element in elements)
                    AddElement(pointer, element);
                SetField(pointer, "Internal", isInternal);
                CallObjectMethod(pointer, "addToGlobalStructureList");
                return ReadStructure(pointer);
            }
            catch
            {
                TryCallObjectMethod(pointer, "destroy");
                throw;
            }
        }

        /// <summary>Removes and destroys a named global structure.</summary>
        public static void Remove(string name)
        {
            IntPtr pointer = RequireStructure(name);
            CallObjectMethod(pointer, "removeFromGlobalStructureList");
            CallObjectMethod(pointer, "destroy");
        }

        /// <summary>Adds an element to a named structure.</summary>
        public static StructureInfo AddElement(string structureName, StructureElementDefinition element)
        {
            IntPtr pointer = RequireStructure(structureName);
            AddElement(pointer, element);
            return ReadStructure(pointer);
        }

        /// <summary>Removes an element from a named structure by index.</summary>
        public static StructureInfo RemoveElement(string structureName, int index)
        {
            IntPtr structure = RequireStructure(structureName);
            IntPtr element = GetElementPointer(structure, index);
            if (element == IntPtr.Zero)
                throw new ArgumentOutOfRangeException(nameof(index), "Structure element does not exist");
            CallObjectMethod(element, "destroy");
            return ReadStructure(structure);
        }

        /// <summary>Lets Cheat Engine infer structure fields from a target address.</summary>
        public static StructureInfo AutoGuess(
            string structureName,
            ulong baseAddress,
            int offset,
            int size)
        {
            IntPtr pointer = RequireStructure(structureName);
            CallObjectMethod(pointer, "autoGuess", baseAddress, offset, size);
            return ReadStructure(pointer);
        }

        /// <summary>Compares interpreted structure fields at two base addresses.</summary>
        public static List<StructureDifference> Compare(
            string structureName,
            ulong firstAddress,
            ulong secondAddress,
            int maxDifferences)
        {
            IntPtr structure = RequireStructure(structureName);
            int count = GetIntField(structure, "Count");
            var differences = new List<StructureDifference>();
            for (int index = 0; index < count && differences.Count < maxDifferences; index++)
            {
                IntPtr element = GetElementPointer(structure, index);
                if (element == IntPtr.Zero)
                    continue;

                object? first = CallObjectMethodValue(element, "getValueFromBase", firstAddress);
                object? second = CallObjectMethodValue(element, "getValueFromBase", secondAddress);
                if (!Equals(first, second))
                {
                    differences.Add(new StructureDifference(
                        GetStringField(element, "Name"),
                        GetLongField(element, "Offset"),
                        first,
                        second));
                }
            }
            return differences;
        }

        private static StructureInfo ReadStructure(IntPtr pointer)
        {
            int count = GetIntField(pointer, "Count");
            var elements = new List<StructureElementInfo>(count);
            for (int index = 0; index < count; index++)
            {
                IntPtr element = GetElementPointer(pointer, index);
                if (element == IntPtr.Zero)
                    continue;
                elements.Add(new StructureElementInfo(
                    index,
                    GetLongField(element, "Offset"),
                    GetStringField(element, "Name"),
                    GetIntField(element, "Vartype"),
                    GetIntField(element, "Bytesize"),
                    GetStringField(element, "ChildClassName")));
            }

            return new StructureInfo(
                GetStringField(pointer, "Name"),
                GetLongField(pointer, "Size"),
                GetBoolField(pointer, "Internal"),
                elements);
        }

        private static void AddElement(IntPtr structure, StructureElementDefinition definition)
        {
            IntPtr element = CallObjectMethodForObject(structure, "addElement");
            if (element == IntPtr.Zero)
                throw new InvalidOperationException("Cheat Engine did not create the structure element");

            try
            {
                SetField(element, "Offset", definition.Offset);
                SetField(element, "Name", definition.Name);
                SetField(element, "Vartype", definition.VariableType);
                if (definition.ByteSize > 0)
                    SetField(element, "Bytesize", definition.ByteSize);
                if (!string.IsNullOrEmpty(definition.ChildClassName))
                    SetField(element, "ChildClassName", definition.ChildClassName);
            }
            catch
            {
                TryCallObjectMethod(element, "destroy");
                throw;
            }
        }

        private static IntPtr RequireStructure(string name)
        {
            IntPtr pointer = GetStructurePointer(name);
            return pointer != IntPtr.Zero
                ? pointer
                : throw new InvalidOperationException($"Structure '{name}' was not found");
        }

        private static IntPtr GetStructurePointer(object key) =>
            LuaUtils.CallLuaFunction(
                "getStructure",
                "get structure",
                ReadObjectPointer,
                key);

        private static IntPtr ReadObjectPointer() =>
            lua.IsCEObject(-1) ? lua.ToCEObject(-1) : IntPtr.Zero;

        private static IntPtr GetElementPointer(IntPtr structure, int index)
        {
            lua.PushCEObject(structure);
            lua.PushInteger(index);
            lua.GetTable(-2);
            IntPtr result = lua.IsCEObject(-1) ? lua.ToCEObject(-1) : IntPtr.Zero;
            lua.Pop(2);
            return result;
        }

        private static IntPtr CallObjectMethodForObject(IntPtr target, string method) =>
            (IntPtr)(CallObjectMethodValue(target, method)
                ?? IntPtr.Zero);

        private static object? CallObjectMethodValue(IntPtr target, string method, params object?[] arguments)
        {
            int initialTop = lua.GetTop();
            try
            {
                PushMethod(target, method);
                foreach (object? argument in arguments)
                    PushSimple(argument);
                int result = lua.PCall(arguments.Length, 1);
                if (result != 0)
                    throw new InvalidOperationException($"{method}() failed: {lua.ToString(-1)}");

                return lua.IsCEObject(-1)
                    ? lua.ToCEObject(-1)
                    : LuaExecutor.ReadStackValue(-1);
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        private static void CallObjectMethod(IntPtr target, string method, params object?[] arguments)
        {
            int initialTop = lua.GetTop();
            try
            {
                PushMethod(target, method);
                foreach (object? argument in arguments)
                    PushSimple(argument);
                int result = lua.PCall(arguments.Length, 0);
                if (result != 0)
                    throw new InvalidOperationException($"{method}() failed: {lua.ToString(-1)}");
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        private static void TryCallObjectMethod(IntPtr target, string method)
        {
            try { CallObjectMethod(target, method); }
            catch { }
        }

        private static void PushMethod(IntPtr target, string method)
        {
            lua.PushCEObject(target);
            lua.GetField(-1, method);
            if (!lua.IsFunction(-1))
                throw new InvalidOperationException($"{method} method is not available");
        }

        private static void PushSimple(object? value)
        {
            switch (value)
            {
                case null: lua.PushNil(); break;
                case string text: lua.PushString(text); break;
                case bool flag: lua.PushBoolean(flag); break;
                case int number: lua.PushInteger(number); break;
                case long number: lua.PushInteger(number); break;
                case ulong number: lua.PushInteger((long)number); break;
                default: throw new ArgumentException($"Unsupported structure argument type: {value.GetType()}");
            }
        }

        private static int GetIntField(IntPtr target, string field)
        {
            lua.PushCEObject(target);
            lua.GetField(-1, field);
            int value = lua.ToInteger(-1);
            lua.Pop(2);
            return value;
        }

        private static long GetLongField(IntPtr target, string field)
        {
            lua.PushCEObject(target);
            lua.GetField(-1, field);
            long value = lua.ToInteger64(-1);
            lua.Pop(2);
            return value;
        }

        private static string GetStringField(IntPtr target, string field)
        {
            lua.PushCEObject(target);
            lua.GetField(-1, field);
            string value = lua.ToString(-1);
            lua.Pop(2);
            return value;
        }

        private static bool GetBoolField(IntPtr target, string field)
        {
            lua.PushCEObject(target);
            lua.GetField(-1, field);
            bool value = lua.ToBoolean(-1);
            lua.Pop(2);
            return value;
        }

        private static void SetField(IntPtr target, string field, object? value)
        {
            lua.PushCEObject(target);
            PushSimple(value);
            lua.SetField(-2, field);
            lua.Pop(1);
        }
    }
}
