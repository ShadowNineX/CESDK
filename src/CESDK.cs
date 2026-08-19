using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using CESDK.Lua;
using System.IO;

namespace CESDK
{
    [StructLayout(LayoutKind.Sequential)]
    public struct TExportedFunctions
    {
        public int sizeofExportedFunctions;
        public IntPtr GetLuaState;
        public IntPtr LuaRegister;
        public IntPtr LuaPushClassInstance;
        public IntPtr ProcessMessages;
        public IntPtr CheckSynchronize;
    }

    [SuppressMessage("Naming", "S101:Types should be named in PascalCase", Justification = "CESDK is an acronym")]
    public class CESDK
    {
        private const int PLUGIN_VERSION = 6;
        private static CESDK? mainSelf;
        private static CheatEnginePlugin? _currentPlugin;
        private static readonly object SynchronizeLock = new();
        private static Action? synchronizedAction;
        private static readonly LuaNative.LuaCFunction SynchronizeCallback = _ =>
        {
            synchronizedAction?.Invoke();
            return 0;
        };
        public static CheatEnginePlugin? CurrentPlugin => _currentPlugin;

        private TExportedFunctions pluginExports;

        private static IntPtr PluginNamePtr;

        #region Delegates

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool delegateGetVersion(ref TPluginVersion PluginVersion, int TPluginVersionSize);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool delegateEnablePlugin(ref TExportedFunctions ExportedFunctions, uint pluginid);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool delegateDisablePlugin();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void delegateProcessMessages();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate bool delegateCheckSynchronize(int timeout);

        private readonly delegateGetVersion? delGetVersion;
        private readonly delegateEnablePlugin? delEnablePlugin;
        private readonly delegateDisablePlugin? delDisablePlugin;
        private delegateProcessMessages? delProcessMessages;
        private delegateCheckSynchronize? delCheckSynchronize;

        #endregion

        #region Internal Structures

        [StructLayout(LayoutKind.Sequential)]
        private struct TPluginVersion
        {
            public uint version;
            public IntPtr name;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TPluginInit
        {
            public IntPtr name;
            public IntPtr getVersionPtr;
            public IntPtr enablePluginPtr;
            public IntPtr disablePluginPtr;
            public int version;
        }

        #endregion

        private CESDK()
        {
            delGetVersion = GetVersion;
            delEnablePlugin = EnablePlugin;
            delDisablePlugin = DisablePlugin;
        }

        #region Delegate Implementations

        private static bool GetVersion(ref TPluginVersion PluginVersion, int TPluginVersionSize)
        {
            PluginVersion.name = PluginNamePtr;
            PluginVersion.version = PLUGIN_VERSION;
            return true;
        }

        private static bool EnablePlugin(ref TExportedFunctions ExportedFunctions, uint pluginid)
        {
            try
            {
                if (mainSelf == null || CurrentPlugin == null)
                    return false;

                mainSelf.pluginExports = ExportedFunctions;

                mainSelf.delProcessMessages ??= Marshal.GetDelegateForFunctionPointer<delegateProcessMessages>(mainSelf.pluginExports.ProcessMessages);
                mainSelf.delCheckSynchronize ??= Marshal.GetDelegateForFunctionPointer<delegateCheckSynchronize>(mainSelf.pluginExports.CheckSynchronize);

                // Initialize Lua with CE's exported functions
                PluginContext.Initialize(ExportedFunctions.GetLuaState, ExportedFunctions.LuaRegister, ExportedFunctions.LuaPushClassInstance);

                // Call the plugin enable hook
                CurrentPlugin.EnablePlugin();

                return true; // Must return true to CE
            }
            catch (Exception ex)
            {
                PluginLogger.LogException(ex);
                return false;
            }
        }

        private static bool DisablePlugin()
        {
            try
            {
                CurrentPlugin?.DisablePlugin();
                return true;
            }
            catch (Exception ex)
            {
                PluginLogger.LogException(ex);
                return false;
            }
        }

        #endregion

        #region Public Helpers

        public static void ProcessMessages()
        {
            mainSelf?.delProcessMessages?.Invoke();
        }

        public static bool CheckSynchronize(int timeout)
        {
            return mainSelf?.delCheckSynchronize?.Invoke(timeout) ?? false;
        }

        /// <summary>
        /// Executes an action on the GUI thread using CE's synchronize() function.
        /// This is required for operations that interact with CE's UI components like the address list.
        /// </summary>
        /// <param name="action">The action to execute on the GUI thread</param>
        public static void Synchronize(Action action)
        {
            lock (SynchronizeLock)
            {
                var lua = PluginContext.Lua;
                Exception? caughtException = null;
                synchronizedAction = () =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        caughtException = ex;
                    }
                };

                try
                {
                    lua.PushCFunction(SynchronizeCallback);
                    lua.SetGlobal("__cesdk_sync_callback");
                    lua.DoString("synchronize(__cesdk_sync_callback)");

                    if (caughtException != null)
                        throw caughtException;
                }
                finally
                {
                    synchronizedAction = null;
                    lua.PushNil();
                    lua.SetGlobal("__cesdk_sync_callback");
                }
            }
        }

        /// <summary>
        /// Executes a function on the GUI thread and returns the result.
        /// </summary>
        /// <typeparam name="T">The return type</typeparam>
        /// <param name="func">The function to execute on the GUI thread</param>
        /// <returns>The result of the function</returns>
        public static T Synchronize<T>(Func<T> func)
        {
            T result = default!;
            Synchronize(() => { result = func(); });
            return result;
        }

        #endregion

#if NETFRAMEWORK
        public static int CEPluginInitialize(string parameters)
        {
            ulong args = ulong.Parse(parameters);
#else
        public static int CEPluginInitialize(IntPtr args, int size)
        {
#endif
            try
            {
                mainSelf ??= new CESDK();

                if (PluginNamePtr == IntPtr.Zero)
                {
                    foreach (var type in typeof(CheatEnginePlugin).Assembly.GetTypes())
                    {
                        if (type.IsSubclassOf(typeof(CheatEnginePlugin)) && !type.IsAbstract)
                        {
                            _currentPlugin = (CheatEnginePlugin)Activator.CreateInstance(type)!;
                            break;
                        }
                    }

                    if (_currentPlugin == null)
                        return 0;

                    PluginNamePtr = Marshal.StringToHGlobalAnsi(_currentPlugin.Name);
                }

                ulong address = (ulong)args;
                var pluginInit = new TPluginInit
                {
                    name = PluginNamePtr,
                    getVersionPtr = Marshal.GetFunctionPointerForDelegate(mainSelf.delGetVersion!),
                    enablePluginPtr = Marshal.GetFunctionPointerForDelegate(mainSelf.delEnablePlugin!),
                    disablePluginPtr = Marshal.GetFunctionPointerForDelegate(mainSelf.delDisablePlugin!),
                    version = PLUGIN_VERSION
                };

                Marshal.StructureToPtr(pluginInit, (IntPtr)address, false);
                return 1;
            }
            catch (Exception ex)
            {
                PluginLogger.LogException(ex);
                return 0;
            }
        }
    }
}
