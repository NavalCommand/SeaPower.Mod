using System;
using System.IO;
using System.Threading;
using BepInEx;
using SeaPower;
using UnityEngine.SceneManagement;

namespace NavalCommand.SeaPower.Harness;

[BepInPlugin("dev.navalcommand.seapower.harness", "NavalCommand Development Harness", "0.1.0")]
public sealed class HarnessPlugin : BaseUnityPlugin
{
    private EvidenceWriter evidence;
    private MissionLauncher launcher;
    private string mission;
    private string side;
    private bool active;
    private bool installed;
    private bool reloadRequested;
    private bool reload;

    private void Awake()
    {
        if (System.Environment.GetEnvironmentVariable("NC_HARNESS_ENABLED") != "1")
        {
            return;
        }
        try
        {
            string directory = System.Environment.GetEnvironmentVariable("NC_HARNESS_RUN_DIRECTORY");
            mission = System.Environment.GetEnvironmentVariable("NC_HARNESS_SCENARIO");
            side = System.Environment.GetEnvironmentVariable("NC_HARNESS_SIDE");
            string token = System.Environment.GetEnvironmentVariable("NC_HARNESS_TOKEN");
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory) || string.IsNullOrWhiteSpace(mission)
                || !Path.IsPathRooted(mission) || string.IsNullOrWhiteSpace(side) || !Guid.TryParse(token, out _))
            {
                throw new ArgumentException("Explicit absolute run/scenario paths, player side and GUID run token are required.");
            }
            evidence = new EvidenceWriter(directory, token, System.Diagnostics.Process.GetCurrentProcess().Id);
            reload = System.Environment.GetEnvironmentVariable("NC_HARNESS_RELOAD") == "1";
            SceneManager.sceneLoaded += SceneLoaded;
            SceneManager.sceneUnloaded += SceneUnloaded;
            active = true;
            evidence.Emit("plugin-awake thread=" + Thread.CurrentThread.ManagedThreadId);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception);
        }
    }

    private void Update()
    {
        if (!active)
        {
            return;
        }
        if (evidence.Failed)
        {
            Logger.LogError(evidence.Error);
            Teardown();
            return;
        }
        if (!installed && Globals.mainThread == Thread.CurrentThread)
        {
            installed = true;
            try
            {
                launcher = MissionLauncher.Install(mission, side);
            }
            catch (Exception exception)
            {
                evidence.Emit("failed-install " + exception);
            }
        }
        if (launcher != null)
        {
            while (launcher.TryReadPhase(out string phase))
            {
                evidence.Emit(phase);
            }
        }
        if (reload && !reloadRequested && GameTime.missionElapsedTime > 15
            && Singleton<GameMain>.InstanceExists() && Singleton<GameMain>.Instance._initialized)
        {
            reloadRequested = true;
            evidence.Emit("native-unload-reload-requested");
            Singleton<MissionManager>.Instance.UnloadMissionAndReload();
        }
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode) => evidence?.Emit("scene-loaded buildIndex=" + scene.buildIndex);
    private void SceneUnloaded(Scene scene) => evidence?.Emit("scene-unloaded buildIndex=" + scene.buildIndex);

    private void Teardown()
    {
        active = false;
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneUnloaded -= SceneUnloaded;
        if (launcher != null)
        {
            UnityEngine.Object.Destroy(launcher.gameObject);
            launcher = null;
        }
        evidence?.Emit("plugin-destroyed");
        evidence?.Stop();
    }

    private void OnDestroy() => Teardown();
}
