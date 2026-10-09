using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using BepInEx;
using SeaPower;
using UnityEngine.SceneManagement;

namespace NavalCommand.Task002;

[BepInPlugin("dev.navalcommand.task002", "Task002 Runtime Validation", "0.0.1")]
public sealed class RuntimePlugin : BaseUnityPlugin
{
    private const int MaximumEvidenceMessages = 128;
    private const int WriterPollMilliseconds = 25;
    private const int MissionSceneBuildIndex = 2;
    private const double ReloadAfterOperationalSeconds = 15;
    private sealed class WriterState
    {
        public readonly ConcurrentQueue<string> Queue = new();
        public volatile bool Stop;
        public volatile bool Failed;
        public int Dropped;
        public volatile bool CompositionLoaded;
        public System.Reflection.Assembly CompositionAssembly;
    }

    private readonly WriterState writerState = new();
    private Thread writer;
    private string mission;
    private string side;
    private LaunchCandidate candidate;
    private bool installed;
    private bool active;
    private bool diagnostics;
    private bool reloadRequested;
    private bool lifecycleTest;
    private bool airOps;
    private void Awake()
    {
        active = System.Environment.GetEnvironmentVariable("NC_TASK002_ENABLED") == "1";
        if (!active)
        {
            return;
        }

        SceneManager.sceneLoaded += SceneLoaded;
        SceneManager.sceneUnloaded += SceneUnloaded;
        var directory = System.Environment.GetEnvironmentVariable("NC_TASK002_RUN_DIR");
        mission = System.Environment.GetEnvironmentVariable("NC_TASK002_SCENARIO");
        side = System.Environment.GetEnvironmentVariable("NC_TASK002_SIDE");
        if (string.IsNullOrWhiteSpace(directory))
        {
            active = false;
            return;
        }

        var plainState = writerState;
        var plainQueue = plainState.Queue;
        var token = System.Environment.GetEnvironmentVariable("NC_TASK002_TOKEN") ?? "";
        var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        var compositionEnabled = System.Environment.GetEnvironmentVariable("NC_TASK004_ENABLED") == "1";
        var compositionPath = System.Environment.GetEnvironmentVariable("NC_TASK004_MOD_ASSEMBLY");
        var platformDirectory = Path.GetDirectoryName(typeof(GameMain).Assembly.Location);
        // Worker closes over plain state, queue and strings only, no Unity/game state.
        writer = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                using var log = new StreamWriter(Path.Combine(directory, "probe.log"), false);
                using var privileged = new StreamWriter(Path.Combine(directory, "privileged.log"), false);
                if (compositionEnabled)
                {
                    // Mono does not probe our external staging directory automatically.
                    // Load metadata/dependencies on this I/O worker before main-thread DI.
                    var moduleDirectory = Path.GetDirectoryName(compositionPath);
                    // Use Unity's existing platform shims, never NuGet Span duplicates.
                    foreach (var name in new[]
                    {
                        "System.Buffers.dll",
                        "System.Runtime.CompilerServices.Unsafe.dll",
                        "System.Memory.dll"
                    }

                    )
                    {
                        System.Reflection.Assembly.LoadFrom(Path.Combine(platformDirectory, name));
                    }

                    foreach (var name in new[]
                    {
                        "Google.Protobuf.dll",
                        "Gateway.Api.dll",
                        "SeaPower.Client.dll"
                    }

                    )
                    {
                        System.Reflection.Assembly.LoadFrom(Path.Combine(moduleDirectory, name));
                    }

                    plainState.CompositionAssembly = System.Reflection.Assembly.LoadFrom(compositionPath);
                    plainState.CompositionLoaded = true;
                    log.WriteLine(DateTime.UtcNow.ToString("o") + " task004-metadata-loaded worker=" + Thread.CurrentThread.ManagedThreadId);
                }

                while (!plainState.Stop || !plainQueue.IsEmpty)
                {
                    if (Volatile.Read(ref plainState.Dropped) > 0)
                    {
                        log.WriteLine("evidence-overflow dropped=" + Volatile.Read(ref plainState.Dropped));
                        File.WriteAllText(Path.Combine(directory, "evidence-error.json"), "{\"error\":\"queue-overflow\"}");
                    }

                    while (plainQueue.TryDequeue(out var line))
                    {
                        if (line.Contains(" PRIVILEGED "))
                        {
                            privileged.WriteLine(line);
                        }
                        else
                        {
                            log.WriteLine(line);
                        }

                        if (line.Contains(" playable-ready ") && Guid.TryParse(token, out _))
                        {
                            File.WriteAllText(Path.Combine(directory, "ready.json"), "{\"ready\":true,\"token\":\"" + token + "\",\"pid\":" + processId + "}");
                        }
                    }

                    log.Flush();
                    privileged.Flush();
                    Thread.Sleep(WriterPollMilliseconds);
                }
            }
            catch (Exception exception)
            {
                plainState.Failed = true;
                try
                {
                    File.WriteAllText(Path.Combine(directory, "evidence-error.json"), "{\"error\":\"writer-failed\",\"type\":\""
                        + exception.GetType().Name + "\"}");
                }
                catch
                { /* No marker can be created; external timeout still cannot pass. */
                }
            }
        })
        {
            IsBackground = true,
            Name = "Task002 Evidence Writer"
        };
        writer.Start();
        Emit("plugin-awake thread=" + Thread.CurrentThread.ManagedThreadId);
        Emit("assembly=" + typeof(GameMain).Assembly.FullName);
    }

    private void Emit(string value)
    {
        if (writerState.Queue.Count < MaximumEvidenceMessages)
        {
            writerState.Queue.Enqueue(DateTime.UtcNow.ToString("o") + " " + value);
        }
        else
        {
            Interlocked.Increment(ref writerState.Dropped);
        }
    }

    private void Update()
    {
        if (!active)
        {
            return;
        }

        if (writerState.Failed)
        {
            if (diagnostics)
            {
                DiagnosticProbe.Remove();
            }

            if (airOps)
            {
                AirOpsProbe.Remove();
            }

            diagnostics = false;
            active = false;
            return;
        }

        if (!installed && Globals.mainThread == Thread.CurrentThread)
        {
            if (System.Environment.GetEnvironmentVariable("NC_TASK004_ENABLED") == "1" && !writerState.CompositionLoaded)
            {
                return;
            }

            installed = true;
            if (System.Environment.GetEnvironmentVariable("NC_TASK004_ENABLED") == "1")
            {
                try
                {
                    var assembly = writerState.CompositionAssembly;
                    assembly.GetType("NavalCommand.SeaPower.Mod.Bootstrap").GetMethod("Attach").Invoke(null, null);
                    Emit("task004-composition-attached");
                }
                catch (Exception exception)
                {
                    Emit("task004-bootstrap-failed " + exception);
                }
            }

            Emit("unity-thread-confirmed id=" + Thread.CurrentThread.ManagedThreadId);
            diagnostics = System.Environment.GetEnvironmentVariable("NC_TASK002_PROBES") == "1";
            airOps = System.Environment.GetEnvironmentVariable("NC_TASK003_AIROPS") == "1";
            if (airOps)
            {
                AirOpsProbe.Install(Emit);
            }

            lifecycleTest = System.Environment.GetEnvironmentVariable("NC_TASK002_LIFECYCLE") == "1";
            if (diagnostics)
            {
                DiagnosticProbe.Install(Emit);
            }

            if (!string.IsNullOrWhiteSpace(mission) && !string.IsNullOrWhiteSpace(side))
            {
                try
                {
                    candidate = LaunchCandidate.Install(mission, side);
                }
                catch (Exception exception)
                {
                    Emit("install-failed " + exception);
                }
            }
        }

        if (candidate != null)
        {
            while (candidate.TryReadPhase(out var phase))
            {
                Emit(phase);
            }
        }

        if (diagnostics)
        {
            DiagnosticProbe.UpdatePauseExperiment();
        }

        if (airOps)
        {
            AirOpsProbe.Update();
        }

        if (System.Environment.GetEnvironmentVariable("NC_TASK004_RELOAD_TEST") == "1" && !reloadRequested
            && GameTime.missionElapsedTime > ReloadAfterOperationalSeconds && Singleton<GameMain>.InstanceExists() && Singleton<GameMain>.Instance._initialized)
        {
            reloadRequested = true;
            Emit("task004-native-unload-reload-requested");
            Singleton<MissionManager>.Instance.UnloadMissionAndReload();
        }

        if (diagnostics && lifecycleTest && !reloadRequested && GameTime.missionElapsedTime > ReloadAfterOperationalSeconds
            && Singleton<GameMain>.InstanceExists() && Singleton<GameMain>.Instance._initialized)
        {
            reloadRequested = true;
            DiagnosticProbe.PrepareOwnedUnload();
            Emit("native-unload-reload-requested");
            Singleton<MissionManager>.Instance.UnloadMissionAndReload();
        }
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Emit("scene-loaded buildIndex=" + scene.buildIndex + " thread=" + Thread.CurrentThread.ManagedThreadId);
    }

    private void SceneUnloaded(Scene scene)
    {
        Emit("scene-unloaded buildIndex=" + scene.buildIndex + " thread=" + Thread.CurrentThread.ManagedThreadId);
        if (scene.buildIndex == MissionSceneBuildIndex && diagnostics)
        {
            DiagnosticProbe.InvalidateScene();
        }

        if (scene.buildIndex == MissionSceneBuildIndex && airOps)
        {
            AirOpsProbe.Remove();
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneUnloaded -= SceneUnloaded;
        if (diagnostics)
        {
            DiagnosticProbe.Remove();
        }

        Emit("plugin-destroyed");
        writerState.Stop = true;
        if (airOps)
        {
            AirOpsProbe.Remove();
        }
    }
}
