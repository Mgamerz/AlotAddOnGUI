﻿using System;
using System.Collections.Generic;
using System.Text;
using ALOTInstallerCore.Helpers;
using Serilog;

namespace ALOTInstallerCore
{
    /// <summary>
    /// Class that is used to setup hooking into the library and setting up things such as logging, API keys, etc.
    /// </summary>
    public static class Hook
    {
        private static bool startedUp;

        /// <summary>
        /// Sets the Logger that will be used by Serilog to perform logging to disk.
        /// </summary>
        /// <param name="logger">Logger for the application to use</param>
        public static void SetLogger(ILogger logger)
        {
            Log.Logger = logger;
        }

        public enum Platform
        {
            Windows,
            Linux,
            MacOS
        }

        public static void Startup(Platform platform)
        {
            if (startedUp) return;
            startedUp = true;
            Locations.LoadLocations(platform);
        }
    }
}