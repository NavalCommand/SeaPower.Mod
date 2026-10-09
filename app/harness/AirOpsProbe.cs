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
    private const int MaximumTrackedSorties = 16;
    private const int MaximumCallerFrames = 12;
    private const int MaximumFacilitySamples = 8;
    private const int MaximumVehicleTypeSamples = 12;
    private const int MaximumDeckTaskSamples = 24;
    private const double RequestAfterOperationalSeconds = 8;
    private const double ReturnToBaseDelayOperationalSeconds = 15;
    private const float SampleIntervalWallSeconds = 0.5f;
    private const float OwnedPauseDurationWallSeconds = 1;
    private const int TestTimeCompression = 5;
    private sealed class Sortie
    {
        public ObjectBase Facility;
        public ObjectBase Unit;
        public double AirborneAt = -1;
        public bool Rtb;
        public bool Gone;
    }

    private static readonly List<Sortie> sorties = new();
    private static readonly Dictionary<string, string> lastSampleByKey = new();
    private static Action<string> emit;
    private static Harmony harmony;
    private static bool requested;
    private static bool paused;
    private static float pauseWall;
    private static long pauseTick;
    private static float nextSample;
    public static void Install(Action<string> sink)
    {
        emit = sink;
        requested = false;
        paused = false;
        sorties.Clear();
        lastSampleByKey.Clear();
        nextSample = 0;
        harmony = new Harmony("dev.navalcommand.task003.airops");
        harmony.Patch(AccessTools.Method(typeof(FlightDeck), "createLaunchTask"), prefix: new HarmonyMethod(typeof(AirOpsProbe), nameof(LaunchPrefix)),
            postfix: new HarmonyMethod(typeof(AirOpsProbe), nameof(LaunchPostfix)));
        harmony.Patch(AccessTools.Method(typeof(JobPerformer), "CompleteFixedUpdateJobs"), postfix: new HarmonyMethod(typeof(AirOpsProbe), nameof(AfterJobs)));
        emit("AIROPS installed thread=" + Thread.CurrentThread.ManagedThreadId);
    }

    public static void Remove()
    {
        harmony?.UnpatchSelf();
        harmony = null;
        sorties.Clear();
        lastSampleByKey.Clear();
    }

    private static bool Main() => Globals.mainThread == Thread.CurrentThread;
    private static bool Own(ObjectBase nativeUnit) => nativeUnit != null && nativeUnit._taskforce == Globals._playerTaskforce;
    private static void Log(ObjectBase facility, string value)
    {
        if (!Main() || facility == null)
        {
            return;
        }

        emit((Own(facility) ? "AIROPS " : " PRIVILEGED AIROPS ") + "t=" + GameTime.missionElapsedTime + " thread="
            + Thread.CurrentThread.ManagedThreadId + " facility=" + facility.UniqueID + " " + value);
    }

    private static void Changed(ObjectBase facility, string key, string value)
    {
        key = facility.UniqueID + ":" + key;
        if (!lastSampleByKey.TryGetValue(key, out var previousSample) || previousSample != value)
        {
            lastSampleByKey[key] = value;
            Log(facility, value);
        }
    }

    private static void LaunchPrefix(FlightDeck __instance, VehicleTypeOnBoard vehicle, Loadout loadout, LaunchTaskParameters ltp)
    {
        // Harmony binds this argument by the native method's parameter name.
        var launchParameters = ltp;
        if (!Main())
        {
            return;
        }

        // Stack contains method names only. Nonplayer evidence is privileged.
        var stack = new System.Diagnostics.StackTrace().GetFrames();
        var callers = string.Join(">", (stack ?? Array.Empty<System.Diagnostics.StackFrame>()).Take(MaximumCallerFrames).Select(frame => frame.GetMethod()?.DeclaringType?.Name + "." + frame.GetMethod()?.Name));
        Log(__instance._baseObject, "request aircraft=" + vehicle?._fileName + " loadout=" + loadout?.Name + " count="
            + launchParameters?._launchCount + " mission=" + launchParameters?._missionType + " strategicForcedOff=" + AIPlayer.IsForcedToDisable
            + " deckAiDisabled=" + __instance._aiDisabled + " callers=" + callers);
    }

    private static void LaunchPostfix(FlightDeck __instance, PendingLaunchTask __result)
    {
        if (!Main())
        {
            return;
        }

        Log(__instance._baseObject, "accepted=" + (__result != null) + " task=" + __result?._uid);
        if (__result == null)
        {
            return;
        }

        __result.Launched += (_, unit) =>
        {
            Log(__instance._baseObject, "spawn-callback task=" + __result._uid + " unit=" + unit?.UniqueID + " inFlight=" + InFlight(unit)
                + " deckState=" + unit?._flightDeckState);
            if (Own(__instance._baseObject) && unit != null && sorties.Count < MaximumTrackedSorties)
            {
                sorties.Add(new Sortie
                {
                    Facility = __instance._baseObject,
                    Unit = unit
                });
            }
        };
    }

    private static bool InFlight(ObjectBase nativeUnit) => nativeUnit is Aircraft aircraft ? aircraft._isInFlight : nativeUnit is Helicopter helicopter && helicopter._isInFlight;
    public static void Update() => Step(false);
    public static void AfterJobs() => Step(true);
    private static void Step(bool fromJobHook)
    {
        if (!Main() || !Singleton<GameMain>.InstanceExists() || !Singleton<GameMain>.Instance._initialized || Singleton<GameMain>.Instance._closing
            || !Singleton<SceneCreator>.Instance.IsLoadingDone || Singleton<LoadScreen>.Instance.cameraObject.enabled || Globals._playerTaskforce == null)
        {
            return;
        }

        if ((fromJobHook || GameTime.IsPaused()) && Time.realtimeSinceStartup >= nextSample && ObjectCalculationsJobScheduler.HasPreviousJobCompleted
            && CollisionAvoidanceJobScheduler.HasPreviousJobCompleted)
        {
            nextSample = Time.realtimeSinceStartup + SampleIntervalWallSeconds;
            foreach (var taskforce in new[]
            {
                Globals._playerTaskforce,
                Globals._enemyTaskforce
            }

            )
            {
                if (taskforce == null)
                {
                    continue;
                }

                foreach (var facility in taskforce._taskforceUnits.Where(nativeUnit => nativeUnit != null
                    && nativeUnit._obp?._flightDeck != null).Take(MaximumFacilitySamples).ToArray())
                {
                    Sample(facility);
                }
            }

            foreach (var sortie in sorties.ToArray())
            {
                if (sortie.Unit == null || sortie.Unit.IsDestroyed)
                {
                    if (!sortie.Gone)
                    {
                        sortie.Gone = true;
                        Log(sortie.Facility, "sortie-object-gone rtbRequested=" + sortie.Rtb);
                    }

                    continue;
                }

                var unit = sortie.Unit;
                Changed(sortie.Facility, "unit-" + unit.UniqueID, "unit=" + unit.UniqueID + " inFlight=" + InFlight(unit) + " deckState="
                    + unit._flightDeckState + " unitState=" + unit.StateMachine?.CurrentState?.GetType().Name + " order=" + unit.CurrentOrder.Value.OrderType);
                if (InFlight(unit) && string.IsNullOrEmpty(unit._flightDeckState) && sortie.AirborneAt < 0)
                {
                    sortie.AirborneAt = GameTime.missionElapsedTime;
                    Log(sortie.Facility, "airborne-confirmed unit=" + unit.UniqueID + " ordinaryMember="
                        + Globals._playerTaskforce._taskforceUnits.Contains(unit));
                }

                if (!sortie.Rtb && sortie.AirborneAt >= 0 && GameTime.missionElapsedTime > sortie.AirborneAt + ReturnToBaseDelayOperationalSeconds
                    && Own(unit) && unit.HasControl && !GameTime.IsPaused() && !(unit.StateMachine?.CurrentState is AircraftStates.AdvancedMissileEvasion))
                {
                    sortie.Rtb = true;
                    unit.setOrder(Order.Type.ReturnToBase, sortie.Facility, displayOrderText: false);
                    Log(sortie.Facility, "rtb-request unit=" + unit.UniqueID);
                }
            }
        }

        if (fromJobHook)
        {
            return;
        }

        if (!requested && GameTime.missionElapsedTime >= RequestAfterOperationalSeconds && !GameTime.IsPaused())
        {
            requested = true;
            GameTime.Pause();
            paused = true;
            pauseWall = Time.realtimeSinceStartup;
            pauseTick = GameTime.simulationTick;
            foreach (var facility in Globals._playerTaskforce._taskforceUnits.Where(nativeUnit => nativeUnit != null
                && nativeUnit._obp?._flightDeck != null).Take(MaximumFacilitySamples).ToArray())
            {
                Request(facility);
            }
        }

        if (paused && Time.realtimeSinceStartup > pauseWall + OwnedPauseDurationWallSeconds)
        {
            emit("AIROPS pause-check unchangedTick=" + (pauseTick == GameTime.simulationTick) + " paused=" + GameTime.IsPaused());
            paused = false;
            // Disposable test owns this pause; no general player/menu pause lease is claimed.
            if (GameTime.IsPaused())
            {
                GameTime.UnPause();
                GameTime.SetTimeCompression(TestTimeCompression);
                emit("AIROPS owned-test-resume compression=" + GameTime.TimeCompression);
            }
        }
    }

    private static void Sample(ObjectBase facility)
    {
        var flightDeck = facility._obp._flightDeck;
        Changed(facility, "facility", "type=" + facility._obp._objectIniName + " own=" + Own(facility) + " control=" + facility.HasControl
            + " playerOrders=" + facility.AcceptsOrdersFromPlayer + " deckAiDisabled=" + flightDeck._aiDisabled + " inoperable="
            + flightDeck.Inoperable.Value + " maxCapacity=" + flightDeck.MaxAircraftOnBoard + " onboard=" + flightDeck.AircraftCurrentlyOnBoard
            + " readyCount=" + flightDeck.TotalVehiclesOnBoard() + " crews=" + flightDeck.GroundCrewCount + " deckSlots=" + flightDeck.DeckParkSlots
            + " ammo=" + flightDeck._currentAmmo + " taskCount=" + flightDeck.FlightDeckTasks.Count + " landingQueue="
            + flightDeck._landingQueue.Count + " holdingQueue=" + flightDeck._holdingQueue.Count + " timingMode="
            + Singleton<OptionsManager>.Instance.FlightDeckTimingsMode);
        foreach (var vehicleType in flightDeck._vehiclesOnBoard.Where(vehicleType => vehicleType.Numbers > 0
            || flightDeck.FlightDeckTasks.Any(deckTask => deckTask.VehicleType == vehicleType)).Take(MaximumVehicleTypeSamples))
        {
            Changed(facility, "inventory-" + vehicleType._fileName, "inventory aircraft=" + vehicleType._fileName + " numbers=" + vehicleType.Numbers
                + " squads=" + string.Join(",", vehicleType.Squadrons.Select(squadron => squadron._name + ":" + squadron.Numbers)));
        }

        foreach (var task in flightDeck.FlightDeckTasks.Take(MaximumDeckTaskSamples))
        {
            Changed(facility, "task-" + task._uid, "task=" + task._uid + " kind=" + task.GetType().Name + " state="
                + task._stateMachine?.CurrentState?.GetType().Name + " unit=" + task.Vehicle?.UniqueID + " remainingWork=" + Math.Round(task._duration,
                1) + " launchAllowed=" + (task is PendingLaunchTask pendingLaunchTask ? pendingLaunchTask.launchAllowed.ToString() : "n/a"));
        }
    }

    private static void Request(ObjectBase facility)
    {
        var flightDeck = facility._obp._flightDeck;
        if (!Own(facility) || !facility.HasControl || !facility.AcceptsOrdersFromPlayer || flightDeck.Inoperable.Value)
        {
            Log(facility, "request-rejected authority/inoperable");
            return;
        }

        var vehicleType = flightDeck._vehiclesOnBoard.FirstOrDefault(vehicleType => vehicleType.Numbers >= 1
            && vehicleType.Squadrons.Any(squadron => squadron.Numbers >= 1 && squadron.Callsigns.Count > 0));
        var squadron = vehicleType?.Squadrons.FirstOrDefault(squadron => squadron.Numbers >= 1 && squadron.Callsigns.Count > 0);
        var loadout = vehicleType?.Loadouts.FirstOrDefault(loadout => flightDeck.CanLaunchVehicle(loadout, 1) && loadout.Name != "None");
        if (loadout == null || squadron == null)
        {
            Log(facility, "request-rejected inventory/loadout");
            return;
        }

        Log(facility, "selected aircraft=" + vehicleType._fileName + " loadout=" + loadout.Name + " nominalReadyWork=" + loadout._readyUpDuration
            + " ammoCost=" + loadout._ammoPoints + " pause=" + GameTime.IsPaused() + " tick=" + GameTime.simulationTick);
        var stockBeforeRequest = vehicleType.Numbers;
        var ammoBefore = flightDeck._currentAmmo;
        var cancelledLaunchTask = flightDeck.createLaunchTask(vehicleType, loadout, squadron, squadron.Callsigns[0], new LaunchTaskParameters(),
            allowLaunch: false);
        if (cancelledLaunchTask != null)
        {
            cancelledLaunchTask.PrioritiseTaskFunc(null);
            cancelledLaunchTask.ToggleLaunchAllowedFunc(null);
            cancelledLaunchTask.ToggleLaunchAllowedFunc(null);
            flightDeck.abortLaunchTask(cancelledLaunchTask._uid);
            Log(facility, "cancel-check inventoryRestored=" + (vehicleType.Numbers == stockBeforeRequest) + " ammoRestored="
                + (flightDeck._currentAmmo == ammoBefore));
        }

        flightDeck.createLaunchTask(vehicleType, loadout, squadron, squadron.Callsigns[0], new LaunchTaskParameters());
    }
}
