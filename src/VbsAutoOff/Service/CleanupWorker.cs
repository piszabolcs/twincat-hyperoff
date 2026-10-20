using System;
using System.ServiceProcess;
using System.Threading;
using VbsAutoOff.Core;
namespace VbsAutoOff.Service
{
    // The engine of the background service (System.ServiceProcess.ServiceBase, built into .NET Framework).
    // - On boot, plus an aggressive initial cadence (20s/3min, then 60s), it runs Phase1.
    // - On OnShutdown/OnStop, a final cleanup so the machine boots with a clean registry.
    //
    // During OnShutdown/OnStop ONLY a fast registry cleanup runs (no bcdedit, no WMI).
    // Previously 7 external processes + a WMI query ran here, which Windows can cut short on
    // shutdown (about a 5 s service timeout), so exactly the most important cleanup was skipped.
    // The GPO does not rewrite the BCD, so maintaining that in the normal cycle is enough.
    public sealed class CleanupWorker : ServiceBase
    {
        private Thread _workerThread;
        private ManualResetEvent _stopEvent;

        public CleanupWorker()
        {
            ServiceName = ServiceControl.ServiceName;
            CanStop = true;
            CanShutdown = true;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            _stopEvent = new ManualResetEvent(false);
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "VbsCleanupLoop"
            };
            _workerThread.Start();
        }

        protected override void OnStop()
        {
            Logger.Log("Service stopping (OnStop)...");
            if (_stopEvent != null) _stopEvent.Set();
            try { VbsCleaner.RunRegistryOnlyFast(); } catch { }
            if (_workerThread != null) _workerThread.Join(5000);
            Logger.Log("Service stopped.");
        }

        protected override void OnShutdown()
        {
            Logger.Log("System shutdown/restart (OnShutdown) -> fast registry cleanup.");
            if (_stopEvent != null) _stopEvent.Set();
            try { VbsCleaner.RunRegistryOnlyFast(); } catch { }
            base.OnShutdown();
        }

        private void WorkerLoop()
        {
            Logger.Log("Service started (boot + aggressive GPO protection + shutdown protection).");
            try
            {
                ServiceControl.EnsureWatchdogTask();
                VbsCleaner.RunPhase1(true);
            }
            catch (Exception ex)
            {
                Logger.Log("Startup Phase1 error: " + ex.Message);
            }
            int tickCount = 0;
            while (!_stopEvent.WaitOne(TimeSpan.FromSeconds(tickCount < 9 ? 20 : 60)))
            {
                tickCount++;
                try
                {
                    bool forceBcd = (tickCount % 15 == 0);
                    VbsCleaner.RunPhase1(forceBcd);
                }
                catch (Exception ex)
                {
                    Logger.Log("Cycle Phase1 error: " + ex.Message);
                }
            }
        }
    }
}

