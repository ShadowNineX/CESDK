using System;
using CESDK.Lua;

namespace CESDK.Classes
{
    /// <summary>
    /// Base exception for all CESDK operations
    /// </summary>
    public class CesdkException : Exception
    {
        public CesdkException(string message) : base(message) { }
        public CesdkException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Base class for wrapping Cheat Engine objects in C#. Owned objects must be disposed on
    /// Cheat Engine's main thread; borrowed CE-owned objects set <see cref="SuppressDestroy"/>.
    /// </summary>
    public abstract class CEObjectWrapper : IDisposable
    {
        protected readonly LuaNative lua;
        protected IntPtr CEObject;

        protected CEObjectWrapper()
        {
            lua = PluginContext.Lua;
            CEObject = IntPtr.Zero;
        }

        /// <summary>
        /// Pushes the CE object onto the Lua stack
        /// </summary>
        internal void PushCEObject()
        {
            if (CEObject == IntPtr.Zero)
                throw new InvalidOperationException("CE object is not initialized");

            lua.PushCEObject(CEObject);
        }

        /// <summary>
        /// Sets the CE object from the current top of the Lua stack
        /// </summary>
        protected void SetCEObjectFromStack()
        {
            if (!lua.IsCEObject(-1))
                throw new InvalidOperationException("Top of stack is not a CE object");

            CEObject = lua.ToCEObject(-1);
        }

        #region Property Helpers

        protected int GetIntProperty(string name) =>
            GetProperty(name, () =>
            {
                if (!lua.IsNumber(-1))
                    throw new InvalidOperationException($"{name} property is not an integer");
                return lua.ToInteger(-1);
            });

        protected long GetLongProperty(string name) =>
            GetProperty(name, () =>
            {
                if (!lua.IsNumber(-1))
                    throw new InvalidOperationException($"{name} property is not an integer");
                return lua.ToInteger64(-1);
            });

        protected string GetStringProperty(string name) =>
            GetProperty(name, () =>
            {
                if (!lua.IsString(-1))
                    throw new InvalidOperationException($"{name} property is not a string");
                return lua.ToString(-1) ?? "";
            });

        protected bool GetBoolProperty(string name) =>
            GetProperty(name, () =>
            {
                if (!lua.IsBoolean(-1))
                    throw new InvalidOperationException($"{name} property is not a boolean");
                return lua.ToBoolean(-1);
            });

        protected void SetIntProperty(string name, int value) =>
            SetProperty(name, () => lua.PushInteger(value));

        protected void SetStringProperty(string name, string value) =>
            SetProperty(name, () => lua.PushString(value));

        protected void SetBoolProperty(string name, bool value) =>
            SetProperty(name, () => lua.PushBoolean(value));

        protected double? GetNullableNumberProperty(string name) =>
            GetProperty(name, () => lua.IsNil(-1) ? (double?)null : lua.ToNumber(-1));

        private T GetProperty<T>(string name, Func<T> readValue)
        {
            EnsureInitialized();
            int initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, name);
                return readValue();
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        private void SetProperty(string name, Action pushValue)
        {
            EnsureInitialized();
            int initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                pushValue();
                lua.SetField(-2, name);
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        protected T GetIndexedProperty<T>(string name, int index, Func<T> readValue)
        {
            EnsureInitialized();
            int initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, name);
                if (!lua.IsTable(-1))
                    throw new InvalidOperationException($"{name} property is not indexable");

                lua.PushInteger(index);
                lua.GetTable(-2);
                return readValue();
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        #endregion

        #region Method Helpers

        /// <summary>
        /// Calls a parameterless method on this CE object.
        /// </summary>
        protected void CallMethod(string methodName)
        {
            EnsureInitialized();
            int initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, methodName);
                if (!lua.IsFunction(-1))
                    throw new InvalidOperationException($"{methodName} method not available");

                int result = lua.PCall(0, 0);
                if (result != 0)
                    throw new InvalidOperationException($"{methodName}() call failed: {lua.ToString(-1)}");
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        /// <summary>
        /// Calls a CE object method with arguments and one return value while preserving the Lua stack.
        /// </summary>
        protected T CallMethod<T>(
            string methodName,
            Action pushArguments,
            int argumentCount,
            Func<T> readResult)
        {
            EnsureInitialized();
            int initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, methodName);
                if (!lua.IsFunction(-1))
                    throw new InvalidOperationException($"{methodName} method not available");

                pushArguments();
                int result = lua.PCall(argumentCount, 1);
                if (result != 0)
                    throw new InvalidOperationException($"{methodName}() call failed: {lua.ToString(-1)}");

                return readResult();
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        /// <summary>
        /// Calls a CE object method with arguments and no return values while preserving the Lua stack.
        /// </summary>
        protected void CallMethod(string methodName, Action pushArguments, int argumentCount)
        {
            EnsureInitialized();
            int initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, methodName);
                if (!lua.IsFunction(-1))
                    throw new InvalidOperationException($"{methodName} method not available");

                pushArguments();
                int result = lua.PCall(argumentCount, 0);
                if (result != 0)
                    throw new InvalidOperationException($"{methodName}() call failed: {lua.ToString(-1)}");
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        #endregion

        /// <summary>
        /// Whether this wrapper should skip destruction because Cheat Engine owns the object.
        /// </summary>
        protected internal bool SuppressDestroy { get; set; }

        /// <summary>
        /// Destroys an owned Cheat Engine object. Call on Cheat Engine's main thread.
        /// </summary>
        public virtual void Dispose()
        {
            if (CEObject == IntPtr.Zero)
                return;

            if (SuppressDestroy)
            {
                CEObject = IntPtr.Zero;
                return;
            }

            int initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, "destroy");
                if (!lua.IsFunction(-1))
                    throw new InvalidOperationException("destroy method not available");

                int result = lua.PCall(0, 0);
                if (result != 0)
                    throw new InvalidOperationException($"destroy() call failed: {lua.ToString(-1)}");

                CEObject = IntPtr.Zero;
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        private void EnsureInitialized()
        {
            if (CEObject == IntPtr.Zero)
                throw new InvalidOperationException("CE object is not initialized");
        }
    }
}