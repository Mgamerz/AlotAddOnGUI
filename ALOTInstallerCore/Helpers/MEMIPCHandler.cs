﻿using Serilog;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using ALOTInstallerCore.Objects;
using CliWrap;
using CliWrap.EventStream;

namespace ALOTInstallerCore.Helpers
{
    [Flags]
    public enum LodSetting
    {
        Vanilla = 0,
        TwoK = 1,
        FourK = 2,
        SoftShadows = 4,
    }


    /// <summary>
    /// Utility class for interacting with MEM. Calls must be run on a background thread or they will stall due to how the CliWrap library appears tto work.
    /// </summary>
    public static class MEMIPCHandler
    {
        /// <summary>
        /// Returns the version number for MEM, or 0 if it couldn't be retreived
        /// </summary>
        /// <returns></returns>
        public static short GetMemVersion()
        {
            short version = 0;
            // If the current version doesn't support the --version --ipc, we just assume it is 0.
            MEMIPCHandler.RunMEMIPCUntilExit("--version --ipc", ipcCallback: (command, param) =>
            {
                if (command == "VERSION")
                {
                    version = short.Parse(param);
                }
            });
            return version;
        }

        /// <summary>
        /// Verifies a game against the MEM MD5 database
        /// </summary>
        /// <param name="game"></param>
        /// <param name="applicationStarted"></param>
        /// <param name="ipcCallback"></param>
        /// <param name="applicationStdErr"></param>
        /// <param name="applicationExited"></param>
        /// <param name="cancellationToken"></param>
        public static void VerifyVanilla(Enums.MEGame game, Action<int> applicationStarted = null,
            Action<string, string> ipcCallback = null, Action<string> applicationStdErr = null,
            Action<int> applicationExited = null, CancellationToken cancellationToken = default)
        {
            RunMEMIPCUntilExit($"--check-game-data-vanilla --gameid {game.ToGameNum()} --ipc", applicationStarted, ipcCallback, applicationStdErr, applicationExited, cancellationToken);
        }

        public static void RunMEMIPCUntilExit(string arguments, Action<int> applicationStarted = null, Action<string, string> ipcCallback = null, Action<string> applicationStdErr = null, Action<int> applicationExited = null, CancellationToken cancellationToken = default)
        {
            if (Settings.DebugLogs)
            {
                arguments += " --debug-logs";
            }
            object lockObject = new object();
            void appStart(int processID)
            {
                applicationStarted?.Invoke(processID);
                // This might need to be waited on after method is called.
                Debug.WriteLine(@"Process launched. Process ID: " + processID);
            }
            void appExited(int code)
            {
                Debug.WriteLine($"Process exited with code {code}");
                applicationExited?.Invoke(code);
                lock (lockObject)
                {
                    Monitor.Pulse(lockObject);
                }
            }

            // Run MEM
            MEMIPCHandler.RunMEMIPC(arguments, appStart, ipcCallback, applicationStdErr, appExited,
                cancellationToken);

            // Wait until exit
            lock (lockObject)
            {
                Monitor.Wait(lockObject);
            }
        }

        private static readonly UTF8Encoding unicode = new UTF8Encoding();


        private static async void RunMEMIPC(string arguments, Action<int> applicationStarted = null, Action<string, string> ipcCallback = null, Action<string> applicationStdErr = null, Action<int> applicationExited = null, CancellationToken cancellationToken = default)
        {
            bool exceptionOcurred = false;
            void internalHandleIPC(string command, string parm)
            {
                switch (command)
                {
                    case "EXCEPTION_OCCURRED": //An exception has occured and MEM is going to crash
                        exceptionOcurred = true;
                        break;
                    default:
                        ipcCallback?.Invoke(command, parm);
                        break;
                }
            }

            // No validation. Make sure exit code is checked in the calling process.
            var cmd = Cli.Wrap(Locations.MEMPath()).WithArguments(arguments).WithValidation(CommandResultValidation.None);
            Debug.WriteLine($"Launching process: {Locations.MEMPath()} {arguments}");
            await foreach (var cmdEvent in cmd.ListenAsync(Encoding.Unicode, cancellationToken))
            {
                switch (cmdEvent)
                {
                    case StartedCommandEvent started:
                        applicationStarted?.Invoke(started.ProcessId);
                        break;
                    case StandardOutputCommandEvent stdOut:
                        Debug.WriteLine(stdOut.Text);
                        if (stdOut.Text.StartsWith(@"[IPC]"))
                        {
                            var ipc = breakdownIPC(stdOut.Text);
                            internalHandleIPC(ipc.command, ipc.param);
                        }
                        else
                        {
                            if (exceptionOcurred)
                            {
                                Log.Fatal(stdOut.Text);
                            }
                        }
                        break;
                    case StandardErrorCommandEvent stdErr:
                        Debug.WriteLine("STDERR " + stdErr.Text);
                        if (exceptionOcurred)
                        {
                            Log.Fatal(stdErr.Text);
                        }
                        else
                        {
                            applicationStdErr?.Invoke(stdErr.Text);
                        }
                        break;
                    case ExitedCommandEvent exited:
                        applicationExited?.Invoke(exited.ExitCode);
                        break;
                }
            }
        }

        /// <summary>
        /// Converts MEM IPC output to command, param for handling. This method assumes string starts with [IPC] always.
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        private static (string command, string param) breakdownIPC(string str)
        {
            string command = str.Substring(5);
            int endOfCommand = command.IndexOf(' ');
            if (endOfCommand >= 0)
            {
                command = command.Substring(0, endOfCommand);
            }

            string param = str.Substring(endOfCommand + 5).Trim();
            return (command, param);
        }

        /// <summary>
        /// Sets the path MEM will use for the specified game
        /// </summary>
        /// <param name="targetGame"></param>
        /// <param name="targetPath"></param>
        /// <returns></returns>
        public static bool SetGamePath(Enums.MEGame targetGame, string targetPath)
        {
            int exitcode = 0;
            string args = $"--set-game-data-path --game-id {targetGame.ToGameNum()} --path \"{targetPath}\"";
            MEMIPCHandler.RunMEMIPCUntilExit(args, applicationExited: x => exitcode = x);
            if (exitcode != 0)
            {
                Log.Error($"Non-zero MassEffectModderNoGui exit code setting game path: {exitcode}");
            }
            return exitcode == 0;
        }

        /// <summary>
        /// Sets the LODs as specified in the setting bitmask with MEM for the specified game
        /// </summary>
        /// <param name="game"></param>
        /// <param name="setting"></param>
        /// <returns></returns>
        public static bool SetLODs(Enums.MEGame game, LodSetting setting)
        {
            string args = $"--apply-lods-gfx --gameid {game.ToGameNum()}";
            if (setting.HasFlag(LodSetting.SoftShadows))
            {
                args += " --soft-shadows-mode --meuitm-mode";
            }

            if (setting.HasFlag(LodSetting.TwoK))
            {
                args += " --limit-2k";
            }
            else if (setting.HasFlag(LodSetting.FourK))
            {
                // Nothing
            }
            else if (setting == LodSetting.Vanilla)
            {
                // Remove LODs
                args = $"--remove-lods --gameid {game.ToGameNum()}";
            }

            int exitcode = -1;
            // We don't care about IPC on this
            MEMIPCHandler.RunMEMIPCUntilExit(args,
                null,
                (x, y) => Debug.WriteLine("hi"),
                x => Log.Error($"StdError setting LODs: {x}"),
                x => exitcode = x); //Change to catch exit code of non zero.        
            if (exitcode != 0)
            {
                Log.Error($"MassEffectModderNoGui had error setting LODs, exited with code {exitcode}");
                return false;
            }

            return true;
        }
    }
}
