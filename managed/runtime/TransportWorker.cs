using System;
using System.IO;
using System.Threading;

namespace NavalCommand.SeaPower.Mod;

internal static class TransportWorker
{
    private const int MaximumQueuedCommands = 64;
    private const int MaximumOrderBytes = 65_536;
    private const int WorkerPollMilliseconds = 10;
    internal static void Run(RuntimeMailbox workerMailbox, RuntimeConfiguration configuration, Func<string, INativeTransport> createTransport = null)
    {
        string runDirectory = configuration.LogDirectory;
        string nativeLibraryPath = configuration.NativeLibrary;
        string endpoint = configuration.Endpoint;
        INativeTransport nativeTransport = null;
        try
        {
            using var log = new StreamWriter(Path.Combine(runDirectory, "gateway.log"), false)
            {
                AutoFlush = true
            };
            nativeTransport = createTransport == null ? new NativeTransport(nativeLibraryPath) : createTransport(nativeLibraryPath);
            int started = nativeTransport.Start(endpoint);
            log.WriteLine(DateTime.UtcNow.ToString("o") + " native-start abi=1 status=" + started);
            if (started != 0)
            {
                throw new InvalidOperationException("host_start_failed:" + started);
            }

            File.WriteAllText(Path.Combine(runDirectory, "gateway-ready.json"), "{\"abi\":1,\"endpoint\":\"" + endpoint + "\"}");
            var buffer = new byte[MaximumOrderBytes];
            byte[] pendingViewBytes = null;
            byte[] pendingResultBytes = null;
            while (!workerMailbox.Stop)
            {
                if (workerMailbox.Failed)
                {
                    throw new InvalidOperationException("provider_failed: " + workerMailbox.Error);
                }

                while (workerMailbox.Events.TryDequeue(out var line))
                {
                    log.WriteLine(DateTime.UtcNow.ToString("o") + " " + line);
                }

                while (workerMailbox.Views.TryDequeue(out var viewBytes))
                {
                    pendingViewBytes = viewBytes;
                }

                if (pendingViewBytes != null)
                {
                    int statusCode = nativeTransport.PublishView(pendingViewBytes);
                    if (statusCode == 0)
                    {
                        pendingViewBytes = null;
                    }
                    else if (statusCode != 1)
                    {
                        throw new InvalidOperationException("publish_view:" + statusCode);
                    }
                }

                if (pendingResultBytes == null)
                {
                    workerMailbox.Results.TryDequeue(out pendingResultBytes);
                }

                if (pendingResultBytes != null)
                {
                    int statusCode = nativeTransport.PublishResult(pendingResultBytes);
                    if (statusCode == 0)
                    {
                        pendingResultBytes = null;
                    }
                    else if (statusCode != 1)
                    {
                        log.WriteLine("expired-result rc=" + statusCode);
                        pendingResultBytes = null;
                    }
                }

                if (workerMailbox.Commands.Count < MaximumQueuedCommands)
                {
                    int statusCode = nativeTransport.PollOrder(buffer, out var size);
                    if (statusCode == 0)
                    {
                        if (size > buffer.Length)
                        {
                            throw new InvalidOperationException("native_order_size_out_of_bounds");
                        }
                        var bytes = new byte[size];
                        Buffer.BlockCopy(buffer, 0, bytes, 0, (int)size);
                        workerMailbox.Commands.Enqueue(bytes);
                    }
                    else if (statusCode != 1)
                    {
                        throw new InvalidOperationException("poll_order:" + statusCode);
                    }
                }

                Thread.Sleep(WorkerPollMilliseconds);
            }

            log.WriteLine("worker-stop status=" + nativeTransport.Stop());
            nativeTransport = null;
        }
        catch (Exception exception)
        {
            workerMailbox.Error = exception.ToString();
            workerMailbox.Failed = true;
            try
            {
                File.WriteAllText(Path.Combine(runDirectory, "gateway-error.txt"), exception.ToString());
            }
            catch (Exception loggingException)
            {
                workerMailbox.Error += "\nDiagnostic file failure: " + loggingException;
            }
        }
        finally
        {
            if (nativeTransport != null)
            {
                try
                {
                    nativeTransport.Stop();
                }
                catch (Exception shutdownException)
                {
                    workerMailbox.Error = "Native shutdown failed: " + shutdownException;
                    workerMailbox.Failed = true;
                }
            }
            workerMailbox.Completed = true;
        }
    }

}
