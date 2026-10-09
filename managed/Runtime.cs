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

public sealed class Runtime
{
    private const int MaximumQueuedCommands = 64;
    private const int MaximumRetainedResults = 128;
    private const int MaximumCommandsPerSample = 8;
    private const int MaximumOrderBytes = 65_536;
    private const int WorkerPollMilliseconds = 10;
    private const float SampleIntervalWallSeconds = 0.25f;
    // Worker receives only plain queues/state; it never closes over this runtime.
    sealed class Mailbox
    {
        public readonly ConcurrentQueue<byte[]> Views = new();
        public readonly ConcurrentQueue<byte[]> Results = new();
        public readonly ConcurrentQueue<byte[]> Commands = new();
        public readonly ConcurrentQueue<string> Events = new();
        public volatile bool Stop;
        public volatile bool Failed;
    }

    readonly Mailbox mailbox = new();
    Provider provider;
    Harmony harmony;
    static Runtime instance;
    float nextSampleWallSeconds;
    Thread worker;
    public void Install()
    {
        instance = this;
        provider = new Provider(new NativeGameFacade(), System.Environment.GetEnvironmentVariable("NC_TASK002_SIDE") ?? "Taskforce1");
        harmony = new Harmony("dev.navalcommand.task004.provider");
        harmony.Patch(typeof(JobPerformer).GetMethod(nameof(JobPerformer.CompleteFixedUpdateJobs)),
            postfix: new HarmonyMethod(typeof(Runtime).GetMethod(nameof(AfterJobs))));
        harmony.Patch(AccessTools.Method(typeof(GameMain), "Update"), postfix: new HarmonyMethod(typeof(Runtime).GetMethod(nameof(AfterUpdate))));
        SceneManager.sceneUnloaded += Unloaded;
        Application.quitting += Stop;
        string runDirectory = System.Environment.GetEnvironmentVariable("NC_TASK002_RUN_DIR");
        string nativeLibraryPath = System.Environment.GetEnvironmentVariable("NC_TASK004_NATIVE_DLL");
        string endpoint = System.Environment.GetEnvironmentVariable("NC_TASK004_ENDPOINT") ?? "127.0.0.1:50051";
        var workerMailbox = mailbox;
        worker = new Thread(() => Run(workerMailbox, runDirectory, nativeLibraryPath, endpoint))
        {
            IsBackground = true,
            Name = "SeaPower.Mod transport"
        };
        worker.Start();
    }

    static void Run(Mailbox workerMailbox, string runDirectory, string nativeLibraryPath, string endpoint)
    {
        NativeTransport nativeTransport = null;
        try
        {
            using var log = new StreamWriter(Path.Combine(runDirectory, "gateway.log"), false)
            {
                AutoFlush = true
            };
            nativeTransport = new NativeTransport(nativeLibraryPath);
            int started = nativeTransport.Start(endpoint);
            log.WriteLine(DateTime.UtcNow.ToString("o") + " native-start abi=1 status=" + started);
            if (started != 0)
            {
                throw new InvalidOperationException("host_start_failed:" + started);
            }

            File.WriteAllText(Path.Combine(runDirectory, "gateway-ready.json"), "{\"abi\":1,\"endpoint\":\"" + endpoint + "\"}");
            var buffer = new byte[MaximumOrderBytes];
            byte[] pendingViewBytes = null;
            byte[] pendingResultBytes = null;
            while (!workerMailbox.Stop)
            {
                if (workerMailbox.Failed)
                {
                    throw new InvalidOperationException("provider_failed");
                }

                while (workerMailbox.Events.TryDequeue(out var line))
                {
                    log.WriteLine(DateTime.UtcNow.ToString("o") + " " + line);
                }

                while (workerMailbox.Views.TryDequeue(out var viewBytes))
                {
                    pendingViewBytes = viewBytes;
                }

                if (pendingViewBytes != null)
                {
                    int statusCode = nativeTransport.PublishView(pendingViewBytes);
                    if (statusCode == 0)
                    {
                        pendingViewBytes = null;
                    }
                    else if (statusCode != 1)
                    {
                        throw new InvalidOperationException("publish_view:" + statusCode);
                    }
                }

                if (pendingResultBytes == null)
                {
                    workerMailbox.Results.TryDequeue(out pendingResultBytes);
                }

                if (pendingResultBytes != null)
                {
                    int statusCode = nativeTransport.PublishResult(pendingResultBytes);
                    if (statusCode == 0)
                    {
                        pendingResultBytes = null;
                    }
                    else if (statusCode != 1)
                    {
                        log.WriteLine("expired-result rc=" + statusCode);
                        pendingResultBytes = null;
                    }
                }

                if (workerMailbox.Commands.Count < MaximumQueuedCommands)
                {
                    int statusCode = nativeTransport.PollOrder(buffer, out var size);
                    if (statusCode == 0)
                    {
                        var bytes = new byte[size];
                        Buffer.BlockCopy(buffer, 0, bytes, 0, (int)size);
                        workerMailbox.Commands.Enqueue(bytes);
                    }
                    else if (statusCode != 1)
                    {
                        throw new InvalidOperationException("poll_order:" + statusCode);
                    }
                }

                Thread.Sleep(WorkerPollMilliseconds);
            }

            log.WriteLine("worker-stop status=" + nativeTransport.Stop());
            nativeTransport = null;
        }
        catch (Exception exception)
        {
            workerMailbox.Failed = true;
            try
            {
                File.WriteAllText(Path.Combine(runDirectory, "gateway-error.txt"), exception.ToString());
            }
            catch
            {
            }
        }
        finally
        {
            if (nativeTransport != null)
            {
                nativeTransport.Stop();
            }
        }
    }

    public static void AfterJobs()
    {
        instance?.Step();
    }

    public static void AfterUpdate()
    {
        if (GameTime.IsPaused())
        {
            instance?.Step();
        }
    }

    void Step()
    {
        if (mailbox.Failed || mailbox.Stop || Globals.mainThread != Thread.CurrentThread || Time.realtimeSinceStartup < nextSampleWallSeconds
            || !ObjectCalculationsJobScheduler.HasPreviousJobCompleted || !CollisionAvoidanceJobScheduler.HasPreviousJobCompleted)
        {
            return;
        }

        nextSampleWallSeconds = Time.realtimeSinceStartup + SampleIntervalWallSeconds;
        try
        {
            for (int commandIndex = 0; commandIndex < MaximumCommandsPerSample && mailbox.Commands.TryDequeue(out var bytes); ++commandIndex)
            {
                if (!provider.Enqueue(bytes))
                {
                    throw new InvalidOperationException("provider_backpressure");
                }
            }

            var view = provider.Tick();
            if (view != null)
            {
                while (mailbox.Views.TryDequeue(out _))
                {
                }

                mailbox.Views.Enqueue(view);
            }

            while (provider.Results.TryDequeue(out var status))
            {
                if (mailbox.Results.Count >= MaximumRetainedResults)
                {
                    throw new InvalidOperationException("result_backpressure");
                }

                mailbox.Results.Enqueue(status);
                var orderResult = NavalCommand.Gateway.Wire.OrderResult.Parser.ParseFrom(status);
                if (mailbox.Events.Count < MaximumRetainedResults)
                {
                    mailbox.Events.Enqueue("dispatch thread=" + Thread.CurrentThread.ManagedThreadId + " request=" + orderResult.RequestId
                        + " stage=" + orderResult.Stage + " reason=" + orderResult.Reason + " seconds=" + orderResult.SampledAtSeconds);
                }
            }
        }
        catch (Exception exception)
        {
            mailbox.Failed = true;
            if (mailbox.Events.Count < MaximumRetainedResults)
            {
                mailbox.Events.Enqueue("provider_failed " + exception);
            }
        }
    }

    void Unloaded(Scene scene)
    {
        if (scene.buildIndex != 2)
        {
            return;
        }

        provider.Invalidate();
        while (mailbox.Commands.TryDequeue(out _))
        {
        }

        while (mailbox.Results.TryDequeue(out _))
        {
        }

        while (mailbox.Views.TryDequeue(out _))
        {
        }

        if (provider.TryRead(out var invalid))
        {
            mailbox.Views.Enqueue(invalid);
        }

        mailbox.Events.Enqueue("mission-invalidated thread=" + Thread.CurrentThread.ManagedThreadId);
    }

    void Stop()
    {
        Application.quitting -= Stop;
        SceneManager.sceneUnloaded -= Unloaded;
        harmony?.UnpatchSelf();
        provider?.Invalidate();
        mailbox.Stop = true;
        instance = null; /* No worker join on engine thread. Native module remains process-lifetime. */
    }
}
