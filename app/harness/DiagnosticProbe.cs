using System;
using System.Linq;
using System.Threading;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace NavalCommand.Task002;

// Disposable scripted experiment; not an API. All callbacks below run on Unity's thread.
public static class DiagnosticProbe
{
    private static Action<string> emit;
    private static Harmony harmony;
    private static double nextSample;
    private static GoToWaypointTask playerTask, aiTask;
    private static bool playerIssued, aiIssued;
    private static GeoPosition playerOrigin, aiOrigin;
    private static long pauseRevision;
    private static long leaseRevision;
    private static bool adapterIntent;
    private static int pauseStage;
    private static float pauseWall;
    private static long pauseTick;
    private static bool observationOnly;
    private static int generation;

    public static void Install(Action<string> sink)
    {
        emit = sink;
        observationOnly = System.Environment.GetEnvironmentVariable("NC_TASK002_OBSERVE_ONLY") == "1";
        harmony = new Harmony("dev.navalcommand.task002.diagnostics");
        harmony.Patch(typeof(JobPerformer).GetMethod(nameof(JobPerformer.CompleteFixedUpdateJobs)),
            postfix: new HarmonyMethod(typeof(DiagnosticProbe).GetMethod(nameof(AfterJobs))));
        harmony.Patch(typeof(GameTime).GetMethod(nameof(GameTime.Pause)),
            prefix: new HarmonyMethod(typeof(DiagnosticProbe).GetMethod(nameof(PauseIntent))));
        harmony.Patch(typeof(AircraftStates.AdvancedMissileEvasion).GetMethod("onEnter"),
            postfix: new HarmonyMethod(typeof(DiagnosticProbe).GetMethod(nameof(EvasionEnter))));
        harmony.Patch(typeof(AircraftStates.AdvancedMissileEvasion).GetMethod("onExit"),
            postfix: new HarmonyMethod(typeof(DiagnosticProbe).GetMethod(nameof(EvasionExit))));
        emit("diagnostics-installed mainThread=" + Thread.CurrentThread.ManagedThreadId);
    }

    public static void PauseIntent() { ++pauseRevision; emit?.Invoke("pause-intent origin=" + (adapterIntent ? "adapter" : "external-native-call") + " revision=" + pauseRevision); }

    public static void AfterJobs()
    {
        if (Globals.mainThread != Thread.CurrentThread) { emit("failed-hook-thread"); return; }
        // Native Complete can return while a nonblocking scheduler remains pending.
        if (!ObjectCalculationsJobScheduler.HasPreviousJobCompleted ||
            !CollisionAvoidanceJobScheduler.HasPreviousJobCompleted) return;
        if (!Singleton<GameMain>.InstanceExists() || !Singleton<GameMain>.Instance._initialized ||
            Singleton<GameMain>.Instance._closing || !Singleton<LoadScreen>.InstanceExists() ||
            Singleton<LoadScreen>.Instance.cameraObject.enabled || Globals._playerTaskforce == null ||
            Globals._enemyTaskforce == null) return;
        double time = GameTime.missionElapsedTime;
        if (time < 8 || time < nextSample) return;
        nextSample = time + 5;
        emit("safe-sample thread=" + Thread.CurrentThread.ManagedThreadId + " tick=" + GameTime.simulationTick
            + " objectJobsComplete=true collisionJobsComplete=true missionTime=" + time);
        Observe(Globals._playerTaskforce, "player");
        Observe(Globals._enemyTaskforce, "ai");
        foreach (var own in Globals._playerTaskforce._taskforceUnits)
            if (own is Aircraft)
                emit("own-aircraft state=" + own.StateMachine.CurrentState.GetType().Name + " routeCount="
                    + own._userRoot.TaskViewModels.Count + " speedKnots=" + own._velocityInKnots
                    + " desiredAltitudeNative=" + own.DesiredAltitude.Value);
        if (observationOnly) return;
        var player = Globals._playerTaskforce._taskforceUnits.FirstOrDefault();
        var ai = Globals._enemyTaskforce._taskforceUnits.FirstOrDefault();
        if (player == null || ai == null) return;
        if (!playerIssued)
        {
            playerOrigin = player._geoPosition;
            playerTask = Issue(player, "player"); playerIssued = true;
        }
        else if (!aiIssued && time > 15)
        {
            aiOrigin = ai._geoPosition;
            emit("PRIVILEGED ai-control IsPlayerObject=" + ai.IsPlayerObject + " HasControl=" + ai.HasControl
                + " AcceptsOrdersFromAI=" + ai.AcceptsOrdersFromAI + " AIPlayerForcedDisable=" + AIPlayer.IsForcedToDisable);
            aiTask = Issue(ai, "ai"); aiIssued = true;
        }
        if (playerTask != null) ReportOrder(player, playerTask, playerOrigin, "player");
        if (aiTask != null) ReportOrder(ai, aiTask, aiOrigin, "ai");
    }

    private static void Observe(Taskforce faction, string label)
    {
        // Id is a side-local plotting-table key, never an ObjectBase UID. Export opaque indices.
        var contacts = faction.PlottingTable.Vehicles.Where(v => v.Id >= 7001).ToArray();
        emit("OBS side=" + label + " foreignTracks=" + contacts.Length);
        int index = 0;
        foreach (var contact in contacts)
        {
            var estimate = contact.DetectedPosition;
            emit("OBS side=" + label + " opaque=track-" + (++index) + " estimatePresent=" + estimate.HasValue
                + " cachedUpdateEpoch=" + contact.UpdateEpoch + " observedMissionTime=" + GameTime.missionElapsedTime
                + (estimate.HasValue ? " detectedLatitude=" + estimate.Value.Item1.Latitude
                    + " detectedLongitude=" + estimate.Value.Item1.Longitude + " majorStdDevNative="
                    + estimate.Value.Item2.MajorStdDev + " minorStdDevNative=" + estimate.Value.Item2.MinorStdDev : ""));
        }
        // No BaseObject, UnitTaskforce, name, UID, Velocity, Position fallback or destruction getter.
        // Snapshot is cached managed estimates; not an atomic ECS/sensor snapshot.
    }

    private static GoToWaypointTask Issue(ObjectBase unit, string label)
    {
        if (unit is Aircraft && unit.StateMachine.CurrentState is AircraftStates.AdvancedMissileEvasion)
        { emit("command-rejected reason=AdvancedMissileEvasion"); return null; }
        var pos = unit._geoPosition;
        var task = unit.SetWaypointTask(new WaypointData {
            _geoposition = new GeoPosition(pos.Latitude + 0.03, pos.Longitude, pos._height),
            _heightState = WaypointData.WaypointHeightState.Current,
            _setTelegraph = 2, _allowFormationManeuver = false, wpName = "Task002-" + label
        });
        task.ReachedWaypoint += (_, _) => emit("order-reached role=" + label);
        task.RemovedOrFinished += (_, _) => emit("order-removed-or-finished role=" + label);
        emit("command-accepted role=" + label + " taskInserted=" + unit._userRoot.TaskViewModels.Any(v => v.Task == task));
        return task;
    }

    public static void EvasionEnter(AircraftStates.AdvancedMissileEvasion __instance)
    {
        var aircraft = __instance._aircraft;
        if (!aircraft.IsPlayerObject) { emit("PRIVILEGED opponent-evasion-enter"); return; }
        emit("INCIDENT own-evasion-enter time=" + GameTime.missionElapsedTime + " routeCount="
            + aircraft._userRoot.TaskViewModels.Count + " speedKnots=" + aircraft._velocityInKnots
            + " desiredAltitudeNative=" + aircraft.DesiredAltitude.Value);
        if (System.Environment.GetEnvironmentVariable("NC_TASK002_INTERFERE") == "1")
        {
            aircraft.CeaseFire();
            emit("deliberate-interference CeaseFire IsFinished=" + __instance.IsFinished);
        }
        else
        {
            Issue(aircraft, "protected-own-aircraft");
            emit("guard-after-dispatch stillEvasion=" + (aircraft.StateMachine.CurrentState == __instance)
                + " IsFinished=" + __instance.IsFinished);
        }
    }

    public static void EvasionExit(AircraftStates.AdvancedMissileEvasion __instance)
    {
        if (!__instance._aircraft.IsPlayerObject) { emit("PRIVILEGED opponent-evasion-exit"); return; }
        emit("INCIDENT own-evasion-exit time=" + GameTime.missionElapsedTime + " routeCount="
            + __instance._aircraft._userRoot.TaskViewModels.Count + " desiredAltitudeNative="
            + __instance._aircraft.DesiredAltitude.Value);
    }

    private static void ReportOrder(ObjectBase unit, GoToWaypointTask task, GeoPosition origin, string label)
    {
        bool present = unit._userRoot.TaskViewModels.Any(v => v.Task == task);
        double distance = origin.GetDistance(unit._geoPosition);
        emit((label == "ai" ? "PRIVILEGED " : "") + "order-progress role=" + label + " retained=" + present
            + " distanceNative=" + distance + " telegraph=" + unit.getTelegraph()
            + " state=" + unit.StateMachine.CurrentState.GetType().Name);
    }

    // Update is deliberately the paused-mode path; never calls FixedUpdate or job completion.
    public static void UpdatePauseExperiment()
    {
        if (!aiIssued || GameTime.missionElapsedTime < 65 || !Singleton<GameMain>.InstanceExists() ||
            !Singleton<GameMain>.Instance._initialized || Singleton<GameMain>.Instance._closing) return;
        if (pauseStage == 0)
        {
            if (GameTime.IsPaused()) { emit("pause-acquire-refused already-paused"); pauseStage = 9; return; }
            adapterIntent = true; GameTime.Pause(); adapterIntent = false;
            leaseRevision = pauseRevision; pauseTick = GameTime.simulationTick; pauseWall = Time.realtimeSinceStartup;
            var unit = Globals._playerTaskforce._taskforceUnits.FirstOrDefault();
            if (unit != null) Issue(unit, "paused-player");
            emit("pause-owned nativeTick=" + pauseTick + " revision=" + leaseRevision);
            pauseStage = 1;
        }
        else if (pauseStage == 1 && Time.realtimeSinceStartup > pauseWall + 1)
        {
            emit("paused-dispatch-check tickUnchanged=" + (pauseTick == GameTime.simulationTick)
                + " stillPaused=" + GameTime.IsPaused());
            if (leaseRevision == pauseRevision) { GameTime.UnPause(); emit("pause-owned-released"); }
            pauseStage = 2;
        }
        else if (pauseStage == 2 && GameTime.missionElapsedTime > 70)
        {
            adapterIntent = true; GameTime.Pause(); adapterIntent = false;
            leaseRevision = pauseRevision;
            // Synthetic competing native pause intent, including the same-value case.
            GameTime.Pause();
            emit("synthetic-player-pause-intent revision=" + pauseRevision);
            if (leaseRevision != pauseRevision) emit("pause-release-refused competing-intent");
            if (GameTime.IsPaused()) emit("pause-acquire-refused already-paused");
            pauseStage = 9;
        }
    }

    public static void Remove()
    {
        harmony?.UnpatchSelf(); emit?.Invoke("diagnostics-unpatched"); emit = null;
    }

    public static void PrepareOwnedUnload()
    {
        adapterIntent = true; GameTime.Pause(); adapterIntent = false;
        leaseRevision = pauseRevision;
        emit("lifecycle-test owns-pause-before-unload revision=" + leaseRevision);
    }

    public static void InvalidateScene()
    {
        playerTask = null; aiTask = null; playerIssued = false; aiIssued = false;
        nextSample = 0; leaseRevision = -1; pauseStage = 0; ++generation;
        emit?.Invoke("scene-invalidated generation=" + generation + " nativeReferencesCleared=true pauseLeaseInvalidated=true");
    }
}
