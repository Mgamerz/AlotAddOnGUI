﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using ALOTInstallerCore.Helpers;
using ALOTInstallerCore.ModManager.GameDirectories;
using ALOTInstallerCore.ModManager.Objects;
using ALOTInstallerCore.ModManager.Services;
using ALOTInstallerCore.Objects;
using ALOTInstallerCore.PlatformSpecific.Windows;
using Microsoft.Win32;
using Serilog;

namespace ALOTInstallerCore.ModManager.ME3Tweaks
{


    /// <summary>
    /// Interaction logic for BackupRestoreManager.xaml
    /// </summary>
    public static class BackupHandler
    {
        public static bool AnyGameMissingBackup => !BackupService.ME1BackedUp || !BackupService.ME2BackedUp || !BackupService.ME3BackedUp;

        public class GameBackup : INotifyPropertyChanged
        {
            private Enums.MEGame Game;
            public ObservableCollectionExtended<GameTarget> AvailableTargetsToBackup { get; } = new ObservableCollectionExtended<GameTarget>();

            /// <summary>
            /// Reports the current progress of the backup
            /// </summary>
            public Action<long, long> BackupProgressCallback { get; set; }
            /// <summary>
            /// Called when there is a blocking action, such as game running
            /// </summary>
            public Action<string, string> BlockingActionCallback { get; set; }
            /// <summary>
            /// Called when there is a warning that needs a yes/no answer
            /// </summary>
            public Func<string, string, bool> WarningActionCallback { get; set; }
            /// <summary>
            /// Called when the user must select a game executable (for backup)
            /// </summary>
            public Func<Enums.MEGame, string> SelectGameExecutableCallback { get; set; }

            /// <summary>
            /// Called when the user must select a backup folder destination
            /// </summary>
            public Func<string> SelectGameBackupFolderDestination { get; set; }
            /// <summary>
            /// Called when the backup thread has completed.
            /// </summary>
            public Action NotifyBackupThreadCompleted { get; set; }
            /// <summary>
            /// Called when there is a warning that has a potentially long list of items in it. These items should be placed in a scrolling mechanism
            /// </summary>
            public Func<string, string, List<string>, bool> WarningListCallback { get; set; }


            public GameBackup(Enums.MEGame game, IEnumerable<GameTarget> availableBackupSources)
            {
                this.Game = game;
                this.AvailableTargetsToBackup.AddRange(availableBackupSources);
                this.AvailableTargetsToBackup.Add(new GameTarget(Game, "Link to an existing backup", false, true));
                ResetBackupStatus();
            }

            public void BeginBackup()
            {
                var targetToBackup = BackupSourceTarget;
                if (!targetToBackup.IsCustomOption)
                {
                    Log.Information($"BeginBackup() on {BackupSourceTarget.TargetPath}");
                    // Backup target
                    if (Utilities.IsGameRunning(targetToBackup.Game))
                    {
                        BlockingActionCallback?.Invoke("Cannot backup game", $"Cannot backup while {BackupSourceTarget.Game.ToGameName()} is running.");
                        return;
                    }
                }
                else
                {
                    // Point to existing game installation
                    Log.Information(@"BeginBackup() with IsCustomOption.");
                    var linkOK = WarningActionCallback?.Invoke("Ensure correct game chosen", "The path you specify will be checked if it is a vanilla backup. Once this check is complete it will be marked as a backup and modding tools will refuse to modify it. Ensure this is not your active game path or you will be unable to mod the game.");
                    if (!linkOK.HasValue || !linkOK.Value)
                    {
                        Log.Information(@"User aborted linking due to dialog");
                        return;
                    }

                    Log.Information(@"Prompting user to select executable of link target");

                    var gameexe = SelectGameExecutableCallback?.Invoke(Game);

                    if (gameexe == null)
                    {
                        Log.Warning("User did not choose game executable to link as backup. Aborting");
                        return;
                    }

                    targetToBackup = new GameTarget(Game, Utilities.GetGamePathFromExe(Game, gameexe), false, true);
                    if (AvailableTargetsToBackup.Any(x => x.TargetPath.Equals(targetToBackup.TargetPath, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        // Can't point to an existing modding target
                        Log.Error(@"This target is not valid to point to as a backup: It is listed a modding target already, it must be removed as a target first");
                        BlockingActionCallback?.Invoke("Cannot backup game", "Cannot use this target as backup: It is the current game path for this game.");
                        return;
                    }

                    var validationFailureReason = targetToBackup.ValidateTarget(ignoreCmmVanilla: true);
                    if (!targetToBackup.IsValid)
                    {
                        Log.Error(@"This installation is not valid to point to as a backup: " + validationFailureReason);
                        BlockingActionCallback?.Invoke("Cannot backup game", $"Cannot use this target as backup: {validationFailureReason}");
                        return;
                    }
                }

                NamedBackgroundWorker nbw = new NamedBackgroundWorker(Game + @"Backup");
                nbw.DoWork += (a, b) =>
                {
                    Log.Information(@"Starting the backup thread. Checking path: " + targetToBackup.TargetPath);
                    BackupInProgress = true;

                    List<string> nonVanillaFiles = new List<string>();

                    void nonVanillaFileFoundCallback(string filepath)
                    {
                        Log.Error($@"Non-vanilla file found: {filepath}");
                        nonVanillaFiles.Add(filepath);
                    }

                    List<string> inconsistentDLC = new List<string>();

                    void inconsistentDLCFoundCallback(string filepath)
                    {
                        if (targetToBackup.Supported)
                        {
                            Log.Error($@"DLC is in an inconsistent state: {filepath}");
                            inconsistentDLC.Add(filepath);
                        }
                        else
                        {
                            Log.Error(@"Detected an inconsistent DLC, likely due to an unofficial copy of the game");
                        }
                    }

                    ProgressVisible = true;
                    ProgressIndeterminate = true;
                    BackupStatus = "Validating backup source";
                    Log.Information(@"Checking target is vanilla");
                    bool isVanilla = VanillaDatabaseService.ValidateTargetAgainstVanilla(targetToBackup, nonVanillaFileFoundCallback);

                    Log.Information(@"Checking DLC consistency");
                    bool isDLCConsistent = VanillaDatabaseService.ValidateTargetDLCConsistency(targetToBackup, inconsistentDLCCallback: inconsistentDLCFoundCallback);

                    Log.Information(@"Checking only vanilla DLC is installed");
                    List<string> dlcModsInstalled = VanillaDatabaseService.GetInstalledDLCMods(targetToBackup).Select(x =>
                    {
                        var tpmi = ThirdPartyServices.GetThirdPartyModInfo(x, targetToBackup.Game);
                        if (tpmi != null) return $@"{x} ({tpmi.modname})";
                        return x;
                    }).ToList();
                    List<string> installedDLC = VanillaDatabaseService.GetInstalledOfficialDLC(targetToBackup);
                    List<string> allOfficialDLC = MEDirectories.OfficialDLC(targetToBackup.Game);

                    if (installedDLC.Count() < allOfficialDLC.Count())
                    {
                        var dlcList = string.Join("\n - ", allOfficialDLC.Except(installedDLC).Select(x => $@"{MEDirectories.OfficialDLCNames(targetToBackup.Game)[x]} ({x})")); //do not localize
                        dlcList = @" - " + dlcList;
                        Log.Information(@"The following dlc will be missing in the backup if user continues: " + dlcList);
                        string message =
                            $"This target does not have have all OFFICIAL DLC installed. Ensure you have installed all OFFICIAL DLC you want to include in your backup, otherwise a game restore will not include all of it.\n\nThe following DLC is not installed:\n{dlcList}\n\nMake a backup of this target?";
                        var okToBackup = WarningActionCallback?.Invoke("Warning: some official DLC missing", message);
                        if (!okToBackup.HasValue || !okToBackup.Value)
                        {
                            Log.Information("User canceled backup due to some missing data");
                            return;
                        }
                    }

                    if (!isDLCConsistent)
                    {
                        if (targetToBackup.Supported)
                        {
                            BlockingActionCallback?.Invoke("Issue detected", "Detected inconsistent DLCs, which is due to having vanilla DLC files with unpacked archives. Delete (do not repair) your game installation and reinstall the game to fix this issue.");
                            return;
                        }
                        else
                        {
                            BlockingActionCallback?.Invoke("Issue detected", "Detected inconsistent DLCs, likely due to using an unofficial copy of the game. This game cannot be backed up.");
                            return;
                        }
                    }


                    if (!isVanilla)
                    {
                        //Show UI for non vanilla
                        string message = "The following files were found to be modified and are not vanilla. You can continue making a backup, however other modding tools such as ME3Tweaks Mod Manager will not accept this as a valid backup source. It is highly recommended that all backups of a game be unmodified, as a broken modified backup is a worthless backup.";
                        string bottomMessage = "Make a backup anyways (NOT RECOMMENDED)?";
                        var continueBackup = WarningListCallback?.Invoke(message, bottomMessage, nonVanillaFiles);
                        if (!continueBackup.HasValue || !continueBackup.Value)
                        {
                            Log.Information("User aborted backup due to non-vanilla files found");
                            return;
                        }
                    }
                    else if (dlcModsInstalled.Any())
                    {
                        //Show UI for non vanilla
                        string message = "The following DLC mods were found to be installed. These mods are not part of the original game. You can continue making a backup, however other modding tools such as ME3Tweaks Mod Manager will not accept this as a valid backup source. It is highly recommended that all backups of a game be unmodified, as a broken modified backup is a worthless backup.";
                        string bottomMessage = "Make a backup anyways (NOT RECOMMENDED)?";
                        var continueBackup = WarningListCallback?.Invoke(message, bottomMessage, dlcModsInstalled);
                        if (!continueBackup.HasValue || !continueBackup.Value)
                        {
                            Log.Information("User aborted backup due to found DLC mods");
                            return;
                        }
                    }

                    BackupStatus = "Waiting for user input";

                    string backupPath = null;
                    if (!targetToBackup.IsCustomOption)
                    {
                        // Creating a new backup
                        Log.Information(@"Prompting user to select backup destination");
                        backupPath = SelectGameBackupFolderDestination?.Invoke();
                        if (backupPath != null && Directory.Exists(backupPath))
                        {
                            Log.Information(@"Backup path chosen: " + backupPath);
                            bool okToBackup = validateBackupPath(backupPath, targetToBackup);
                            if (!okToBackup)
                            {
                                EndBackup();
                                return;
                            }
                        }
                        else
                        {
                            EndBackup();
                            return;
                        }
                    }
                    else
                    {
                        Log.Information(@"Linking existing backup at " + targetToBackup.TargetPath);
                        backupPath = targetToBackup.TargetPath;
                        // Linking existing backup
                        bool okToBackup = validateBackupPath(targetToBackup.TargetPath, targetToBackup);
                        if (!okToBackup)
                        {
                            EndBackup();
                            return;
                        }
                    }


                    if (!targetToBackup.IsCustomOption)
                    {
                        #region callbacks and copy code

                        // Copy to new backup
                        void fileCopiedCallback()
                        {
                            ProgressValue++;
                            BackupProgressCallback?.Invoke(ProgressValue, ProgressMax);
                        }

                        string dlcFolderpath = MEDirectories.DLCPath(targetToBackup) + '\\';
                        int dlcSubStringLen = dlcFolderpath.Length;

                        bool aboutToCopyCallback(string file)
                        {
                            try
                            {
                                if (file.Contains(@"\cmmbackup\")) return false; //do not copy cmmbackup files
                                if (file.StartsWith(dlcFolderpath))
                                {
                                    //It's a DLC!
                                    string dlcname = file.Substring(dlcSubStringLen);
                                    var dlcFolderNameEndPos = dlcname.IndexOf('\\');
                                    if (dlcFolderNameEndPos > 0)
                                    {
                                        dlcname = dlcname.Substring(0, dlcFolderNameEndPos);
                                        if (MEDirectories.OfficialDLCNames(targetToBackup.Game)
                                            .TryGetValue(dlcname, out var hrName))
                                        {
                                            BackupStatusLine2 = $"Backing up {hrName}";
                                        }
                                        else
                                        {
                                            BackupStatusLine2 = $"Backing up {dlcname}";
                                        }
                                    }
                                    else
                                    {
                                        // Loose files in the DLC folder
                                        BackupStatusLine2 = $"Backing up basegame";
                                    }
                                }
                                else
                                {
                                    //It's basegame
                                    if (file.EndsWith(@".bik"))
                                    {
                                        BackupStatusLine2 = $"Backing up movies";
                                    }
                                    else if (new FileInfo(file).Length > 52428800)
                                    {

                                        BackupStatusLine2 = $"Backing up {Path.GetFileName(file)}";
                                    }
                                    else
                                    {
                                        BackupStatusLine2 = $"Backing up basegame";
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error($"Error about to copy file: {e.Message}");
                                Analytics.TrackError(e);
                            }


                            return true;
                        }

                        void totalFilesToCopyCallback(int total)
                        {
                            ProgressValue = 0;
                            ProgressIndeterminate = false;
                            ProgressMax = total;
                        }

                        BackupStatus = "Creating backup";
                        Log.Information($@"Backing up {targetToBackup.TargetPath} to {backupPath}");
                        CopyTools.CopyAll_ProgressBar(new DirectoryInfo(targetToBackup.TargetPath),
                            new DirectoryInfo(backupPath),
                            totalItemsToCopyCallback: totalFilesToCopyCallback,
                            aboutToCopyCallback: aboutToCopyCallback,
                            fileCopiedCallback: fileCopiedCallback,
                            ignoredExtensions: new[] { @"*.pdf", @"*.mp3", @"*.wav" });
                        #endregion
                    }

                    // Write key
                    WriteBackupLocation(Game, backupPath);

                    var cmmvanilla = Path.Combine(backupPath, @"cmm_vanilla");
                    if (!File.Exists(cmmvanilla))
                    {
                        Log.Information($@"Writing cmm_vanilla to " + cmmvanilla);
                        File.Create(cmmvanilla).Close();
                    }

                    Log.Information($@"Backup completed.");

                    Analytics.TrackEvent?.Invoke(@"Created a backup", new Dictionary<string, string>()
                        {
                                {@"game", Game.ToString()},
                                {@"Result", @"Success"},
                                {@"Type", targetToBackup.IsCustomOption ? @"Linked" : @"Copy"}
                        });

                    EndBackup();
                };
                nbw.RunWorkerCompleted += (a, b) =>
                        {
                            if (b.Error != null)
                            {
                                Log.Error($@"Exception occured in {nbw.Name} thread: {b.Error.Message}");
                            }

                            NotifyBackupThreadCompleted?.Invoke();
                        };
                nbw.RunWorkerAsync();
            }

            private bool validateBackupPath(string backupPath, GameTarget targetToBackup)
            {
                //Check empty
                if (!targetToBackup.IsCustomOption && Directory.Exists(backupPath))
                {
                    if (Directory.GetFiles(backupPath).Length > 0 ||
                        Directory.GetDirectories(backupPath).Length > 0)
                    {
                        //Directory not empty
                        Log.Error(@"Selected backup directory is not empty.");
                        BlockingActionCallback?.Invoke("Invalid backup destination",
                            "The backup destination directory must be empty. Delete the files and folders in this directory, or select a different empty path.");
                        return false;
                    }
                }

                //Check is Documents folder
                var docsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"BioWare", targetToBackup.Game.ToGameName());
                if (backupPath.Equals(docsPath, StringComparison.InvariantCultureIgnoreCase) || backupPath.IsSubPathOf(docsPath))
                {
                    Log.Error(@"User chose path in or around the documents path for the game - not allowed as game can load files from here.");
                    BlockingActionCallback?.Invoke($"Invalid backup destination", $"The backup destination cannot be a subdirectory of the Documents/BioWare/{targetToBackup.Game.ToGameName()} folder. Select a different directory.");
                    return false;
                }

                //Check space
                DriveInfo di = new DriveInfo(backupPath);
                var requiredSpace = (long)(Utilities.GetSizeOfDirectory(new DirectoryInfo(targetToBackup.TargetPath)) * 1.1); //10% buffer
                Log.Information($@"Backup space check. Backup size required: {FileSizeFormatter.FormatSize(requiredSpace)}, free space: {FileSizeFormatter.FormatSize(di.AvailableFreeSpace)}");
                if (di.AvailableFreeSpace < requiredSpace)
                {
                    //Not enough space.
                    Log.Error($@"Not enough disk space to create backup at {backupPath}");
                    BlockingActionCallback?.Invoke("Not enough free disk space", $"There is not enough free disk space to make a game backup at this location.\n\nFree space: {FileSizeFormatter.FormatSize(di.AvailableFreeSpace)}\nRequired space: {FileSizeFormatter.FormatSize(requiredSpace)}");
                    return false;
                }

                //Check it is not subdirectory of the game (we might want to check its not subdir of a target)
                foreach (var target in AvailableTargetsToBackup)
                {
                    if (backupPath.IsSubPathOf(target.TargetPath))
                    {
                        //Not enough space.
                        Log.Error($@"A backup cannot be created in a subdirectory of a game. {backupPath} is a subdir of {targetToBackup.TargetPath}");
                        BlockingActionCallback?.Invoke("Invalid backup destination", $"You cannot place a backup into a subdirectory of the game you are backing up. Select another directory.");
                        return false;
                    }
                }

                //Check writable
                var writable = Utilities.IsDirectoryWritable(backupPath);
                if (!writable)
                {
                    //Not enough space.
                    Log.Error($@"Backup destination selected is not writable.");
                    BlockingActionCallback?.Invoke("Invalid backup destination", "Selected backup folder does not have write permissions from this account. Select a different directory.");
                    return false;
                }
                return true;
            }


            private void EndBackup()
            {
                Log.Information($@"EndBackup()");
                ResetBackupStatus();
                ProgressIndeterminate = false;
                ProgressVisible = false;
                BackupInProgress = false;
            }

            private void ResetBackupStatus()
            {
                BackupLocation = BackupService.GetGameBackupPath(Game);
                //BackupService.RefreshBackupStatus(window, Game);
                BackupStatus = BackupService.GetBackupStatus(Game);
                BackupStatusLine2 = BackupLocation ?? BackupService.GetBackupStatusTooltip(Game);
            }

            public event PropertyChangedEventHandler PropertyChanged;
            public GameTarget BackupSourceTarget { get; set; }
            public string BackupLocation { get; set; }
            public string BackupStatus { get; set; }
            public string BackupStatusLine2 { get; set; }
            public int ProgressMax { get; set; } = 100;
            public int ProgressValue { get; set; } = 0;
            public bool ProgressIndeterminate { get; set; } = true;
            public bool ProgressVisible { get; set; } = false;
            public bool BackupInProgress { get; set; }

        }

        //#if WINDOWS
        private const string REGISTRY_KEY_ME3CMM = @"HKEY_CURRENT_USER\Software\Mass Effect 3 Mod Manager";

        /// <summary>
        /// ALOT Addon Registry Key, used for ME1 and ME2 backups
        /// </summary>
        private const string BACKUP_REGISTRY_KEY = @"HKEY_CURRENT_USER\Software\ALOTAddon"; //Shared. Do not change
                                                                                            //#endif

        private static void WriteBackupLocation(Enums.MEGame game, string backupPath)
        {
            //#if WIN
            switch (game)
            {
                case Enums.MEGame.ME1:
                case Enums.MEGame.ME2:
                    RegistryHandler.WriteRegistryKey(Registry.CurrentUser, BACKUP_REGISTRY_KEY, game + @"VanillaBackupLocation", backupPath);
                    break;
                case Enums.MEGame.ME3:
                    RegistryHandler.WriteRegistryKey(Registry.CurrentUser, REGISTRY_KEY_ME3CMM, @"VanillaCopyLocation", backupPath);
                    break;
            }//#else

            //#endif
        }

        public static void UnlinkBackup(Enums.MEGame meGame)
        {
#if !WINDOWS
            switch (meGame)
            {
                case Enums.MEGame.ME1:
                case Enums.MEGame.ME2:
                    RegistryHandler.DeleteRegistryKey(Registry.CurrentUser, BACKUP_REGISTRY_KEY,
                        meGame + @"VanillaBackupLocation");
                    break;
                case Enums.MEGame.ME3:
                    RegistryHandler.DeleteRegistryKey(Registry.CurrentUser, REGISTRY_KEY_ME3CMM,
                        @"VanillaCopyLocation");
                    break;
            }
#elif LINUX

#endif

        }
    }
}