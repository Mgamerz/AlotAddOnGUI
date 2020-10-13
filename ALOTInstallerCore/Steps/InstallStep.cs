﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using ALOTInstallerCore.Helpers;
using ALOTInstallerCore.Helpers.AppSettings;
using ALOTInstallerCore.ModManager.GameINI;
using ALOTInstallerCore.Objects;
using ALOTInstallerCore.Objects.Manifest;
using ALOTInstallerCore.Steps.Installer;
using MassEffectModManagerCore.modmanager.asi;
using ME3ExplorerCore;
using ME3ExplorerCore.Packages;
using ME3ExplorerCore.Unreal;
using Serilog;
using NickStrupat;


namespace ALOTInstallerCore.Steps
{
    /// <summary>
    /// Object that handles the install step of texture installation.
    /// </summary>
    public class InstallStep
    {
        private InstallOptionsPackage package;
        private string memInputPath;

        private ProgressHandler pm { get; }

        /// <summary>
        /// Callback to set what is being installed. Used for UI purposes
        /// </summary>
        public Action<string> SetInstallString { get; set; }

        /// <summary>
        /// Callback to set the first line of text's visibility
        /// </summary>
        public Action<string> SetTopTextCallback { get; set; }

        /// <summary>
        /// Callback to set the second line of text's visibility
        /// </summary>
        public Action<string> SetMiddleTextCallback { get; set; }

        /// <summary>
        /// Callback to set the third line of text
        /// </summary>
        public Action<string> SetBottomTextCallback { get; set; }

        /// <summary>
        /// Callback to set the first line of text's visibility
        /// </summary>
        public Action<bool> SetTopTextVisibilityCallback { get; set; }

        /// <summary>
        /// Callback to set the second line of text's visibility
        /// </summary>
        public Action<bool> SetMiddleTextVisibilityCallback { get; set; }

        /// <summary>
        /// Callback to set the third line of text's visibility
        /// </summary>
        public Action<bool> SetBottomTextVisibilityCallback { get; set; }

        /// <summary>
        /// Callback to indicate that there should be a warning about Origin automatically updating the game for users and that they should never do this. Only triggers on Origin versions of games (ME1/2/3 + 3 steam)
        /// </summary>
        public Action<Enums.MEGame> ShowStorefrontDontClickUpdateCallback { get; set; }

        /// <summary>
        /// Callback for setting the 'overall' progress value, from 0 to 100. Can be used to display things like progressbars. Only works for the main long install step
        /// </summary>
        //public Action<int> SetOverallProgressCallback { get; set; }

        public InstallStep(InstallOptionsPackage package)
        {
            pm = new ProgressHandler();
            this.package = package;
            memInputPath = Path.Combine(Settings.BuildLocation, package.InstallTarget.Game.ToString(), "InstallationPackages");
        }

        void updateStageOfStage()
        {
            SetMiddleTextCallback?.Invoke($"Stage {pm.CurrentStage?.StageUIIndex} of {pm.Stages.Count} ({pm.GetOverallProgress()}%)");
        }

        void updateCurrentStage()
        {
            SetBottomTextCallback?.Invoke($"{pm.CurrentStage?.TaskName} {pm.CurrentStage?.Progress}%");
        }

        public enum InstallResult
        {
            /// <summary>
            /// The default value, indicating an invalid value. This is used essentially for 'null'
            /// </summary>
            UNSET_VALUE,

            InstallFailed_ExistingMarkersFound,
            InstallFailed_TextureExportFixFailed,
            InstallFailed_PreinstallModFailed,

            InstallFailed_MEMCrashed, //Used?
            InstallFailed_MEMReturnedNonZero, //Used?

            // Main install - defined in manifest
            InstallFailed_PrescanFailed,
            InstallFailed_UnpackingDLCFailed,
            InstallFailed_FailedToScanTextures,
            InstallFailed_TextureMapMissing,
            InstallFailed_InvalidTextureMap,
            InstallFailed_FilesAddedAfterTextureScan,
            InstallFailed_FilesRemovedAfterTextureScan,
            InstallFailed_FailedToInstallTextures,
            InstallFailed_FailedToRemoveEmptyMipmaps,
            InstallFailed_FailedToCompressGameFiles,
            InstallFailed_FailedToInstallRemainingTextureMarkers,
            InstallFailed_TextureVerifyFailed,

            InstallFailed_MEMExitedBeforeStageDone,
            InstallFailed_ME1LAAApplyFailed,
            InstallFailed_FailedToApplyTextureInfo,
            InstallFailed_ZipCopyFilesError,
            InstallOK,
            InstallOKWithWarning,
            InstallFailed_UnknownError
        }

        public void InstallTextures(object sender, DoWorkEventArgs doWorkEventArgs)
        {
            // Where the compiled .mem and staged other files will be

            #region setup top text

            string installString = "";
            if (package.InstallALOTUpdate)
            {
                // alot update
                var updateFile = package.FilesToInstall.FirstOrDefault(x => x.AlotVersionInfo.ALOTUPDATEVER != 0);
                if (updateFile != null)
                {
                    installString = $"ALOT {updateFile.AlotVersionInfo.ALOTVER}.{updateFile.AlotVersionInfo.ALOTUPDATEVER}";
                }
            }
            else if (package.InstallALOT)
            {
                // main alot only
                var mainfile = package.FilesToInstall.FirstOrDefault(x => x.AlotVersionInfo.ALOTVER != 0);
                if (mainfile != null)
                {
                    installString = $"ALOT {mainfile.AlotVersionInfo.ALOTVER}.0";
                }
            }

            if (package.InstallMEUITM)
            {
                var meuitmFile = package.FilesToInstall.FirstOrDefault(x => x.AlotVersionInfo.MEUITMVER != 0);
                if (meuitmFile != null)
                {
                    if (installString == "")
                    {
                        installString = $"MEUITM v{meuitmFile.AlotVersionInfo.MEUITMVER}";
                    }
                    else
                    {
                        installString += $" & MEUITM v{meuitmFile.AlotVersionInfo.MEUITMVER}";
                    }
                }
            }

            if (installString == "")
            {
                installString = "texture mods";
            }

            SetInstallString?.Invoke(installString);
            SetTopTextCallback?.Invoke($"Installing {installString} for {package.InstallTarget.Game.ToGameName()}");

            #endregion

            #region Attempt clearing read-write flag

#if WINDOWS
            try
            {
                Utilities.MakeAllFilesInDirReadWrite(package.InstallTarget.TargetPath);
            }
            catch (Exception e)
            {
                Log.Warning($"[AICORE] Exception occurred while trying to make the game directory fully read-write: {e.Message}. We will continue anyways and hope nothing dies");
            }
#endif

            #endregion

            #region Check for existing markers

            if (!checkForExistingMarkers())
            {
                doWorkEventArgs.Result = InstallResult.InstallFailed_ExistingMarkersFound;
                return;
            }



            #endregion

            #region Preinstall ALOV mods

            if (!package.DebugNoInstall)
            {
                if (!applyModManagerMods())
                {
                    doWorkEventArgs.Result = InstallResult.InstallFailed_TextureExportFixFailed;
                    return;
                }
            }

            #endregion

            #region Fix broken mods like Bonus Powers Pack

            if (!applyNeverStreamFixes())
            {
                doWorkEventArgs.Result = InstallResult.InstallFailed_TextureExportFixFailed;
                return;
            }

            #endregion

            #region Apply Post-mars post-hackett fix (ME3 only)

            applyCitadelTransitionFix();

            #endregion

            #region Main installation phase


            SetMiddleTextCallback?.Invoke("Setting up texture installer");
            SetBottomTextCallback?.Invoke("");
            SetTopTextVisibilityCallback?.Invoke(true);
            SetMiddleTextVisibilityCallback?.Invoke(true);
            SetBottomTextVisibilityCallback?.Invoke(true);
            bool doneReached = false;
            StageFailure failure = null;
            void handleIPC(string command, string param)
            {
                switch (command)
                {
                    case "STAGE_ADD": // Add a new stage 
                        {
                            Log.Information("[AICORE] Adding stage to install stages queue: " + param);
                            pm.AddStage(param, package.InstallTarget.Game);
                            break;
                        }
                    case "STAGE_WEIGHT": //Reweight a stage based on how long we think it will take
                        string[] parameters = param.Split(' ');
                        if (parameters.Length > 1)
                        {
                            try
                            {
                                double scale = TryConvert.ToDouble(parameters[1], 1);
                                Log.Information("[AICORE] Reweighting stage " + parameters[0] + " by " + parameters[1]);
                                pm.ScaleStageWeight(parameters[0], scale);
                            }
                            catch (Exception e)
                            {
                                Log.Warning("[AICORE] STAGE_WEIGHT parameter invalid: " + e.Message);
                            }
                        }
                        else
                        {
                            Log.Error("[AICORE] STAGE_WEIGHT IPC requires 2 parameters, STAGE and WEIGHT");
                        }

                        pm.DebugPrintWeights();
                        break;
                    case "STAGE_CONTEXT": //Change to new stage
                        doneReached = pm.CompleteAndMoveToStage(param);
                        updateStageOfStage();
                        updateCurrentStage();
                        break;
                    case "MOD_OVERRIDE":
                        Log.Information($"[AICORE] {param} overrides some textures in the install set");
                        break;
                    case "TASK_PROGRESS": //Report progress of a stage
                        pm.SubmitProgress(int.Parse(param));
                        updateCurrentStage();
                        updateStageOfStage();
                        break;
                    case "PROCESSING_FILE": //Report a file is being processed
                        Log.Information("[AICORE] Processing file " + param);
                        break;
                    default:
                        var failureIPCTriggered = pm?.CurrentStage?.FailureInfos?.FirstOrDefault(x => x.FailureIPCTrigger == command && !x.Warning);
                        if (failureIPCTriggered != null)
                        {
                            // We have encountered a known failure IPC
                            failure = failureIPCTriggered;
                            break;
                        }

                        var warningIPCTriggered = pm?.CurrentStage?.FailureInfos?.FirstOrDefault(x => x.FailureIPCTrigger == command && x.Warning);
                        if (warningIPCTriggered != null)
                        {
                            // We have encountered a known warning IPC
                            Log.Warning($"[AICORE] {warningIPCTriggered.FailureTopText}: {param}");
                            break;
                        }
                        Debug.WriteLine($"Unhandled IPC: {command} {param}");
                        break;
                }
            }

            var computerInfo = new ComputerInfo();
            var cacheAmountPercent = getCacheSizeToUse(computerInfo);

            int currentMemProcessId = 0;
            int lastExitCode = int.MinValue;
            string args = $"--install-mods --gameid {package.InstallTarget.Game.ToGameNum()} --input \"{memInputPath}\" --alot-mode --verify";

            if (package.CompressPackages)
            {
                Log.Information("[AICORE] We will recompress game files in this pass");
                args += " --repack-mode";
            }

            if (cacheAmountPercent != null)
            {
                Log.Information($"[AICORE] Tuning MEM memory usage: will use up to {cacheAmountPercent}% of system memory ({FileSizeFormatter.FormatSize((long)((cacheAmountPercent.Value * 1f / 100) * computerInfo.TotalPhysicalMemory))})");
                args += $" --cache-amount {cacheAmountPercent}";
            }

            args += " --ipc";

            // Uncomment the next 2 lines and then comment out the IPC call to simulate OK install
            if (package.DebugNoInstall)
            {
                lastExitCode = 0;
                doneReached = true;
            }
            else
            {

                MEMIPCHandler.RunMEMIPCUntilExit(args,
                    x => currentMemProcessId = x,
                    handleIPC,
                    x => Log.Error($"[AICORE] StdError: {x}"),
                    x =>
                    {
                        currentMemProcessId = 0;
                        lastExitCode = x;
                    });
            }

            if (lastExitCode != 0)
            {
                Log.Error($@"[AICORE] MEM exited with non zero exit code: {lastExitCode}");
                // Get Stage Failure
                if (failure == null)
                {
                    // Crashed (or unhandled new exit IPC)
                    failure = pm.CurrentStage?.FailureInfos?.FirstOrDefault(x => x.FailureIPCTrigger == null);
                }

                doWorkEventArgs.Result = failure?.FailureResultCode ?? InstallResult.InstallFailed_UnknownError;
                return;
            }

            if (!doneReached)
            {
                Log.Error(@"[AICORE] MEM exited without reaching STAGE_DONE!");
                doWorkEventArgs.Result = InstallResult.InstallFailed_MEMExitedBeforeStageDone;
                return;
            }

            #endregion

            #region Post-main install modifications

            SetMiddleTextCallback?.Invoke("Finishing installation");

            if (!installZipCopyFiles())
            {
                doWorkEventArgs.Result = InstallResult.InstallFailed_ZipCopyFilesError;
                return;
            }

            if (package.InstallTarget.Game == Enums.MEGame.ME1)
            {
                // Apply ME1 LAA
                if (!applyME1LAA())
                {
                    doWorkEventArgs.Result = InstallResult.InstallFailed_ME1LAAApplyFailed;
                    return;
                }
            }

            if (!stampVersionInformation())
            {
                doWorkEventArgs.Result = InstallResult.InstallFailed_FailedToApplyTextureInfo;
                return;
            }

            //At this point we are now OK, errors will result in warnings only.
            bool hasWarning = false;

            hasWarning |= !applyLODs();
            TextureLibrary.AttemptImportUnpackedFiles(memInputPath, package.FilesToInstall.OfType<ManifestFile>().ToList(), package.ImportNewlyUnpackedFiles,
                (file, done, todo) => SetBottomTextCallback?.Invoke($"Optimizing {file} for future installs {(int)(done * 100f / todo)}%"));

            SetBottomTextCallback?.Invoke("Installing binkw32 ASI loader");

            hasWarning |= !package.InstallTarget.InstallBinkBypass();
            if (package.InstallTarget.Game == Enums.MEGame.ME3)
            {
                // Install ASIs.
                // Need ASI manager code from Mod Manager once it is ready
                if (package.InstallTarget.Supported)
                {
                    SetBottomTextCallback?.Invoke("Installing troubleshooting files");
                    ASIManager.LoadManifest();
                    ASIManager.InstallASIToTargetByGroupID(9, package.InstallTarget); //AutoTOC
                    ASIManager.InstallASIToTargetByGroupID(8, package.InstallTarget); //ME3Logger Truncating
                }
            }

            #endregion

            #region Cleanup
            try
            {
                Utilities.DeleteFilesAndFoldersRecursively(memInputPath);
            }
            catch (Exception e)
            {
                Log.Error($"[AICORE] Unable to delete installation packages at {memInputPath}: {e.Message}");
            }
            #endregion

            #region Show Ending UI
            showOnlineStorefrontNoUpdateScreen();

            SetTopTextCallback?.Invoke($"Installation of {installString}");
            if (hasWarning)
            {
                SetMiddleTextCallback?.Invoke("completed with warnings");
                SetBottomTextCallback?.Invoke("See installer log for more info");
                SetBottomTextVisibilityCallback?.Invoke(true);

            }
            else
            {
                SetMiddleTextCallback?.Invoke("was successful");
                SetBottomTextVisibilityCallback?.Invoke(false);
            }
            SetTopTextVisibilityCallback?.Invoke(true);
            SetMiddleTextVisibilityCallback?.Invoke(true);
            #endregion
         
            doWorkEventArgs.Result = hasWarning ? InstallResult.InstallOKWithWarning : InstallResult.InstallOK;
        }

        private bool checkForExistingMarkers()
        {
            if (package.InstallTarget.GetInstalledALOTInfo() != null) return true; //Marker exists. We shouldn't check if there's existing markers
            SetMiddleTextVisibilityCallback?.Invoke(false);
            string args = $"--check-for-markers --gameid {package.InstallTarget.Game.ToGameNum()} --ipc";
            if (Settings.DebugLogs)
            {
                args += " --debug-logs";
            }

            List<string> badFiles = new List<string>();

            void handleIPC(string command, string param)
            {
                switch (command)
                {
                    case "TASK_PROGRESS":
                        SetBottomTextCallback?.Invoke($"Checking game files for existing texture markers {param}%");
                        break;
                    case "FILENAME":

                        break;
                    case "ERROR_FILEMARKER_FOUND":
                        Log.Error("[AICORE] File was part of a different texture installation: " + param);
                        badFiles.Add(param);
                        break;
                    default:
                        Debug.WriteLine($"Unhandled IPC: {command} {param}");
                        break;
                }
            }

            int currentMemProcessId = 0;
            int lastExitCode = int.MinValue;
            MEMIPCHandler.RunMEMIPCUntilExit(args,
                x => currentMemProcessId = x,
                handleIPC,
                x => Log.Error($"[AICORE] StdError: {x}"),
                x =>
                {
                    currentMemProcessId = 0;
                    lastExitCode = x;
                });

            return !badFiles.Any();
        }

        private int? getCacheSizeToUse(ComputerInfo ci)
        {
            var totalMem = ci.TotalPhysicalMemory;
            var availableMem = ci.AvailablePhysicalMemory - (1024 * 1024 * 1330); //Available memory - 1.3GiB
            if (totalMem > 0 && availableMem > 0 && availableMem < totalMem)
            {
                return (int?)(availableMem * 100 / totalMem);
            }
            else
            {
                return null;
            }
        }

        private void showOnlineStorefrontNoUpdateScreen()
        {
            //Check if origin
            string originTouchupFile = Path.Combine(package.InstallTarget.TargetPath, "__Installer", "Touchup.exe");
            if (File.Exists(originTouchupFile))
            {
                //origin based
                ShowStorefrontDontClickUpdateCallback?.Invoke(package.InstallTarget.Game);
            }
        }

        private void applyCitadelTransitionFix()
        {
            if (package.InstallTarget.Game == Enums.MEGame.ME3)
            {
                SetMiddleTextVisibilityCallback?.Invoke(false);

                SetBottomTextCallback?.Invoke("Fixing Mars to Citadel transition");
                Log.Information("[AICORE] Fixing post-mars hackett cutscene memory issue");
                CoreLib.InitLib(package.UiThreadScheduler, x =>
                {
                    Log.Error($"Error saving package: {x}");
                });
                var biogamePath = Path.Combine(package.InstallTarget.TargetPath, "BIOGame");

                #region BioA_CitHub fix

                var bioACithubs = Directory.GetFiles(biogamePath, "BioA_CitHub.pcc", SearchOption.AllDirectories);
                foreach (var bioa_cithubPath in bioACithubs)
                {
                    try
                    {
                        if (File.Exists(bioa_cithubPath))
                        {
                            var bioa_cithub = MEPackageHandler.OpenMEPackage(bioa_cithubPath);
                            var trigStream1 = bioa_cithub.GetUExport(8);
                            var propsT = trigStream1.GetProperties();
                            var streamStates = trigStream1.GetProperty<ArrayProperty<StructProperty>>("StreamingStates");
                            // Clear preloading
                            Log.Information("[AICORE] Clear LoadChunkNames from BioA_CitHub");
                            if (streamStates != null && streamStates.Count > 1)
                            {
                                streamStates[1].GetProp<ArrayProperty<NameProperty>>("LoadChunkNames").Clear();

                                // Clear visible asset
                                var visibleChunkNames = streamStates[2].GetProp<ArrayProperty<NameProperty>>("VisibleChunkNames");
                                if (visibleChunkNames != null)
                                {
                                    for (int i = visibleChunkNames.Count - 1; i > 0; i--)
                                    {
                                        if (visibleChunkNames[i].Value == "BioA_CitHub_Dock_Det")
                                        {
                                            Log.Information(
                                                "[AICORE] Remove BioA_CitHub_Dock_Det from BioA_CitHub VisibleChunkNames(8)");
                                            visibleChunkNames.RemoveAt(i);
                                        }
                                    }
                                }

                                trigStream1.WriteProperty(streamStates);
                            }

                            var trigStream2 = bioa_cithub.GetUExport(15);
                            streamStates = trigStream2.GetProperty<ArrayProperty<StructProperty>>("StreamingStates");

                            // Cleanup visible assets
                            if (streamStates != null && streamStates.Any())
                            {
                                var visibleChunkNames = streamStates[0].GetProp<ArrayProperty<NameProperty>>("VisibleChunkNames");
                                if (visibleChunkNames != null)
                                {
                                    for (int i = visibleChunkNames.Count - 1; i > 0; i--)
                                    {
                                        if (visibleChunkNames[i].Value == "BioA_Nor_204Conference" ||
                                            visibleChunkNames[i].Value == "BioA_Nor_204WarRoom")
                                        {
                                            Log.Information("[AICORE] Remove " + visibleChunkNames[i].Value + " from BioA_CitHub VisibleChunkNames(15)");
                                            visibleChunkNames.RemoveAt(i);
                                        }
                                    }
                                }

                                trigStream2.WriteProperty(streamStates);
                            }

                            Log.Information("[AICORE] Saving package: " + bioa_cithubPath);
                            bioa_cithub.Save();
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[AICORE] Error applying fix for BioA_Cithub to file ({bioa_cithubPath}) fix (post mars crash fix): " + e.Message);
                    }
                }

                #endregion
                #region BioD_CitHub fix
                {
                    var bioDCithubs = Directory.GetFiles(biogamePath, "BioD_CitHub.pcc", SearchOption.AllDirectories);
                    foreach (var biod_cithubPath in bioDCithubs)
                    {
                        try
                        {
                            if (File.Exists(biod_cithubPath))
                            {
                                var biod_cithub = MEPackageHandler.OpenMEPackage(biod_cithubPath);
                                var trigStream1 = biod_cithub.GetUExport(162);
                                var streamStates = trigStream1.GetProperty<ArrayProperty<StructProperty>>("StreamingStates");
                                if (streamStates != null && streamStates.Count > 2)
                                {
                                    Log.Information(@"[AICORE] Applying BioD_CitHub post-mars fix: " + biod_cithubPath);

                                    // Clear preloading
                                    Log.Information("[AICORE] Clear LoadChunkNames from BioD_CitHub");
                                    streamStates[1].GetProp<ArrayProperty<NameProperty>>("LoadChunkNames").Clear();

                                    // Clear visible asset
                                    var visibleChunkNames = streamStates[2].GetProp<ArrayProperty<NameProperty>>("VisibleChunkNames");
                                    if (visibleChunkNames != null)
                                    {
                                        for (int i = visibleChunkNames.Count - 1; i > 0; i--)
                                        {
                                            if (visibleChunkNames[i].Value == "BioH_Marine" ||
                                                visibleChunkNames[i].Value == "BioD_CitHub_Dock")
                                            {
                                                Log.Information("[AICORE] Remove " + visibleChunkNames[i].Value +
                                                                " from BioA_CitHub VisibleChunkNames(8)");
                                                visibleChunkNames.RemoveAt(i);
                                            }
                                        }
                                    }

                                    trigStream1.WriteProperty(streamStates);
                                    Log.Information("[AICORE] Saving package: " + biod_cithubPath);
                                    biod_cithub.Save();
                                }
                                else
                                {
                                    Log.Information(@"[AICORE] Not applying BioD_Cithub fix to " + biod_cithubPath);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error($"[AICORE] Error applying fix for BioD_Cithub to file ({biod_cithubPath}) fix (post mars crash fix): " +
                                      e.Message);
                        }
                    }
                }

                #endregion
                Log.Information("[AICORE] Finished fixing post-mars hackett cutscene memory issue");
            }
        }


        private bool applyNeverStreamFixes()
        {
            if (package.InstallTarget.Game >= Enums.MEGame.ME2)
            {
                SetMiddleTextCallback?.Invoke("Checking mods for known issues");
                SetMiddleTextVisibilityCallback?.Invoke(true);
                SetBottomTextVisibilityCallback?.Invoke(false);

                string dlcPath = Path.Combine(package.InstallTarget.TargetPath, "BIOGame", "DLC");
                package.InstallTarget.PopulateDLCMods(false);
                var listOfItemsToFix = package.InstallTarget.Game == Enums.MEGame.ME2 ? ManifestHandler.MasterManifest.ME2DLCRequiringTextureExportFixes : ManifestHandler.MasterManifest.ME3DLCRequiringTextureExportFixes;
                foreach (var d in package.InstallTarget.UIInstalledDLCMods)
                {
                    if (listOfItemsToFix.Contains(d.DLCFolderName, StringComparer.InvariantCultureIgnoreCase))
                    {
                        // Fix required
                        Log.Information("[AICORE] DLC requires texture fixes: " + d.DLCFolderName);
                        SetBottomTextCallback?.Invoke($"Fixing texture exports in {d.DLCFolderName}");
                        string args = $"--fix-textures-property --gameid {package.InstallTarget.Game.ToGameNum()} --filter \"{d.DLCFolderName}\" --ipc";
                        int resultCode = -1;
                        MEMIPCHandler.RunMEMIPCUntilExit(args,
                            null,
                            null,
                            x => Log.Error($"[AICORE] StdError fixing DLC foldernames for {d.DLCFolderName}: {x}"),
                            x => resultCode = x);
                        if (resultCode != 0)
                        {
                            Log.Error($"[AICORE] Non zero result code fixing textures for {d.DLCFolderName}: {resultCode}");
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private bool installZipCopyFiles()
        {
            SetBottomTextCallback?.Invoke("Installing additional texture mod components");
            try
            {
                //things like soft shadows, reshade
                foreach (InstallerFile af in package.FilesToInstall)
                {
                    if (af is ManifestFile mf)
                    {
                        if (mf.CopyFiles != null)
                        {
                            foreach (CopyFile cf in mf.CopyFiles)
                            {
                                if (cf.IsSelectedForInstallation())
                                {
                                    string installationPath = Path.Combine(package.InstallTarget.TargetPath, cf.GameDestinationPath);
                                    File.Copy(cf.StagedPath, installationPath, true);
                                    Log.Information($"[AICORE] Installed copyfile: {cf.ChoiceTitle} {cf.StagedPath} to {installationPath}");
                                }
                            }
                        }

                        if (mf.ZipFiles != null)
                        {
                            foreach (ZipFile zf in mf.ZipFiles)
                            {
                                if (zf.IsSelectedForInstallation())
                                {
                                    SetBottomTextCallback?.Invoke($"Installing {zf.ChoiceTitle}");
                                    string installationPath = Path.Combine(package.InstallTarget.TargetPath, zf.GameDestinationPath);
                                    int extractcode = -1;
                                    Directory.CreateDirectory(installationPath);
                                    MEMIPCHandler.RunMEMIPCUntilExit($"--unpack-archive --input \"{zf.StagedPath}\" --output \"{installationPath}\" --ipc",
                                        null,
                                        null,
                                        x => Log.Error($"[AICORE] StdError on {zf.StagedPath}: {x}"),
                                        x => extractcode = x);
                                    if (extractcode == 0)
                                    {
                                        Log.Information($"[AICORE] Installed zipfile: {zf.ChoiceTitle} {zf.StagedPath} to {installationPath}");

                                    }
                                    else
                                    {
                                        Log.Error("[AICORE] Extraction of " + zf.ChoiceTitle + " failed with code " + extractcode);
                                    }

                                    if (package.InstallTarget.Game == Enums.MEGame.ME1 && zf.DeleteShaders)
                                    {
                                        string documents = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                                        string localusershaderscache = Path.Combine(documents, @"BioWare\Mass Effect\Published\CookedPC\LocalShaderCache-PC-D3D-SM3.upk");
                                        if (File.Exists(localusershaderscache))
                                        {
                                            File.Delete(localusershaderscache);
                                            Log.Information("[AICORE] Deleted user localshadercache: " + localusershaderscache);
                                        }
                                        else
                                        {
                                            Log.Warning("[AICORE] unable to delete user local shadercache, it does not exist: " + localusershaderscache);
                                        }

                                        string gamelocalshadercache = Path.Combine(package.InstallTarget.TargetPath, @"BioGame\CookedPC\LocalShaderCache-PC-D3D-SM3.upk");
                                        if (File.Exists(gamelocalshadercache))
                                        {
                                            File.Delete(gamelocalshadercache);
                                            Log.Information("[AICORE] Deleted game localshadercache: " + gamelocalshadercache);
                                        }
                                        else
                                        {
                                            Log.Warning("[AICORE] Unable to delete game localshadercache, it does not exist: " + gamelocalshadercache);
                                        }
                                    }

                                    //MEUITM SPECIFIC FIX
                                    //REMOVE ONCE THIS IS FIXED IN FUTURE MEUITM
                                    if (mf.AlotVersionInfo.MEUITMVER != 0 && !zf.MEUITMSoftShadows)
                                    {
                                        //reshade
                                        var d3d9ini = Path.Combine(package.InstallTarget.TargetPath, "Binaries", "d3d9.ini");
                                        if (File.Exists(d3d9ini))
                                        {
                                            try
                                            {
                                                DuplicatingIni shaderConf = DuplicatingIni.LoadIni(d3d9ini);
                                                shaderConf["GENERAL"]["TextureSearchPaths"].Value = Path.Combine(package.InstallTarget.TargetPath, "Binaries", "reshade-shaders", "Textures");
                                                shaderConf["GENERAL"]["EffectSearchPaths"].Value = Path.Combine(package.InstallTarget.TargetPath, "Binaries", "reshade-shaders", "Shaders");
                                                shaderConf["GENERAL"]["PresetFiles"].Value = Path.Combine(package.InstallTarget.TargetPath, "Binaries", "MassEffect.ini");
                                                File.WriteAllText(d3d9ini, shaderConf.ToString());
                                                Log.Information("[AICORE] Corrected MEUITM shader ini");
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.Error("[AICORE] Error fixing MEUITM shader ini: " + ex.Message);
                                                return false;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"[AICORE] Error applying copy/zip files: {e.Message}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Applies the texture installation information marker
        /// </summary>
        /// <returns></returns>
        private bool stampVersionInformation()
        {
            try
            {
                SetBottomTextCallback?.Invoke("Updating texture installation marker");
                TextureModInstallationInfo tmii = TextureModInstallationInfo.CalculateMarker(package.InstallTarget.GetInstalledALOTInfo(), package.FilesToInstall);
                tmii.ALOT_INSTALLER_VERSION_USED = (short)Assembly.GetEntryAssembly().GetName().Version.Build;
                int version = 0;
                // If the current version doesn't support the --version --ipc, we just assume it is 0.
                tmii.MEM_VERSION_USED = MEMIPCHandler.GetMemVersion();
                package.InstallTarget.StampTextureModificationInfo(tmii);
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[AICORE] Error setting texture installation marker: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Applies the texture level-of-detail settings
        /// </summary>
        private bool applyLODs()
        {
            SetBottomTextCallback?.Invoke("Applying graphics settings");
            Log.Information("[AICORE] Updating texture lods");

            LodSetting setting = LodSetting.Vanilla;
            var meuitmSoftShadows = package.FilesToInstall.Any(x =>
                x is ManifestFile mf && mf.ZipFiles.Any(x => x.IsSelectedForInstallation() && x.MEUITMSoftShadows));
            if (meuitmSoftShadows)
            {
                setting |= LodSetting.SoftShadows;
                Log.Information("[AICORE]  > MEUITM Soft Shadows");
            }

            if (package.Limit2K)
            {
                setting |= LodSetting.TwoK;
                Log.Information("[AICORE]  > Using 2K lods");
            }
            else
            {
                setting |= LodSetting.FourK;
            }

            return MEMIPCHandler.SetLODs(package.InstallTarget.Game, setting);
        }

        /// <summary>
        /// Applies the LAA patch as well as the Mass Effect -> Mass_Effect product name patch to bypass the Windows compatibility DB
        /// </summary>
        /// <returns></returns>
        private bool applyME1LAA()
        {
            SetBottomTextCallback?.Invoke("Applying LAA");
            Log.Information("[AICORE] Applying LAA/Admin to game executable");
            string args = "--apply-me1-laa";
            int exitcode = -1;
            // We don't care about IPC on this
            MEMIPCHandler.RunMEMIPCUntilExit(args,
                null,
                null,
                x => Log.Error($"[AICORE] StdError setting LAA: {x}"),
                x => exitcode = x); //Change to catch exit code of non zero.        

            return exitcode == 0;
        }

        /// <summary>
        /// Applies specifically supported Mod Manager mods (technically they aren't mod managerm ods...)
        /// </summary>
        private bool applyModManagerMods()
        {
            //Apply ALOT-verified Mod Manager mods that we support installing
            SetMiddleTextVisibilityCallback?.Invoke(true);

            foreach (var modAddon in package.FilesToInstall.OfType<PreinstallMod>())
            {
                SetMiddleTextCallback?.Invoke($"Installing {modAddon.FriendlyName}");
                SetBottomTextCallback?.Invoke("Loading archive");

                // We will stage the archive contents into game dir to start with, so moving files should be fast.
                var stagingPath = Directory.CreateDirectory(Path.Combine(package.InstallTarget.TargetPath, "ModExtractStaging")).FullName;

                void handleIPC(string command, string param)
                {
                    switch (command)
                    {
                        case "TASK_PROGRESS":
                            SetBottomTextCallback?.Invoke($"Extracting files {param}%");
                            break;
                        case "FILENAME":
                            Log.Information($"[AICORE] Extracting file from archive: {param}");
                            break;
                        default:
                            Debug.WriteLine($"Unhandled IPC: {command} {param}");
                            break;
                    }
                }

                int exitcode = -1;
                MEMIPCHandler.RunMEMIPCUntilExit($"--unpack-archive --input \"{modAddon.GetUsedFilepath()}\" --output \"{stagingPath}\" --ipc",
                    null,
                    handleIPC,
                    x => Log.Error($"[AICORE] StdError on {modAddon.FriendlyName}: {x}"),
                    x => exitcode = x); //Change to catch exit code of non zero.
                if (exitcode != 0)
                {
                    Log.Error($"[AICORE] MassEffectModderNoGui exited with non zero code {exitcode} extracting {modAddon.FriendlyName}");
                    return false;
                }

                SetBottomTextCallback?.Invoke("Installing files");

                //Check requirements for this extraction rule to fire.
                var dlcDirectory = package.InstallTarget.Game == Enums.MEGame.ME1
                    ? Path.Combine(package.InstallTarget.TargetPath, "DLC")
                    : Path.Combine(package.InstallTarget.TargetPath, "BioGame", "DLC");

                try
                {
                    foreach (var extractionRedirect in modAddon.ExtractionRedirects)
                    {
                        //dlc is required (all in list)
                        if (extractionRedirect.OptionalRequiredDLC != null)
                        {
                            List<string> requiredDlc = extractionRedirect.OptionalRequiredDLC.Split(';').ToList();
                            List<string> requiredFiles = new List<string>();
                            List<long> requiredFilesSizes = new List<long>();


                            if (extractionRedirect.OptionalRequiredFiles != null)
                            {
                                requiredFiles = extractionRedirect.OptionalRequiredFiles.Split(';').ToList();
                                if (extractionRedirect.OptionalRequiredFilesSizes != null)
                                {
                                    //parse required sizes list. This can be null which means we don't check the list of file sizes in the list
                                    requiredFilesSizes = extractionRedirect.OptionalRequiredFilesSizes.Split(';').Select(x => long.Parse(x)).ToList();
                                }
                            }

                            //check if any required dlc is missing
                            if (requiredDlc.Any(x => !Directory.Exists(Path.Combine(dlcDirectory, x))))
                            {
                                Log.Information($"[AICORE] {extractionRedirect.LoggingName}: Extraction rule is not applicable to this setup. Rule requires all of the DLC: {extractionRedirect.OptionalRequiredDLC}");
                                continue;
                            }

                            //Check if any required file is missing
                            if (requiredFiles.Any(x => !File.Exists(Path.Combine(dlcDirectory, x))))
                            {
                                Log.Information($"[AICORE] {extractionRedirect.LoggingName}: Extraction rule is not applicable to this setup. At least one file for this rule was not found: {extractionRedirect.OptionalRequiredFiles}");
                                continue;
                            }

                            //Check if any required file size is wrong
                            if (requiredFilesSizes.Any())
                            {
                                bool doNotInstall = false;
                                for (int i = 0; i < requiredFilesSizes.Count; i++)
                                {
                                    string file = requiredFiles[i];
                                    long size = requiredFilesSizes[i];
                                    string finalPath = Path.Combine(dlcDirectory, file);
                                    var info = new FileInfo(finalPath);
                                    if (info.Length != size)
                                    {
                                        Log.Information($"[AICORE] {extractionRedirect.LoggingName}: Extraction rule is not applicable to this setup, file size for file {file} does not match rule.");
                                        doNotInstall = true;
                                        break;
                                    }
                                }

                                if (doNotInstall)
                                {
                                    continue;
                                }
                            }
                        }

                        //dlc required (any in list)
                        if (extractionRedirect.OptionalAnyDLC != null)
                        {
                            List<string> anyDLC = extractionRedirect.OptionalAnyDLC.Split(';').ToList();
                            //check if all dlc is missing
                            if (anyDLC.All(x => !Directory.Exists(Path.Combine(dlcDirectory, x))))
                            {
                                Log.Information($"AICORE] {extractionRedirect.LoggingName}: Extraction rule is not applicable to this setup. Rule requires at least one of the DLC: {extractionRedirect.OptionalRequiredDLC}");
                                continue;
                            }
                        }

                        Log.Information("[AICORE] Applying extraction rule: " + extractionRedirect.LoggingName);
                        var rootPath = Path.Combine(stagingPath, extractionRedirect.ArchiveRootPath);
                        var filesToMove = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);

                        var ingameDestination = Path.Combine(package.InstallTarget.TargetPath, extractionRedirect.RelativeDestinationDirectory);
                        if (extractionRedirect.IsDLC && Directory.Exists(ingameDestination))
                        {
                            //delete first
                            Utilities.DeleteFilesAndFoldersRecursively(ingameDestination);
                        }

                        Directory.CreateDirectory(ingameDestination);

                        foreach (var file in filesToMove)
                        {
                            string relativePath = file.Substring(rootPath.Length + 1);
                            var extension = Path.GetExtension(relativePath);
                            if (extension.Equals(".pcc", StringComparison.InvariantCultureIgnoreCase) ||
                                extension.Equals(".tfc", StringComparison.InvariantCultureIgnoreCase))
                            {
                                Log.Information("[AICORE] Skipping file that that cannot be installed after alot: " + relativePath);
                                continue;
                            }

                            string finalDestinationPath = Path.Combine(ingameDestination, relativePath);
                            if (File.Exists(finalDestinationPath))
                            {
                                Log.Information("[AICORE] Deleting existing file before move: " + finalDestinationPath);
                                File.Delete(finalDestinationPath);
                            }

                            Log.Information($"[AICORE] Moving staged file into game directory: {file} -> {finalDestinationPath}");
                            Directory.CreateDirectory(Directory.GetParent(finalDestinationPath).FullName);
                            File.Move(file, finalDestinationPath);
                        }

                        if (extractionRedirect.IsDLC)
                        {
                            //Write a _metacmm.txt file
                            var metacmm = Path.Combine(ingameDestination, "_metacmm.txt");
                            string contents = $"{extractionRedirect.LoggingName}\n{extractionRedirect.ModVersion}\n{Assembly.GetEntryAssembly().FullName} {System.Reflection.Assembly.GetEntryAssembly().GetName().Version}\n{Guid.NewGuid().ToString()}";
                            File.WriteAllText(metacmm, contents);
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"[AICORE] Error installing preinstall mod {modAddon.FriendlyName}: {e.Message}. ModExtractingStaging at root of install folder may need to be manually deleted");
                    return false;
                }

                try
                {
                    Utilities.DeleteFilesAndFoldersRecursively(stagingPath);
                }
                catch (Exception e)
                {
                    Log.Error($"[AICORE] Error deleting staging folder for {modAddon.FriendlyName}: {e.Message}");
                    // This is technically not an error. So we will not return this as an error.
                }
            }

            return true;
        }
    }
}