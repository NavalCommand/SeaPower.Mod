using System;
using System.Threading;
using HarmonyLib;
using SeaPower;
using UnityEngine;
using UnityEngine.SceneManagement;
using NavalCommand.SeaPower.Client;

namespace NavalCommand.SeaPower.Mod;

public sealed class Runtime
{
    private const int MaximumQueuedCommands = 64;
    private const int MaximumRetainedResults = 128;
    private const int MaximumCommandsPerSample = 8;
    private const float SampleIntervalWallSeconds = 0.25f;
    readonly RuntimeMailbox mailbox = new();
    readonly NativeGameFacade gameFacade = new();
    Action<string> reportError;
    bool reportedFailure;
    Provider provider;
    Harmony harmony;
    static Runtime instance;
    float nextSampleWallSeconds;
    Thread worker;
    public void Install(RuntimeConfiguration configuration, Action<string> diagnostics)
    {
        if (instance == this)
        {
            return;
        }
        if (instance != null && !instance.mailbox.Completed)
        {
            throw new InvalidOperationException("runtime_already_running_or_stopping");
        }
        if (Globals.mainThread != Thread.CurrentThread)
        {
            throw new InvalidOperationException("engine_thread_required");
        }
        instance = this;
        reportError = diagnostics;
        harmony = new Harmony("dev.navalcommand.seapower.mod.scheduling");
        harmony.Patch(typeof(JobPerformer).GetMethod(nameof(JobPerformer.CompleteFixedUpdateJobs)),
            postfix: new HarmonyMethod(typeof(Runtime).GetMethod(nameof(AfterJobs))));
        harmony.Patch(AccessTools.Method(typeof(GameMain), "Update"), postfix: new HarmonyMethod(typeof(Runtime).GetMethod(nameof(AfterUpdate))));
        SceneManager.sceneUnloaded += Unloaded;
        Application.quitting += Stop;
        var workerMailbox = mailbox;
        worker = new Thread(() => TransportWorker.Run(workerMailbox, configuration))
        {
            IsBackground = true,
            Name = "SeaPower.Mod transport"
        };
        worker.Start();
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
        if (mailbox.Failed && !reportedFailure)
        {
            reportedFailure = true;
            reportError?.Invoke(mailbox.Error ?? "Provider failed; see runtime log.");
        }
        if (mailbox.Failed || mailbox.Stop || Globals.mainThread != Thread.CurrentThread || Time.realtimeSinceStartup < nextSampleWallSeconds
            || !ObjectCalculationsJobScheduler.HasPreviousJobCompleted || !CollisionAvoidanceJobScheduler.HasPreviousJobCompleted)
        {
            return;
        }

        nextSampleWallSeconds = Time.realtimeSinceStartup + SampleIntervalWallSeconds;
        try
        {
            if (provider == null)
            {
                if (!gameFacade.Ready || !Singleton<SceneCreator>.InstanceExists())
                {
                    return;
                }
                string playerSide = Singleton<SceneCreator>.Instance.PlayerSideName;
                if (string.IsNullOrWhiteSpace(playerSide))
                {
                    throw new InvalidOperationException("player_side_unavailable");
                }
                provider = new Provider(gameFacade, playerSide);
                mailbox.Events.Enqueue("player-side-bound " + playerSide);
            }
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
            mailbox.Error = exception.ToString();
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

        provider?.Invalidate();
        while (mailbox.Commands.TryDequeue(out _))
        {
        }

        while (mailbox.Results.TryDequeue(out _))
        {
        }

        while (mailbox.Views.TryDequeue(out _))
        {
        }

        if (provider != null && provider.TryRead(out var invalid))
        {
            mailbox.Views.Enqueue(invalid);
        }

        provider = null;
        mailbox.Events.Enqueue("mission-invalidated thread=" + Thread.CurrentThread.ManagedThreadId);
    }

    public void Stop()
    {
        if (mailbox.Stop)
        {
            return;
        }
        Application.quitting -= Stop;
        SceneManager.sceneUnloaded -= Unloaded;
        harmony?.UnpatchSelf();
        provider?.Invalidate();
        mailbox.Stop = true;
        provider = null;
        if (worker == null)
        {
            mailbox.Completed = true;
        }
        // The stopped singleton guards against overlapping hosts until off-thread shutdown completes.
        // Never join the worker or unload the native DLL in a Unity callback.
    }
}
