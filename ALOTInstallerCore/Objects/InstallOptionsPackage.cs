﻿using System;
using System.Collections.Generic;
using System.Text;
using ALOTInstallerCore.ModManager.Objects.MassEffectModManagerCore.modmanager.objects;

namespace ALOTInstallerCore.Objects
{
    /// <summary>
    /// Contains information to pass to the builder
    /// </summary>
    public class InstallOptionsPackage
    {
        public GameTarget InstallTarget { get; set; }
        /// <summary>
        /// List of all installer files. The builder will determine what files are applicable out of this list
        /// This list can change depending on the mode.
        /// </summary>
        public List<InstallerFile> AllInstallerFiles { get; set; }
        public bool InstallALOT { get; set; }
        public bool InstallALOTUpdate { get; set; }
        public bool InstallALOTAddon { get; set; }
        public bool InstallMEUITM { get; set; }
        public bool InstallUserfiles { get; set; }
    }
}