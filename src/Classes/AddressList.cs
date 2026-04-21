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
            // Empty constructor for setting CE object from stack
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
        public long CurrentAddress => GetLongProperty("CurrentAddress");

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
        public double? NumericalValue
        {
            get
            {
                try
                {
                    lua.PushCEObject(CEObject);
                    lua.GetField(-1, "NumericalValue");
                    if (lua.IsNil(-1))
                    {
                        lua.Pop(2);
                        return null;
                    }
                    var val = lua.ToNumber(-1);
                    lua.Pop(2);
                    return val;
                }
                catch
                {
                    return null;
                }
            }
        }

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
        public long GetOffset(int index)
        {
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, "getOffset");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("getOffset method not available");
                }
                lua.PushInteger(index);
                lua.PCall(1, 1);
                var result = lua.ToInteger(-1);
                lua.Pop(2);
                return result;
            }
            catch (Exception ex) when (ex is not AddressListException)
            {
                throw new AddressListException($"Failed to get offset at index {index}", ex);
            }
        }

        /// <summary>
        /// Sets an offset at the given index
        /// </summary>
        public void SetOffset(int index, long value)
        {
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, "setOffset");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("setOffset method not available");
                }
                lua.PushInteger(index);
                lua.PushInteger(value);
                lua.PCall(2, 0);
                lua.Pop(1);
            }
            catch (Exception ex) when (ex is not AddressListException)
            {
                throw new AddressListException($"Failed to set offset at index {index}", ex);
            }
        }

        /// <summary>
        /// Gets a child memory record at the given index
        /// </summary>
        public MemoryRecord GetChild(int index)
        {
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, "Child");
                if (!lua.IsTable(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("Child property not available");
                }
                lua.PushInteger(index);
                lua.GetTable(-2);
                if (lua.IsNil(-1))
                {
                    lua.Pop(3);
                    throw new AddressListException($"No child at index {index}");
                }
                var child = new MemoryRecord();
                child.SetFromStack();
                lua.Pop(3);
                return child;
            }
            catch (Exception ex) when (ex is not AddressListException)
            {
                throw new AddressListException($"Failed to get child at index {index}", ex);
            }
        }

        /// <summary>
        /// Appends this memory record to another memory record (makes it a child)
        /// </summary>
        public void AppendToEntry(MemoryRecord parent)
        {
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, "appendToEntry");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("appendToEntry method not available");
                }
                lua.PushValue(-2); // self
                lua.PushCEObject(parent.Obj);
                lua.PCall(2, 0);
                lua.Pop(1);
            }
            catch (Exception ex) when (ex is not AddressListException)
            {
                throw new AddressListException("Failed to append to entry", ex);
            }
        }

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
            try
            {
                lua.GetGlobal("getAddressList");
                if (lua.IsNil(-1))
                    throw new AddressListException("getAddressList function not available");

                lua.PCall(0, 1);

                if (lua.IsCEObject(-1))
                    CEObject = lua.ToCEObject(-1);
                else
                    throw new AddressListException("getAddressList did not return a valid object");
            }
            finally
            {
                lua.SetTop(0);
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
        public MemoryRecord GetMemoryRecord(int index)
        {
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, "getMemoryRecord");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("getMemoryRecord method not available");
                }
                lua.PushValue(-2); // self
                lua.PushInteger(index);
                lua.PCall(2, 1);

                if (lua.IsNil(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException($"No memory record at index {index}");
                }

                var record = new MemoryRecord();
                record.SetFromStack();
                lua.Pop(2);
                return record;
            }
            catch (Exception ex) when (ex is not AddressListException)
            {
                throw new AddressListException($"Failed to get memory record at index {index}", ex);
            }
        }

        /// <summary>
        /// Gets a memory record by its description
        /// </summary>
        public MemoryRecord? GetMemoryRecordByDescription(string description)
        {
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, "getMemoryRecordByDescription");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("getMemoryRecordByDescription method not available");
                }
                lua.PushValue(-2); // self
                lua.PushString(description);
                lua.PCall(2, 1);

                if (lua.IsNil(-1))
                {
                    lua.Pop(2);
                    return null;
                }

                var record = new MemoryRecord();
                record.SetFromStack();
                lua.Pop(2);
                return record;
            }
            catch (Exception ex) when (ex is not AddressListException)
            {
                throw new AddressListException($"Failed to get memory record by description '{description}'", ex);
            }
        }

        /// <summary>
        /// Gets all memory records with the specified description
        /// </summary>
        public List<MemoryRecord> GetMemoryRecordsWithDescription(string description)
        {
            var records = new List<MemoryRecord>();
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, "getMemoryRecordsWithDescription");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("getMemoryRecordsWithDescription method not available");
                }
                lua.PushValue(-2); // self
                lua.PushString(description);
                lua.PCall(2, 1);

                if (lua.IsTable(-1))
                {
                    // Iterate through the table
                    lua.PushNil();
                    while (lua.Next(-2) != 0)
                    {
                        if (lua.IsCEObject(-1))
                        {
                            var record = new MemoryRecord();
                            record.SetFromStack();
                            records.Add(record);
                        }
                        lua.Pop(1); // Pop value, keep key for next iteration
                    }
                }

                lua.Pop(2);
                return records;
            }
            catch (Exception ex) when (ex is not AddressListException)
            {
                throw new AddressListException($"Failed to get memory records with description '{description}'", ex);
            }
        }

        /// <summary>
        /// Gets a memory record by its unique ID
        /// </summary>
        public MemoryRecord? GetMemoryRecordByID(int id)
        {
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, "getMemoryRecordByID");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("getMemoryRecordByID method not available");
                }
                lua.PushValue(-2); // self
                lua.PushInteger(id);
                lua.PCall(2, 1);

                if (lua.IsNil(-1))
                {
                    lua.Pop(2);
                    return null;
                }

                var record = new MemoryRecord();
                record.SetFromStack();
                lua.Pop(2);
                return record;
            }
            catch (Exception ex) when (ex is not AddressListException)
            {
                throw new AddressListException($"Failed to get memory record by ID {id}", ex);
            }
        }

        /// <summary>
        /// Creates a new memory record and adds it to the address list
        /// </summary>
        public MemoryRecord CreateMemoryRecord()
        {
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, "createMemoryRecord");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("createMemoryRecord method not available");
                }
                lua.PushValue(-2); // self
                lua.PCall(1, 1);

                if (lua.IsNil(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("createMemoryRecord returned nil");
                }

                var record = new MemoryRecord();
                record.SetFromStack();
                lua.Pop(2);
                return record;
            }
            catch (Exception ex) when (ex is not AddressListException)
            {
                throw new AddressListException("Failed to create memory record", ex);
            }
        }

        /// <summary>
        /// Gets the currently selected memory record
        /// </summary>
        public MemoryRecord? GetSelectedRecord()
        {
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, "getSelectedRecord");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("getSelectedRecord method not available");
                }
                lua.PushValue(-2); // self
                lua.PCall(1, 1);

                if (lua.IsNil(-1))
                {
                    lua.Pop(2);
                    return null;
                }

                var record = new MemoryRecord();
                record.SetFromStack();
                lua.Pop(2);
                return record;
            }
            catch (Exception ex) when (ex is not AddressListException)
            {
                throw new AddressListException("Failed to get selected record", ex);
            }
        }

        /// <summary>
        /// Sets the currently selected memory record (unselects all others)
        /// </summary>
        public void SetSelectedRecord(MemoryRecord record)
        {
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, "setSelectedRecord");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("setSelectedRecord method not available");
                }
                lua.PushValue(-2); // self
                lua.PushCEObject(record.Obj);
                lua.PCall(2, 0);
                lua.Pop(1);
            }
            catch (Exception ex) when (ex is not AddressListException)
            {
                throw new AddressListException("Failed to set selected record", ex);
            }
        }

        /// <summary>
        /// Gets all selected memory records
        /// </summary>
        public List<MemoryRecord> GetSelectedRecords()
        {
            var records = new List<MemoryRecord>();
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, "getSelectedRecords");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("getSelectedRecords method not available");
                }
                lua.PushValue(-2); // self
                lua.PCall(1, 1);

                if (lua.IsTable(-1))
                {
                    lua.PushNil();
                    while (lua.Next(-2) != 0)
                    {
                        if (lua.IsCEObject(-1))
                        {
                            var record = new MemoryRecord();
                            record.SetFromStack();
                            records.Add(record);
                        }
                        lua.Pop(1);
                    }
                }

                lua.Pop(2);
                return records;
            }
            catch (Exception ex) when (ex is not AddressListException)
            {
                throw new AddressListException("Failed to get selected records", ex);
            }
        }

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
            // In CE Lua, calling destroy on the memory record removes it from the list
            try
            {
                lua.PushCEObject(record.Obj);
                lua.GetField(-1, "destroy");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(2);
                    throw new AddressListException("destroy method not available on memory record");
                }
                lua.PushValue(-2); // self
                lua.PCall(1, 0);
                lua.Pop(1);
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
            var records = new List<MemoryRecord>();
            var count = Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    records.Add(GetMemoryRecord(i));
                }
                catch
                {
                    // Skip records that fail to load
                }
            }
            return records;
        }

        /// <summary>
        /// Clears all memory records from the address list
        /// </summary>
        public void Clear()
        {
            // Delete records in reverse order to avoid index shifting issues
            for (int i = Count - 1; i >= 0; i--)
            {
                try
                {
                    DeleteMemoryRecordAt(i);
                }
                catch
                {
                    // Continue trying to delete other records
                }
            }
        }

        #endregion
    }
}
