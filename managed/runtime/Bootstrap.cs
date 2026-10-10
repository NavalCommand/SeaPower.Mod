using System;
using System.IO;
using System.Reflection;
using System.Threading;
using BepInEx;
using SeaPower;

namespace NavalCommand.SeaPower.Mod;

// No provider/protobuf types are referenced before Mono dependencies are loaded.
[BepInPlugin("dev.navalcommand.seapower.mod", "NavalCommand Sea Power", "0.1.0")]
public sealed class Bootstrap : BaseUnityPlugin
{
    private sealed class Preparation
    {
        public RuntimeConfiguration Configuration;
        public string Error;
        public volatile bool Complete;
        public volatile bool Stop;
    }

    private readonly Preparation preparation = new();
    private object runtime;
    private MethodInfo stopRuntime;
    private bool attached;

    private void Awake()
    {
        string moduleDirectory = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location);
        string platformDirectory = Path.GetDirectoryName(typeof(GameMain).Assembly.Location);
        var state = preparation;
        new Thread(() => Prepare(state, moduleDirectory, platformDirectory))
        {
            IsBackground = true,
            Name = "SeaPower.Mod bootstrap"
        }.Start();
    }

    private static void Prepare(Preparation state, string moduleDirectory, string platformDirectory)
    {
        try
        {
            state.Configuration = RuntimeConfiguration.Load(moduleDirectory);
            if (state.Configuration.Enabled && !state.Stop)
            {
                if (!File.Exists(state.Configuration.NativeLibrary))
                {
                    throw new FileNotFoundException("Native host is missing.", state.Configuration.NativeLibrary);
                }
                Directory.CreateDirectory(state.Configuration.LogDirectory);
                string writeCheck = Path.Combine(state.Configuration.LogDirectory, Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(writeCheck, "write-check");
                File.Delete(writeCheck);
                // Use the game's platform shims; never ship duplicate System.* DLLs.
                foreach (string name in new[] { "System.Buffers.dll", "System.Runtime.CompilerServices.Unsafe.dll", "System.Memory.dll" })
                {
                    Assembly.LoadFrom(Path.Combine(platformDirectory, name));
                }
                foreach (string name in new[] { "Google.Protobuf.dll", "Gateway.Api.dll", "SeaPower.Client.dll" })
                {
                    Assembly.LoadFrom(Path.Combine(moduleDirectory, name));
                }
            }
        }
        catch (Exception exception)
        {
            state.Error = exception.ToString();
        }
        finally
        {
            state.Complete = true;
        }
    }

    private void Update()
    {
        if (attached || !preparation.Complete || preparation.Stop)
        {
            return;
        }
        if (preparation.Error != null)
        {
            attached = true;
            Logger.LogError("Runtime bootstrap failed: " + preparation.Error);
            return;
        }
        if (!preparation.Configuration.Enabled)
        {
            attached = true;
            Logger.LogInfo("Production runtime disabled.");
            return;
        }
        if (Globals.mainThread != Thread.CurrentThread)
        {
            return;
        }
        attached = true;
        try
        {
            var runtimeType = typeof(Bootstrap).Assembly.GetType("NavalCommand.SeaPower.Mod.Runtime", true);
            runtime = Activator.CreateInstance(runtimeType);
            stopRuntime = runtimeType.GetMethod("Stop");
            runtimeType.GetMethod("Install").Invoke(runtime, new object[] { preparation.Configuration, (Action<string>)Report });
            Logger.LogInfo("Production runtime attached independently.");
        }
        catch (Exception exception)
        {
            Logger.LogError(exception);
            stopRuntime?.Invoke(runtime, null);
        }
    }

    private void Report(string message) => Logger.LogError(message);

    private void OnDestroy()
    {
        preparation.Stop = true;
        stopRuntime?.Invoke(runtime, null);
    }
}
