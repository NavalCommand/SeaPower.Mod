using System;
using System.Runtime.InteropServices;

namespace NavalCommand.SeaPower.Mod;
// I/O worker only. This type contains no Unity references.
// DLL is deliberately not FreeLibrary'd; RPC code may have process-lifetime state.
public sealed class NativeTransport : INativeTransport
{
    private const uint AbiVersion = 1;
    // Restrict dependency probing to the DLL directory and standard safe directories.
    private const uint LoadLibrarySearchDllLoadDirectory = 0x100;
    private const uint LoadLibrarySearchDefaultDirectories = 0x1000;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibraryEx(string path, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate uint Version();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int Initialize(uint abi, [MarshalAs(UnmanagedType.LPStr)] string endpoint);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int Publish([In] byte[] bytes, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int Poll([Out] byte[] bytes, uint capacity, out uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int Shutdown();
    readonly IntPtr nativeLibrary;
    readonly Initialize initializeHost;
    readonly Publish publishView;
    readonly Publish publishResult;
    readonly Poll pollOrder;
    readonly Shutdown shutdownHost;
    private bool started;
    private bool stopped;
    public NativeTransport(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !System.IO.Path.IsPathRooted(absolutePath))
        {
            throw new ArgumentException("Native library path must be absolute.");
        }
        nativeLibrary = LoadLibraryEx(absolutePath, IntPtr.Zero, LoadLibrarySearchDllLoadDirectory | LoadLibrarySearchDefaultDirectories);
        if (nativeLibrary == IntPtr.Zero)
        {
            throw new InvalidOperationException("native_load_failed:" + Marshal.GetLastWin32Error());
        }

        T Bind<T>(string name)
            where T : Delegate
        {
            var functionAddress = GetProcAddress(nativeLibrary, name);
            if (functionAddress == IntPtr.Zero)
            {
                throw new MissingMethodException(name);
            }

            return Marshal.GetDelegateForFunctionPointer<T>(functionAddress);
        }

        if (Bind<Version>("nc_abi_version")() != AbiVersion)
        {
            throw new InvalidOperationException("abi_mismatch");
        }

        initializeHost = Bind<Initialize>("nc_initialize");
        publishView = Bind<Publish>("nc_publish_view");
        publishResult = Bind<Publish>("nc_publish_result");
        pollOrder = Bind<Poll>("nc_poll_order");
        shutdownHost = Bind<Shutdown>("nc_shutdown");
    }

    public int Start(string endpoint)
    {
        if (stopped)
        {
            throw new InvalidOperationException("Transport has stopped.");
        }
        if (started)
        {
            return 0;
        }
        int status = initializeHost(AbiVersion, endpoint);
        started = status == 0;
        return status;
    }
    public int PublishView(byte[] bytes) => publishView(bytes, (uint)bytes.Length);
    public int PublishResult(byte[] bytes) => publishResult(bytes, (uint)bytes.Length);
    public int PollOrder(byte[] buffer, out uint size) => pollOrder(buffer, (uint)buffer.Length, out size);
    public int Stop()
    {
        if (stopped || !started)
        {
            return 0;
        }
        stopped = true;
        return shutdownHost();
    }
}
