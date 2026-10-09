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

public static class Bootstrap
{
    public static void Attach()
    {
        if (System.Environment.GetEnvironmentVariable("NC_TASK004_ENABLED") != "1")
        {
            return;
        }

        if (Globals.mainThread != Thread.CurrentThread)
        {
            throw new InvalidOperationException("engine_thread_required");
        }

        new Runtime().Install();
    }
}
