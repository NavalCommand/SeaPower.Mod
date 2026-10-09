using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using HarmonyLib;
using SeaPower;
using UnityEngine;
using UnityEngine.SceneManagement;
using NavalCommand.SeaPower.Client;
using Google.Protobuf;
namespace NavalCommand.SeaPower.Mod;
public static class Bootstrap {
 public static void Attach(){
  if(System.Environment.GetEnvironmentVariable("NC_TASK004_ENABLED")!="1")return;
  if(Globals.mainThread!=Thread.CurrentThread)throw new InvalidOperationException("engine_thread_required");
  new Runtime().Install();
 }
}
public sealed class Runtime {
 // Worker receives only plain queues/state; it never closes over this runtime.
 sealed class Mailbox {
  public readonly ConcurrentQueue<byte[]> Views=new(),Results=new(),Commands=new();
  public readonly ConcurrentQueue<string> Events=new();
  public volatile bool Stop,Failed;
 }
 readonly Mailbox mailbox=new();
 Provider provider;Harmony harmony;static Runtime instance;float nextSample;Thread worker;
 public void Install(){
  instance=this;provider=new Provider(new NativeGameFacade(),System.Environment.GetEnvironmentVariable("NC_TASK002_SIDE")??"Taskforce1");
  harmony=new Harmony("dev.navalcommand.task004.provider");
  harmony.Patch(typeof(JobPerformer).GetMethod(nameof(JobPerformer.CompleteFixedUpdateJobs)),postfix:new HarmonyMethod(typeof(Runtime).GetMethod(nameof(AfterJobs))));
  harmony.Patch(AccessTools.Method(typeof(GameMain),"Update"),postfix:new HarmonyMethod(typeof(Runtime).GetMethod(nameof(AfterUpdate))));
  SceneManager.sceneUnloaded+=Unloaded;
  Application.quitting+=Stop;
  string directory=System.Environment.GetEnvironmentVariable("NC_TASK002_RUN_DIR");
  string native=System.Environment.GetEnvironmentVariable("NC_TASK004_NATIVE_DLL");
  string endpoint=System.Environment.GetEnvironmentVariable("NC_TASK004_ENDPOINT")??"127.0.0.1:50051";
  var plain=mailbox;
  worker=new Thread(()=>Run(plain,directory,native,endpoint)){IsBackground=true,Name="SeaPower.Mod transport"};worker.Start();
 }
 static void Run(Mailbox m,string directory,string native,string endpoint){
  NativeTransport host=null;
  try{
   using var log=new StreamWriter(Path.Combine(directory,"gateway.log"),false){AutoFlush=true};
   host=new NativeTransport(native);int started=host.Start(endpoint);log.WriteLine(DateTime.UtcNow.ToString("o")+" native-start abi=1 status="+started);
   if(started!=0)throw new InvalidOperationException("host_start_failed:"+started);
   File.WriteAllText(Path.Combine(directory,"gateway-ready.json"),"{\"abi\":1,\"endpoint\":\""+endpoint+"\"}");
   var buffer=new byte[65536];byte[] pendingView=null,pendingResult=null;
   while(!m.Stop){
    if(m.Failed)throw new InvalidOperationException("provider_failed");
    while(m.Events.TryDequeue(out var line))log.WriteLine(DateTime.UtcNow.ToString("o")+" "+line);
    while(m.Views.TryDequeue(out var v))pendingView=v;
    if(pendingView!=null){int rc=host.PublishView(pendingView);if(rc==0)pendingView=null;else if(rc!=1)throw new InvalidOperationException("publish_view:"+rc);}
    if(pendingResult==null)m.Results.TryDequeue(out pendingResult);
    if(pendingResult!=null){int rc=host.PublishResult(pendingResult);if(rc==0)pendingResult=null;else if(rc!=1){log.WriteLine("expired-result rc="+rc);pendingResult=null;}}
    if(m.Commands.Count<64){int rc=host.PollOrder(buffer,out var size);if(rc==0){var bytes=new byte[size];Buffer.BlockCopy(buffer,0,bytes,0,(int)size);m.Commands.Enqueue(bytes);}else if(rc!=1)throw new InvalidOperationException("poll_order:"+rc);}
    Thread.Sleep(10);
   }
   log.WriteLine("worker-stop status="+host.Stop());host=null;
  }catch(Exception e){m.Failed=true;try{File.WriteAllText(Path.Combine(directory,"gateway-error.txt"),e.ToString());}catch{}}
  finally {if(host!=null)host.Stop();}
 }
 public static void AfterJobs(){instance?.Step();}
 public static void AfterUpdate(){if(GameTime.IsPaused())instance?.Step();}
 void Step(){
  if(mailbox.Failed||mailbox.Stop||Globals.mainThread!=Thread.CurrentThread||Time.realtimeSinceStartup<nextSample||!ObjectCalculationsJobScheduler.HasPreviousJobCompleted||!CollisionAvoidanceJobScheduler.HasPreviousJobCompleted)return;
  nextSample=Time.realtimeSinceStartup+.25f;
  try{
   for(int i=0;i<8&&mailbox.Commands.TryDequeue(out var bytes);++i)if(!provider.Enqueue(bytes))throw new InvalidOperationException("provider_backpressure");
   var view=provider.Tick();
   if(view!=null){while(mailbox.Views.TryDequeue(out _)){}mailbox.Views.Enqueue(view);}
   while(provider.Results.TryDequeue(out var status)){
    if(mailbox.Results.Count>=128)throw new InvalidOperationException("result_backpressure");mailbox.Results.Enqueue(status);
    var r=NavalCommand.Gateway.Wire.OrderResult.Parser.ParseFrom(status);
    if(mailbox.Events.Count<128)mailbox.Events.Enqueue("dispatch thread="+Thread.CurrentThread.ManagedThreadId+" request="+r.RequestId+" stage="+r.Stage+" reason="+r.Reason+" seconds="+r.SampledAtSeconds);
   }
  }catch(Exception e){mailbox.Failed=true;if(mailbox.Events.Count<128)mailbox.Events.Enqueue("provider_failed "+e);}
 }
 void Unloaded(Scene scene){if(scene.buildIndex!=2)return;provider.Invalidate();while(mailbox.Commands.TryDequeue(out _)){}while(mailbox.Results.TryDequeue(out _)){}while(mailbox.Views.TryDequeue(out _)){}if(provider.TryRead(out var invalid))mailbox.Views.Enqueue(invalid);mailbox.Events.Enqueue("mission-invalidated thread="+Thread.CurrentThread.ManagedThreadId);}
 void Stop(){Application.quitting-=Stop;SceneManager.sceneUnloaded-=Unloaded;harmony?.UnpatchSelf();provider?.Invalidate();mailbox.Stop=true;instance=null;/* No worker join on engine thread. Native module remains process-lifetime. */}
}
