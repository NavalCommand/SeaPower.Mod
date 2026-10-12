using System;
using System.Threading;
using Google.Protobuf;
using HarmonyLib;
using SeaPower;
using UnityEngine;
using UnityEngine.SceneManagement;
using NavalCommand.SeaPower.Client;

namespace NavalCommand.SeaPower.Mod;

public sealed class Runtime
{
    const float SampleIntervalWallSeconds = 0.25f;
    const int MaximumControlsPerStep = 64;
    readonly RuntimeMailbox mailbox = new();
    readonly NativeGameFacade game = new();
    Provider provider;
    Harmony harmony;
    Action<string> reportError;
    bool stepping;
    bool releasedAfterFailure;
    float nextSampleWallSeconds;
    static Runtime instance;

    public void Install(RuntimeConfiguration configuration, Action<string> diagnostics)
    {
        if (Globals.mainThread != Thread.CurrentThread)
        {
            throw new InvalidOperationException("engine_thread_required");
        }
        if (instance != null && !instance.mailbox.Completed)
        {
            throw new InvalidOperationException("runtime_already_running_or_stopping");
        }
        instance = this;
        reportError = diagnostics;
        harmony = new Harmony("dev.navalcommand.seapower.mod.runtime");
        harmony.Patch(AccessTools.Method(typeof(JobPerformer), nameof(JobPerformer.CompleteFixedUpdateJobs)),
            postfix: new HarmonyMethod(typeof(Runtime), nameof(AfterJobs)));
        harmony.Patch(AccessTools.Method(typeof(GameMain), "Update"), postfix: new HarmonyMethod(typeof(Runtime), nameof(AfterUpdate)));
        harmony.Patch(AccessTools.Method(typeof(GameTime), nameof(GameTime.Pause)), prefix: new HarmonyMethod(typeof(Runtime), nameof(BeforeNativePause)));
        harmony.Patch(AccessTools.Method(typeof(GameTime), nameof(GameTime.UnPause)), prefix: new HarmonyMethod(typeof(Runtime), nameof(BeforeNativeUnpause)));
        harmony.Patch(AccessTools.PropertySetter(typeof(GameTime), nameof(GameTime.TimeCompression)),
            prefix: new HarmonyMethod(typeof(Runtime), nameof(BeforeNativeCompression)));
        foreach (string method in new[] { "SetTimeCompression", "IncreaseTimeCompression", "DecreaseTimeCompression", "SetMaxPhysicsTimeCompression", "SetMinTimeCompression", "SetMaxTimeCompression", "StopTimeCompression" })
        {
            harmony.Patch(AccessTools.Method(typeof(GameTime), method), postfix: new HarmonyMethod(typeof(Runtime), nameof(AfterNativeCompressionSelection)));
        }
        SceneManager.sceneUnloaded += Unloaded;
        Application.quitting += Stop;
        var workerMailbox = mailbox;
        new Thread(() => TransportWorker.Run(workerMailbox, configuration))
        {
            IsBackground = true, Name = "SeaPower.Mod transport"
        }.Start();
    }

    public static bool BeforeNativePause()
    {
        var runtime = instance;
        if (runtime?.provider == null || runtime.game.ApplyingControl)
        {
            return true;
        }
        runtime.provider.ObservePlayerPause(true);
        // Repeated native Pause() invokes EnforceFixedUpdate; retaining a claim
        // while already effectively paused must not advance a simulation tick.
        return !runtime.provider.HasExternalPauseOwners || !GameTime.IsPaused();
    }

    public static bool BeforeNativeUnpause()
    {
        var runtime = instance;
        if (runtime?.provider == null || runtime.game.ApplyingControl)
        {
            return true;
        }
        runtime.provider.ObservePlayerPause(false);
        return !runtime.provider.EffectivePause;
    }

    public static void BeforeNativeCompression(ref float value)
    {
        var runtime = instance;
        if (runtime?.provider == null || runtime.game.ApplyingControl)
        {
            return;
        }
        if (value > 0.05f)
        {
            if (runtime.provider.HasExternalPauseOwners)
            {
                runtime.game.ObserveNativeCompression(value);
            }
            runtime.provider.ObservePlayerCompression(value);
            if (runtime.provider.EffectivePause)
            {
                value = 0;
            }
        }
        else
        {
            runtime.provider.ObservePlayerPause(true);
        }
    }

    public static void AfterNativeCompressionSelection()
    {
        var runtime = instance;
        if (runtime?.provider == null || runtime.game.ApplyingControl || !runtime.provider.EffectivePause)
        {
            return;
        }
        // Native selectors unmute environment audio before assigning compression.
        // Preserve native mute preference when another owner still holds pause.
        if (Singleton<OptionsManager>.InstanceExists() && Singleton<OptionsManager>.Instance.MuteSoundInPause
            && Singleton<EnvironmentAudioManager>.InstanceExists())
        {
            Singleton<EnvironmentAudioManager>.Instance.toggleMute(true);
        }
    }

    public static void AfterJobs()
    {
        instance?.Step();
    }

    public static void AfterUpdate()
    {
        // A pause applied in the completion postfix can be followed by the
        // remainder of the native fixed update scheduling its final jobs.
        // No further fixed update runs at timeScale zero. Complete those jobs
        // on the engine thread without enforcing another simulation tick.
        if (instance != null && !instance.stepping && !instance.mailbox.Stop
            && Globals.mainThread == Thread.CurrentThread && GameTime.IsPaused()
            && (!ObjectCalculationsJobScheduler.HasPreviousJobCompleted || !CollisionAvoidanceJobScheduler.HasPreviousJobCompleted))
        {
            JobPerformer.CompleteFixedUpdateJobs();
        }
        instance?.Step();
    }

    void Step()
    {
        if (stepping || mailbox.Stop || Globals.mainThread != Thread.CurrentThread
            || !ObjectCalculationsJobScheduler.HasPreviousJobCompleted || !CollisionAvoidanceJobScheduler.HasPreviousJobCompleted)
        {
            return;
        }
        stepping = true;
        try
        {
            if (mailbox.Failed)
            {
                if (!releasedAfterFailure)
                {
                    releasedAfterFailure = true;
                    provider?.Invalidate();
                    provider = null;
                    harmony?.UnpatchSelf();
                    reportError?.Invoke(mailbox.Error ?? "Gateway failed.");
                }
                return;
            }
            if (provider == null)
            {
                if (!game.Ready)
                {
                    return;
                }
                provider = new Provider(game);
            }
            provider.Synchronize();
            // Controls, including disconnect releases, drain at each safe callback.
            // They are never delayed by the wall-clock snapshot polling interval.
            for (int index = 0; index < MaximumControlsPerStep && mailbox.Commands.TryDequeue(out var bytes); ++index)
            {
                var result = provider.Apply(bytes);
                if (result.Request.Length != 0)
                {
                    if (mailbox.Results.Count >= MaximumControlsPerStep)
                    {
                        throw new InvalidOperationException("result_resource_exhausted");
                    }
                    mailbox.Results.Enqueue(result.ToByteArray());
                }
            }
            if (Time.realtimeSinceStartup >= nextSampleWallSeconds)
            {
                nextSampleWallSeconds = Time.realtimeSinceStartup + SampleIntervalWallSeconds;
                var publication = provider.Capture();
                while (mailbox.Views.TryDequeue(out _))
                {
                }
                mailbox.Views.Enqueue(publication);
            }
        }
        catch (Exception exception)
        {
            mailbox.Error = exception.ToString();
            mailbox.Failed = true;
            provider?.Invalidate();
            provider = null;
            harmony?.UnpatchSelf();
            releasedAfterFailure = true;
            reportError?.Invoke(mailbox.Error);
        }
        finally
        {
            stepping = false;
        }
    }

    void Unloaded(Scene scene)
    {
        if (scene.buildIndex != 2)
        {
            return;
        }
        provider?.Invalidate();
        provider = null;
        while (mailbox.Views.TryDequeue(out _))
        {
        }
        mailbox.Views.Enqueue(new NavalCommand.Gateway.Wire.Publication().ToByteArray());
    }

    public void Stop()
    {
        if (mailbox.Stop)
        {
            return;
        }
        Application.quitting -= Stop;
        SceneManager.sceneUnloaded -= Unloaded;
        provider?.Invalidate();
        provider = null;
        harmony?.UnpatchSelf();
        mailbox.Stop = true;
        // Worker performs native shutdown; never join it in a Unity callback.
    }
}
