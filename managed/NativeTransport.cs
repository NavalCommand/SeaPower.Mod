using System;
using System.Runtime.InteropServices;
namespace NavalCommand.SeaPower.Mod;
// I/O worker only. This type contains no Unity references.
// DLL is deliberately not FreeLibrary'd; RPC code may have process-lifetime state.
public sealed class NativeTransport {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string path,IntPtr reserved,uint flags);
 [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr module,string name);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate uint Version();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Initialize(uint abi,[MarshalAs(UnmanagedType.LPStr)]string endpoint);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Publish([In]byte[] bytes,uint length);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Poll([Out]byte[] bytes,uint capacity,out uint length);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Shutdown();
 readonly IntPtr library;readonly Initialize initialize;readonly Publish view,result;readonly Poll poll;readonly Shutdown shutdown;
 public NativeTransport(string absolutePath){
  library=LoadLibraryEx(absolutePath,IntPtr.Zero,0x1100);
  if(library==IntPtr.Zero)throw new InvalidOperationException("native_load_failed:"+Marshal.GetLastWin32Error());
  T Bind<T>(string name) where T:Delegate {var p=GetProcAddress(library,name);if(p==IntPtr.Zero)throw new MissingMethodException(name);return Marshal.GetDelegateForFunctionPointer<T>(p);}
  if(Bind<Version>("nc_abi_version")()!=1)throw new InvalidOperationException("abi_mismatch");
  initialize=Bind<Initialize>("nc_initialize");view=Bind<Publish>("nc_publish_view");result=Bind<Publish>("nc_publish_result");poll=Bind<Poll>("nc_poll_order");shutdown=Bind<Shutdown>("nc_shutdown");
 }
 public int Start(string endpoint)=>initialize(1,endpoint);
 public int PublishView(byte[] bytes)=>view(bytes,(uint)bytes.Length);
 public int PublishResult(byte[] bytes)=>result(bytes,(uint)bytes.Length);
 public int PollOrder(byte[] buffer,out uint size)=>poll(buffer,(uint)buffer.Length,out size);
 public int Stop()=>shutdown();
}

