using System;
using System.Collections.Concurrent;
using System.Threading;
using SeaPower;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NavalCommand.SeaPower.Harness;
// Opt-in development bootstrap. The approved loader calls Install on Unity's main thread.
// Readiness is deliberately narrow: a native player side, own units, and advancing time.
public sealed class MissionLauncher : MonoBehaviour
{
    private const float LaunchTimeoutWallSeconds = 180;
    private const int MaximumEvidencePhases = 32;
    private const int MenuSceneBuildIndex = 1;
    private const int MissionSceneBuildIndex = 2;
    private const float CompleteLoadPercent = 100;
    private const double ReadinessMissionTimeAdvanceSeconds = 1.0;
    private string mission = "";
    private string side = "";
    private int ownerThread;
    private bool requested;
    private bool finished;
    private float deadline;
    private double firstMissionTime = -1;
    private readonly ConcurrentQueue<string> phases = new();
    private int queued;
    // The caller prevalidates file existence, installed hash, native PlayerTaskforce,
    // fresh process, explicit opt-in and run token on an I/O worker BEFORE Install.
    public static MissionLauncher Install(string missionPath, string nativePlayerSide)
    {
        if (Globals.mainThread != Thread.CurrentThread)
        {
            throw new InvalidOperationException("Install requires verified Unity main thread");
        }

        if (string.IsNullOrWhiteSpace(missionPath) || string.IsNullOrWhiteSpace(nativePlayerSide))
        {
            throw new ArgumentException("Explicit mission and native player side required");
        }

        if (SceneManager.GetSceneByBuildIndex(MissionSceneBuildIndex).isLoaded)
        {
            throw new InvalidOperationException("Refusing an existing mission");
        }

        var host = new GameObject("NavalCommand.SeaPower.Harness.MissionLauncher");
        DontDestroyOnLoad(host);
        var launcher = host.AddComponent<MissionLauncher>();
        launcher.mission = missionPath;
        launcher.side = nativePlayerSide;
        launcher.ownerThread = Thread.CurrentThread.ManagedThreadId;
        launcher.deadline = Time.realtimeSinceStartup + LaunchTimeoutWallSeconds;
        launcher.Emit("scenario-launcher-installed");
        return launcher;
    }

    // Worker may drain only these immutable strings; never hand it native references.
    public bool TryReadPhase(out string phase)
    {
        if (phases.TryDequeue(out phase))
        {
            Interlocked.Decrement(ref queued);
            return true;
        }

        return false;
    }

    private void Emit(string phase)
    {
        if (Interlocked.Increment(ref queued) <= MaximumEvidencePhases)
        {
            phases.Enqueue(phase);
        }
        else
        {
            Interlocked.Decrement(ref queued);
        }
    }

    private void Update()
    {
        if (finished)
        {
            return;
        }

        if (Thread.CurrentThread.ManagedThreadId != ownerThread)
        {
            Emit("failed-thread");
            finished = true;
            return;
        }

        if (Time.realtimeSinceStartup > deadline)
        {
            Emit("failed-timeout");
            finished = true;
            return;
        }

        try
        {
            if (!requested)
            {
                // Wait for the inspected menu dependencies; this is an unofficial development hook.
                if (!SceneManager.GetSceneByBuildIndex(MenuSceneBuildIndex).isLoaded || !Singleton<BackgroundSceneController>.InstanceExists()
                    || !Singleton<LoadScreen>.InstanceExists() || !Singleton<SeapowerUI.ViewModels.MenuSystemViewModel>.InstanceExists())
                {
                    return;
                }

                if (Singleton<LoadScreen>.Instance.cameraObject.enabled || Singleton<LoadScreen>.Instance.LoadPercent < CompleteLoadPercent)
                {
                    return;
                }

                Globals.currentMissionFilePath = mission;
                requested = true;
                MissionManager.DoLoad();
                Emit("mission-load-requested");
                return;
            }

            if (!SceneManager.GetSceneByBuildIndex(MissionSceneBuildIndex).isLoaded || !Singleton<SceneCreator>.InstanceExists()
                || !Singleton<GameMain>.InstanceExists())
            {
                return;
            }

            if (!Singleton<SceneCreator>.Instance.IsLoadingDone || !Singleton<GameMain>.Instance._initialized)
            {
                return;
            }

            if (Singleton<SceneCreator>.Instance.PlayerSideName != side)
            {
                Emit("failed-side-mismatch");
                finished = true;
                return;
            }

            if (Singleton<LoadScreen>.Instance.cameraObject.enabled || Singleton<LoadScreen>.Instance.LoadPercent < CompleteLoadPercent
                || Singleton<SceneCreator>.Instance._pauseForBriefing || GameTime.IsPaused())
            {
                return;
            }

            // Readiness is checked on the verified engine thread after native initialization.
            if (Globals._playerTaskforce == null || Globals._playerTaskforce._taskforceUnits.Count == 0)
            {
                return;
            }

            bool controllableOwnUnit = false;
            foreach (var own in Globals._playerTaskforce._taskforceUnits)
            {
                if (own != null && own.IsControllable && own.HasControl && !own._geoPosition.IsInvalid())
                {
                    controllableOwnUnit = true;
                    break;
                }
            }

            if (!controllableOwnUnit)
            {
                return;
            }

            if (firstMissionTime < 0)
            {
                firstMissionTime = GameTime.missionElapsedTime;
                Emit("mission-loaded side=" + side + " ownUnits=" + Globals._playerTaskforce._taskforceUnits.Count);
                return;
            }

            if (GameTime.missionElapsedTime < firstMissionTime + ReadinessMissionTimeAdvanceSeconds)
            {
                return;
            }

            Emit("playable-ready side=" + side + " ownUnits=" + Globals._playerTaskforce._taskforceUnits.Count
                + " controllableOwnUnit=true missionTime=" + GameTime.missionElapsedTime);
            // Readiness never dismisses briefing, advances time or changes pause ownership.
            finished = true;
        }
        catch (Exception exception)
        {
            Emit("failed-" + exception.GetType().Name);
            finished = true;
        }
    }

    private void OnDestroy()
    {
        finished = true;
        Emit("scenario-launcher-destroyed");
    }
}
