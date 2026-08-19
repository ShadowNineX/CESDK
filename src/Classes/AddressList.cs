using System;
using System.Collections.Generic;
using CESDK.Lua;

namespace CESDK.Classes
{
    public class AddressListException : CesdkException
    {
        public AddressListException(string message) : base(message) { }
        public AddressListException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// MemoryRecord class that wraps Cheat Engine's MemoryRecord Lua object.
    /// Memory records are the entries visible in the address list.
    /// </summary>
    public class MemoryRecord : CEObjectWrapper
    {
        internal MemoryRecord()
        {
            SuppressDestroy = true;
        }

        /// <summary>
        /// Sets the CE object from a MemoryRecord object on the Lua stack
        /// </summary>
        internal void SetFromStack()
        {
            if (!lua.IsCEObject(-1))
                throw new AddressListException("Top of stack is not a MemoryRecord CE object");

            SetCEObjectFromStack();
        }

        /// <summary>
        /// Internal pointer for passing to other CE functions
        /// </summary>
        internal IntPtr Obj => CEObject;

        internal void Delete()
        {
            SuppressDestroy = false;
            Dispose();
        }

        #region Properties

        /// <summary>
        /// Gets the unique ID of this memory record
        /// </summary>
        public int ID => GetIntProperty("ID");

        /// <summary>
        /// Gets the index of this record in the address list (0 is top)
        /// </summary>
        public int Index => GetIntProperty("Index");

        /// <summary>
        /// Gets or sets the description of the memory record
        /// </summary>
        public string Description
        {
            get => GetStringProperty("Description");
            set => SetStringProperty("Description", value);
        }

        /// <summary>
        /// Gets or sets the interpretable address string
        /// </summary>
        public string Address
        {
            get => GetStringProperty("Address");
            set => SetStringProperty("Address", value);
        }

        /// <summary>
        /// Gets the address string as shown in CE (ReadOnly)
        /// </summary>
        public string AddressString => GetStringProperty("AddressString");

        /// <summary>
        /// Gets the current resolved address as an integer
        /// </summary>
        public ulong CurrentAddress => (ulong)GetLongProperty("CurrentAddress");

        /// <summary>
        /// Gets or sets the variable type of this record
        /// </summary>
        public VariableType VarType
        {
            get => (VariableType)GetIntProperty("Type");
            set => SetIntProperty("Type", (int)value);
        }

        /// <summary>
        /// Gets or sets the value in string form
        /// </summary>
        public string Value
        {
            get => GetStringProperty("Value");
            set => SetStringProperty("Value", value);
        }

        /// <summary>
        /// Gets or sets the value in numerical form. Returns null if it cannot be parsed.
        /// </summary>
        public double? NumericalValue => GetNullableNumberProperty("NumericalValue");

        /// <summary>
        /// Gets whether this record is selected
        /// </summary>
        public bool Selected => GetBoolProperty("Selected");

        /// <summary>
        /// Gets or sets whether this entry is active/frozen
        /// </summary>
        public bool Active
        {
            get => GetBoolProperty("Active");
            set => SetBoolProperty("Active", value);
        }

        /// <summary>
        /// Gets or sets the color of this record
        /// </summary>
        public int Color
        {
            get => GetIntProperty("Color");
            set => SetIntProperty("Color", value);
        }

        /// <summary>
        /// Gets or sets whether to show value as hexadecimal
        /// </summary>
        public bool ShowAsHex
        {
            get => GetBoolProperty("ShowAsHex");
            set => SetBoolProperty("ShowAsHex", value);
        }

        /// <summary>
        /// Gets or sets whether to show value as signed
        /// </summary>
        public bool ShowAsSigned
        {
            get => GetBoolProperty("ShowAsSigned");
            set => SetBoolProperty("ShowAsSigned", value);
        }

        /// <summary>
        /// Gets or sets whether value can increase
        /// </summary>
        public bool AllowIncrease
        {
            get => GetBoolProperty("AllowIncrease");
            set => SetBoolProperty("AllowIncrease", value);
        }

        /// <summary>
        /// Gets or sets whether value can decrease
        /// </summary>
        public bool AllowDecrease
        {
            get => GetBoolProperty("AllowDecrease");
            set => SetBoolProperty("AllowDecrease", value);
        }

        /// <summary>
        /// Gets or sets whether this record is collapsed
        /// </summary>
        public bool Collapsed
        {
            get => GetBoolProperty("Collapsed");
            set => SetBoolProperty("Collapsed", value);
        }

        /// <summary>
        /// Gets or sets whether this is a group header with no address/value info
        /// </summary>
        public bool IsGroupHeader
        {
            get => GetBoolProperty("IsGroupHeader");
            set => SetBoolProperty("IsGroupHeader", value);
        }

        /// <summary>
        /// Gets or sets whether this is a group header with address
        /// </summary>
        public bool IsAddressGroupHeader
        {
            get => GetBoolProperty("IsAddressGroupHeader");
            set => SetBoolProperty("IsAddressGroupHeader", value);
        }

        /// <summary>
        /// Gets whether the address is readable
        /// </summary>
        public bool IsReadable => GetBoolProperty("IsReadable");

        /// <summary>
        /// Gets or sets the number of pointer offsets (0 for normal address)
        /// </summary>
        public int OffsetCount
        {
            get => GetIntProperty("OffsetCount");
            set => SetIntProperty("OffsetCount", value);
        }

        /// <summary>
        /// Gets the number of child records
        /// </summary>
        public int Count => GetIntProperty("Count");

        /// <summary>
        /// Gets or sets the auto assembler script (if type is vtAutoAssembler)
        /// </summary>
        public string Script
        {
            get => GetStringProperty("Script");
            set => SetStringProperty("Script", value);
        }

        /// <summary>
        /// Gets or sets whether this record should not be saved
        /// </summary>
        public bool DontSave
        {
            get => GetBoolProperty("DontSave");
            set => SetBoolProperty("DontSave", value);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets an offset at the given index
        /// </summary>
        public long GetOffset(int index) =>
            CallMethod(
                "getOffset",
                () => lua.PushInteger(index),
                1,
                () =>
                {
                    if (!lua.IsNumber(-1))
                        throw new AddressListException($"getOffset returned no value for index {index}");
                    return lua.ToInteger64(-1);
                });

        /// <summary>
        /// Sets an offset at the given index
        /// </summary>
        public void SetOffset(int index, long value) =>
            CallMethod(
                "setOffset",
                () =>
                {
                    lua.PushInteger(index);
                    lua.PushInteger(value);
                },
                2);

        /// <summary>
        /// Gets a child memory record at the given index
        /// </summary>
        public MemoryRecord GetChild(int index) =>
            GetIndexedProperty(
                "Child",
                index,
                () =>
                {
                    if (!lua.IsCEObject(-1))
                        throw new AddressListException($"No child at index {index}");

                    var child = new MemoryRecord();
                    child.SetFromStack();
                    return child;
                });

        /// <summary>
        /// Appends this memory record to another memory record (makes it a child)
        /// </summary>
        public void AppendToEntry(MemoryRecord parent) =>
            CallMethod("appendToEntry", () => lua.PushCEObject(parent.Obj), 1);

        /// <summary>
        /// Disables the entry without executing the disable section
        /// </summary>
        public void DisableWithoutExecute()
        {
            CallMethod("disableWithoutExecute");
        }

        /// <summary>
        /// Reinterprets the memory record
        /// </summary>
        public void Reinterpret()
        {
            CallMethod("reinterpret");
        }

        /// <summary>
        /// Call when starting a long edit operation
        /// </summary>
        public void BeginEdit()
        {
            CallMethod("beginEdit");
        }

        /// <summary>
        /// Call when ending a long edit operation
        /// </summary>
        public void EndEdit()
        {
            CallMethod("endEdit");
        }

        #endregion
    }

    /// <summary>
    /// AddressList class that wraps Cheat Engine's AddressList Lua object.
    /// The address list is the main cheat table that contains all memory records.
    /// </summary>
    public class AddressList : CEObjectWrapper
    {
        public AddressList()
        {
            int initialTop = lua.GetTop();
            try
            {
                lua.GetGlobal("getAddressList");
                if (!lua.IsFunction(-1))
                    throw new AddressListException("getAddressList function not available");

                int result = lua.PCall(0, 1);
                if (result != 0)
                    throw new AddressListException($"getAddressList() call failed: {lua.ToString(-1)}");

                if (!lua.IsCEObject(-1))
                    throw new AddressListException("getAddressList did not return a valid object");

                CEObject = lua.ToCEObject(-1);
                SuppressDestroy = true;
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        #region Properties

        /// <summary>
        /// Gets the number of records in the table
        /// </summary>
        public int Count => GetIntProperty("Count");

        /// <summary>
        /// Gets the number of selected records
        /// </summary>
        public int SelCount => GetIntProperty("SelCount");

        /// <summary>
        /// Gets the table version of the last loaded table
        /// </summary>
        public int LoadedTableVersion => GetIntProperty("LoadedTableVersion");

        #endregion

        #region Indexer

        /// <summary>
        /// Gets a memory record at the specified index
        /// </summary>
        public MemoryRecord this[int index] => GetMemoryRecord(index);

        #endregion

        #region Methods

        /// <summary>
        /// Gets a memory record at the specified index
        /// </summary>
        public MemoryRecord GetMemoryRecord(int index) =>
            CallMethod(
                "getMemoryRecord",
                () => lua.PushInteger(index),
                1,
                () => ReadRecord($"No memory record at index {index}"));

        /// <summary>
        /// Gets a memory record by its description
        /// </summary>
        public MemoryRecord? GetMemoryRecordByDescription(string description) =>
            CallMethod(
                "getMemoryRecordByDescription",
                () => lua.PushString(description),
                1,
                ReadOptionalRecord);

        /// <summary>
        /// Gets all memory records with the specified description
        /// </summary>
        public List<MemoryRecord> GetMemoryRecordsWithDescription(string description)
        {
            var records = new List<MemoryRecord>();
            for (int index = 0; index < Count; index++)
            {
                MemoryRecord record = GetMemoryRecord(index);
                if (string.Equals(record.Description, description, StringComparison.Ordinal))
                    records.Add(record);
                else
                    record.Dispose();
            }
            return records;
        }

        /// <summary>
        /// Gets a memory record by its unique ID
        /// </summary>
        public MemoryRecord? GetMemoryRecordByID(int id) =>
            CallMethod(
                "getMemoryRecordByID",
                () => lua.PushInteger(id),
                1,
                ReadOptionalRecord);

        /// <summary>
        /// Creates a new memory record and adds it to the address list
        /// </summary>
        public MemoryRecord CreateMemoryRecord() =>
            CallMethod(
                "createMemoryRecord",
                () => { },
                0,
                () => ReadRecord("createMemoryRecord returned no MemoryRecord"));

        /// <summary>
        /// Gets the currently selected memory record
        /// </summary>
        public MemoryRecord? GetSelectedRecord() =>
            CallMethod(
                "getSelectedRecord",
                () => { },
                0,
                ReadOptionalRecord);

        /// <summary>
        /// Sets the currently selected memory record (unselects all others)
        /// </summary>
        public void SetSelectedRecord(MemoryRecord record) =>
            CallMethod("setSelectedRecord", () => lua.PushCEObject(record.Obj), 1);

        /// <summary>
        /// Gets all selected memory records
        /// </summary>
        public List<MemoryRecord> GetSelectedRecords() =>
            CallMethod(
                "getSelectedRecords",
                () => { },
                0,
                ReadRecordList);

        /// <summary>
        /// Disables all memory records without executing their [Disable] section
        /// </summary>
        public void DisableAllWithoutExecute()
        {
            CallMethod("disableAllWithoutExecute");
        }

        /// <summary>
        /// Rebuilds the description to record lookup table
        /// </summary>
        public void RebuildDescriptionCache()
        {
            CallMethod("rebuildDescriptionCache");
        }

        /// <summary>
        /// Deletes a memory record from the address list
        /// </summary>
        public void DeleteMemoryRecord(MemoryRecord record)
        {
            try
            {
                record.Delete();
            }
            catch (Exception ex) when (ex is not AddressListException)
            {
                throw new AddressListException("Failed to delete memory record", ex);
            }
        }

        /// <summary>
        /// Deletes a memory record at the specified index
        /// </summary>
        public void DeleteMemoryRecordAt(int index)
        {
            var record = GetMemoryRecord(index);
            DeleteMemoryRecord(record);
        }

        /// <summary>
        /// Deletes a memory record by its description
        /// </summary>
        /// <returns>True if a record was found and deleted</returns>
        public bool DeleteMemoryRecordByDescription(string description)
        {
            var record = GetMemoryRecordByDescription(description);
            if (record == null)
                return false;

            DeleteMemoryRecord(record);
            return true;
        }

        /// <summary>
        /// Gets all memory records as a list
        /// </summary>
        public List<MemoryRecord> GetAllRecords()
        {
            int count = Count;
            var records = new List<MemoryRecord>(count);
            for (int i = 0; i < count; i++)
                records.Add(GetMemoryRecord(i));
            return records;
        }

        /// <summary>
        /// Clears all memory records from the address list
        /// </summary>
        public void Clear()
        {
            for (int i = Count - 1; i >= 0; i--)
                DeleteMemoryRecordAt(i);
        }

        private MemoryRecord ReadRecord(string error)
        {
            if (!lua.IsCEObject(-1))
                throw new AddressListException(error);

            var record = new MemoryRecord();
            record.SetFromStack();
            return record;
        }

        private MemoryRecord? ReadOptionalRecord() =>
            lua.IsNil(-1) ? null : ReadRecord("Cheat Engine returned an invalid MemoryRecord");

        private List<MemoryRecord> ReadRecordList()
        {
            if (lua.IsNil(-1))
                return [];

            if (!lua.IsTable(-1))
                throw new AddressListException("Cheat Engine returned an invalid MemoryRecord list");

            var records = new List<MemoryRecord>();
            lua.PushNil();
            while (lua.Next(-2) != 0)
            {
                try
                {
                    if (!lua.IsCEObject(-1))
                        throw new AddressListException("Cheat Engine returned an invalid MemoryRecord");

                    records.Add(ReadRecord("Cheat Engine returned an invalid MemoryRecord"));
                }
                finally
                {
                    lua.Pop(1);
                }
            }
            return records;
        }

        #endregion
    }
}
