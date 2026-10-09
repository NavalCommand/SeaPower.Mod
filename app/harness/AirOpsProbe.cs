using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace NavalCommand.Task002;

// Disposable Task003 script, not an operational API or general command dispatcher.
public static class AirOpsProbe
{
    private sealed class Sortie
    {
        public ObjectBase Facility;
        public ObjectBase Unit;
        public double AirborneAt = -1;
        public bool Rtb;
        public bool Gone;
    }
    private static readonly List<Sortie> sorties = new();
    private static readonly Dictionary<string, string> last = new();
    private static Action<string> emit;
    private static Harmony harmony;
    private static bool requested;
    private static bool paused;
    private static float pauseWall;
    private static long pauseTick;
    private static float nextSample;

    public static void Install(Action<string> sink)
    {
        emit = sink; requested = false; paused = false; sorties.Clear(); last.Clear(); nextSample = 0;
        harmony = new Harmony("dev.navalcommand.task003.airops");
        harmony.Patch(AccessTools.Method(typeof(FlightDeck), "createLaunchTask"),
            prefix: new HarmonyMethod(typeof(AirOpsProbe), nameof(LaunchPrefix)),
            postfix: new HarmonyMethod(typeof(AirOpsProbe), nameof(LaunchPostfix)));
        harmony.Patch(AccessTools.Method(typeof(JobPerformer), "CompleteFixedUpdateJobs"),
            postfix: new HarmonyMethod(typeof(AirOpsProbe), nameof(AfterJobs)));
        emit("AIROPS installed thread=" + Thread.CurrentThread.ManagedThreadId);
    }
    public static void Remove() { harmony?.UnpatchSelf(); harmony = null; sorties.Clear(); last.Clear(); }
    private static bool Main() => Globals.mainThread == Thread.CurrentThread;
    private static bool Own(ObjectBase o) => o != null && o._taskforce == Globals._playerTaskforce;
    private static void Log(ObjectBase facility, string value)
    {
        if (!Main() || facility == null) return;
        emit((Own(facility) ? "AIROPS " : " PRIVILEGED AIROPS ") + "t=" + GameTime.missionElapsedTime +
            " thread=" + Thread.CurrentThread.ManagedThreadId + " facility=" + facility.UniqueID + " " + value);
    }
    private static void Changed(ObjectBase facility, string key, string value)
    {
        key = facility.UniqueID + ":" + key;
        if (!last.TryGetValue(key, out var old) || old != value) { last[key] = value; Log(facility, value); }
    }
    private static void LaunchPrefix(FlightDeck __instance, VehicleTypeOnBoard vehicle, Loadout loadout, LaunchTaskParameters ltp)
    {
        if (!Main()) return;
        // Stack contains method names only. Nonplayer evidence is privileged.
        var stack = new System.Diagnostics.StackTrace().GetFrames();
        var callers = string.Join(">", (stack ?? Array.Empty<System.Diagnostics.StackFrame>()).Take(12)
            .Select(f => f.GetMethod()?.DeclaringType?.Name + "." + f.GetMethod()?.Name));
        Log(__instance._baseObject, "request aircraft=" + vehicle?._fileName + " loadout=" + loadout?.Name +
            " count=" + ltp?._launchCount + " mission=" + ltp?._missionType + " strategicForcedOff=" +
            AIPlayer.IsForcedToDisable + " deckAiDisabled=" + __instance._aiDisabled + " callers=" + callers);
    }
    private static void LaunchPostfix(FlightDeck __instance, PendingLaunchTask __result)
    {
        if (!Main()) return;
        Log(__instance._baseObject, "accepted=" + (__result != null) + " task=" + __result?._uid);
        if (__result == null) return;
        __result.Launched += (_, unit) =>
        {
            Log(__instance._baseObject, "spawn-callback task=" + __result._uid + " unit=" + unit?.UniqueID +
                " inFlight=" + InFlight(unit) + " deckState=" + unit?._flightDeckState);
            if (Own(__instance._baseObject) && unit != null && sorties.Count < 16)
                sorties.Add(new Sortie { Facility = __instance._baseObject, Unit = unit });
        };
    }
    private static bool InFlight(ObjectBase o) => o is Aircraft a ? a._isInFlight : o is Helicopter h && h._isInFlight;
    public static void Update() => Step(false);
    public static void AfterJobs() => Step(true);
    private static void Step(bool fromJobHook)
    {
        if (!Main() || !Singleton<GameMain>.InstanceExists() || !Singleton<GameMain>.Instance._initialized ||
            Singleton<GameMain>.Instance._closing || !Singleton<SceneCreator>.Instance.IsLoadingDone ||
            Singleton<LoadScreen>.Instance.cameraObject.enabled || Globals._playerTaskforce == null) return;
        if ((fromJobHook || GameTime.IsPaused()) && Time.realtimeSinceStartup >= nextSample && ObjectCalculationsJobScheduler.HasPreviousJobCompleted &&
            CollisionAvoidanceJobScheduler.HasPreviousJobCompleted)
        {
            nextSample = Time.realtimeSinceStartup + 0.5f;
            foreach (var tf in new[] { Globals._playerTaskforce, Globals._enemyTaskforce })
            {
                if (tf == null) continue;
                foreach (var facility in tf._taskforceUnits.Where(o => o != null && o._obp?._flightDeck != null).Take(8).ToArray())
                    Sample(facility);
            }
            foreach (var sortie in sorties.ToArray())
            {
                if (sortie.Unit == null || sortie.Unit.IsDestroyed)
                {
                    if (!sortie.Gone) { sortie.Gone = true; Log(sortie.Facility, "sortie-object-gone rtbRequested=" + sortie.Rtb); }
                    continue;
                }
                var unit = sortie.Unit;
                Changed(sortie.Facility, "unit-" + unit.UniqueID, "unit=" + unit.UniqueID + " inFlight=" + InFlight(unit) +
                    " deckState=" + unit._flightDeckState + " unitState=" + unit.StateMachine?.CurrentState?.GetType().Name + " order=" + unit.CurrentOrder.Value.OrderType);
                if (InFlight(unit) && string.IsNullOrEmpty(unit._flightDeckState) && sortie.AirborneAt < 0)
                { sortie.AirborneAt = GameTime.missionElapsedTime; Log(sortie.Facility, "airborne-confirmed unit=" + unit.UniqueID + " ordinaryMember=" + Globals._playerTaskforce._taskforceUnits.Contains(unit)); }
                if (!sortie.Rtb && sortie.AirborneAt >= 0 && GameTime.missionElapsedTime > sortie.AirborneAt + 15 &&
                    Own(unit) && unit.HasControl && !GameTime.IsPaused() && !(unit.StateMachine?.CurrentState is AircraftStates.AdvancedMissileEvasion))
                { sortie.Rtb = true; unit.setOrder(Order.Type.ReturnToBase, sortie.Facility, displayOrderText: false); Log(sortie.Facility, "rtb-request unit=" + unit.UniqueID); }
            }
        }
        if (fromJobHook) return;
        if (!requested && GameTime.missionElapsedTime >= 8 && !GameTime.IsPaused())
        {
            requested = true;
            GameTime.Pause(); paused = true; pauseWall = Time.realtimeSinceStartup; pauseTick = GameTime.simulationTick;
            foreach (var facility in Globals._playerTaskforce._taskforceUnits.Where(o => o != null && o._obp?._flightDeck != null).Take(8).ToArray())
                Request(facility);
        }
        if (paused && Time.realtimeSinceStartup > pauseWall + 1)
        {
            emit("AIROPS pause-check unchangedTick=" + (pauseTick == GameTime.simulationTick) + " paused=" + GameTime.IsPaused());
            paused = false;
            // Disposable test owns this pause; no general player/menu pause lease is claimed.
            if (GameTime.IsPaused()) { GameTime.UnPause(); GameTime.SetTimeCompression(5); emit("AIROPS owned-test-resume compression=" + GameTime.TimeCompression); }
        }
    }
    private static void Sample(ObjectBase facility)
    {
        var d = facility._obp._flightDeck;
        Changed(facility, "facility", "type=" + facility._obp._objectIniName + " own=" + Own(facility) + " control=" + facility.HasControl +
            " playerOrders=" + facility.AcceptsOrdersFromPlayer + " deckAiDisabled=" + d._aiDisabled + " inoperable=" + d.Inoperable.Value +
            " maxCapacity=" + d.MaxAircraftOnBoard + " onboard=" + d.AircraftCurrentlyOnBoard + " readyCount=" + d.TotalVehiclesOnBoard() +
            " crews=" + d.GroundCrewCount + " deckSlots=" + d.DeckParkSlots + " ammo=" + d._currentAmmo + " taskCount=" + d.FlightDeckTasks.Count +
            " landingQueue=" + d._landingQueue.Count + " holdingQueue=" + d._holdingQueue.Count + " timingMode=" + Singleton<OptionsManager>.Instance.FlightDeckTimingsMode);
        foreach (var v in d._vehiclesOnBoard.Where(v => v.Numbers > 0 || d.FlightDeckTasks.Any(t => t.VehicleType == v)).Take(12))
        {
            Changed(facility, "inventory-" + v._fileName, "inventory aircraft=" + v._fileName + " numbers=" + v.Numbers +
                " squads=" + string.Join(",", v.Squadrons.Select(s => s._name + ":" + s.Numbers)));
        }
        foreach (var task in d.FlightDeckTasks.Take(24))
            Changed(facility, "task-" + task._uid, "task=" + task._uid + " kind=" + task.GetType().Name +
                " state=" + task._stateMachine?.CurrentState?.GetType().Name + " unit=" + task.Vehicle?.UniqueID +
                " remainingWork=" + Math.Round(task._duration, 1) + " launchAllowed=" + (task is PendingLaunchTask p ? p.launchAllowed.ToString() : "n/a"));
    }
    private static void Request(ObjectBase facility)
    {
        var d = facility._obp._flightDeck;
        if (!Own(facility) || !facility.HasControl || !facility.AcceptsOrdersFromPlayer || d.Inoperable.Value) { Log(facility, "request-rejected authority/inoperable"); return; }
        var v = d._vehiclesOnBoard.FirstOrDefault(v => v.Numbers >= 1 && v.Squadrons.Any(s => s.Numbers >= 1 && s.Callsigns.Count > 0));
        var squad = v?.Squadrons.FirstOrDefault(s => s.Numbers >= 1 && s.Callsigns.Count > 0);
        var load = v?.Loadouts.FirstOrDefault(l => d.CanLaunchVehicle(l, 1) && l.Name != "None");
        if (load == null || squad == null) { Log(facility, "request-rejected inventory/loadout"); return; }
        Log(facility, "selected aircraft=" + v._fileName + " loadout=" + load.Name + " nominalReadyWork=" + load._readyUpDuration +
            " ammoCost=" + load._ammoPoints + " pause=" + GameTime.IsPaused() + " tick=" + GameTime.simulationTick);
        var before = v.Numbers; var ammoBefore = d._currentAmmo;
        var cancel = d.createLaunchTask(v, load, squad, squad.Callsigns[0], new LaunchTaskParameters(), allowLaunch: false);
        if (cancel != null)
        {
            cancel.PrioritiseTaskFunc(null); cancel.ToggleLaunchAllowedFunc(null); cancel.ToggleLaunchAllowedFunc(null);
            d.abortLaunchTask(cancel._uid);
            Log(facility, "cancel-check inventoryRestored=" + (v.Numbers == before) + " ammoRestored=" + (d._currentAmmo == ammoBefore));
        }
        d.createLaunchTask(v, load, squad, squad.Callsigns[0], new LaunchTaskParameters());
    }
}
