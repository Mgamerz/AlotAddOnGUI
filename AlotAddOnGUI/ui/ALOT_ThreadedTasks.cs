﻿using AlotAddOnGUI.classes;
using AlotAddOnGUI.music;
using AlotAddOnGUI.ui;
using ByteSizeLib;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.WindowsAPICodePack.Taskbar;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Serilog;
using SlavaGu.ConsoleAppLauncher;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Xml.Linq;
using System.Xml.XPath;

namespace AlotAddOnGUI
{
    public partial class MainWindow : MetroWindow
    {
        static Random RANDOM = new Random();
        private int INSTALLING_THREAD_GAME;
        private List<AddonFile> ADDONFILES_TO_BUILD;
        private List<AddonFile> ADDONFILES_TO_INSTALL;
        public const string UPDATE_TASK_PROGRESS = "UPDATE_TASK_PROGRESS";
        public const string UPDATE_OVERALL_TASK = "UPDATE_OVERALL_TASK";
        private const int INSTALL_OK = 1;
        private const int RESULT_ME1LAA_FAILED = -43;
        private const int RESULT_TEXTUREINSTALL_FAILED = -42;
        private WaveOut waveOut;
        private NAudio.Vorbis.VorbisWaveReader vorbisStream;
        private const int RESULT_SCAN_REMOVE_FAILED = -41;
        private const int RESULT_UNPACK_FAILED = -40;
        public const string RESTORE_FAILED_COULD_NOT_DELETE_FOLDER = "RESTORE_FAILED_COULD_NOT_DELETE_FOLDER";
        public string CurrentTask;
        public int CurrentTaskPercent;
        public const string UPDATE_SUBTASK = "UPDATE_SUBTASK";
        private int INSTALL_STAGE = 0;
        public const string UPDATE_STAGE_LABEL = "UPDATE_STAGE_LABEL";
        private int STAGE_COUNT;
        public const string HIDE_STAGE_LABEL = "HIDE_STAGE_LABEL";
        public const string UPDATE_HEADER_LABEL = "UPDATE_HEADER_LABEL";
        public static ALOTVersionInfo CURRENTLY_INSTALLED_ME1_ALOT_INFO;
        public static ALOTVersionInfo CURRENTLY_INSTALLED_ME2_ALOT_INFO;
        public static ALOTVersionInfo CURRENTLY_INSTALLED_ME3_ALOT_INFO;
        int USERFILE_INDEX = 100;


        private bool WARN_USER_OF_EXIT = false;
        private List<string> TIPS_LIST;
        private const string SET_OVERALL_PROGRESS = "SET_OVERALL_PROGRESS";
        private const string HIDE_LOD_LIMIT = "HIDE_LOD_LIMIT";
        Stopwatch stopwatch;
        private string MAINTASK_TEXT;
        private string CURRENT_USER_BUILD_FILE = "";

        public bool BUILD_ALOT { get; private set; }

        private bool BUILD_ADDON_FILES = false;
        private bool BUILD_USER_FILES = false;
        private FadeInOutSampleProvider fadeoutProvider;
        private bool MusicPaused;
        private const string SET_VISIBILE_ITEMS_LIST = "SET_VISIBILE_ITEMS_LIST";

        public bool MusicIsPlaying { get; private set; }

        private bool ExtractAddon(AddonFile af)
        {
            string stagingdirectory = EXE_DIRECTORY + ADDON_STAGING_DIR + "\\";

            string fileToUse = af.Filename;
            bool isSingleFileMode = false;
            if (File.Exists(EXE_DIRECTORY + "Downloaded_Mods\\" + af.UnpackedSingleFilename))
            {
                isSingleFileMode = true;
                fileToUse = af.UnpackedSingleFilename;
            }

            string prefix = "[" + Path.GetFileNameWithoutExtension(fileToUse) + "] ";
            Log.Information(prefix + "Processing extraction on " + fileToUse);
            string fileextension = System.IO.Path.GetExtension(fileToUse);
            ulong freeBytes;
            ulong diskSize;
            ulong totalFreeBytes;

            string processingStr = "Processing " + af.FriendlyName;
            long length = 0L;
            if (af.UserFile)
            {
                length = new System.IO.FileInfo(af.UserFilePath).Length;

            }
            else
            {
                length = new System.IO.FileInfo(EXE_DIRECTORY + "Downloaded_Mods\\" + fileToUse).Length;
            }
            processingStr += " (" + ByteSize.FromBytes(length) + ")";

            TasksDisplayEngine.SubmitTask(processingStr);
            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, processingStr));
            string extractpath = EXE_DIRECTORY + "Extracted_Mods\\" + System.IO.Path.GetFileNameWithoutExtension(fileToUse);
            string extractSource = EXE_DIRECTORY + "Downloaded_Mods\\" + fileToUse;
            if (af.UserFile)
            {
                extractSource = af.UserFilePath;
                extractpath = USER_FULL_STAGING_DIRECTORY + System.IO.Path.GetFileNameWithoutExtension(fileToUse);
                Directory.CreateDirectory(extractpath);
            }

            try
            {
                switch (fileextension)
                {
                    case ".7z":
                    case ".zip":
                    case ".rar":
                        {
                            af.ReadyStatusText = "Extracting";
                            af.SetWorking();
                            //Extract file


                            Log.Information(prefix + "Extracting file: " + extractSource);
                            string exe = BINARY_DIRECTORY + "7z.exe";
                            string args = "x \"" + extractSource + "\" -aoa -r -o\"" + extractpath + "\"";
                            Utilities.runProcess(exe, args);
                            if (af.UserFile)
                            {
                                af.ReadyStatusText = "Waiting for Addon to complete build";
                                af.SetIdle();
                                BuildWorker.ReportProgress(0, new ThreadCommand(INCREMENT_COMPLETION_EXTRACTION));
                                return true;
                            }
                            //get free space for debug purposes
                            Utilities.GetDiskFreeSpaceEx(stagingdirectory, out freeBytes, out diskSize, out totalFreeBytes);
                            Log.Information("[SIZE] ADDONEXTRACTFINISH Free Space on current drive: " + ByteSize.FromBytes(freeBytes) + " " + freeBytes);
                            var moveableFiles = Directory.EnumerateFiles(extractpath, "*.*", SearchOption.AllDirectories) //<--- .NET 4.5
                                .Where(file => file.ToLower().EndsWith("tpf") || file.ToLower().EndsWith("mem"))
                                .ToList();
                            if (moveableFiles.Count() > 0)
                            {
                                //check for copy directly items first, and move them.

                                foreach (string moveableFile in moveableFiles)
                                {
                                    string name = Utilities.GetRelativePath(moveableFile, extractpath);
                                    foreach (PackageFile pf in af.PackageFiles)
                                    {
                                        string fname = Path.GetFileName(name);
                                        if (pf.MoveDirectly && pf.SourceName == name && pf.AppliesToGame(CURRENT_GAME_BUILD) && af.ALOTVersion > 0)
                                        {
                                            //It's an ALOT file. We will move this directly to the output directory.
                                            Log.Information("ALOT MAIN FILE - moving to output: " + fname);
                                            string movename = getOutputDir(CURRENT_GAME_BUILD) + "000_" + fname;
                                            if (File.Exists(movename))
                                            {
                                                File.Delete(movename);
                                            }
                                            File.Move(moveableFile, movename);
                                            pf.Processed = true; //no more ops on this package file.
                                            break;
                                        }
                                        if (pf.MoveDirectly && pf.SourceName == name && pf.AppliesToGame(CURRENT_GAME_BUILD) && af.ALOTUpdateVersion > 0)
                                        {
                                            //It's an ALOT update file. We will move this directly to the output directory.
                                            Log.Information("ALOT UPDATE FILE - moving to output: " + fname);
                                            string movename = getOutputDir(CURRENT_GAME_BUILD) + "001_" + fname;
                                            if (File.Exists(movename))
                                            {
                                                File.Delete(movename);
                                            }
                                            File.Move(moveableFile, movename); pf.Processed = true; //no more ops on this package file.
                                            break;
                                        }
                                        if (pf.MoveDirectly && pf.SourceName == name && pf.AppliesToGame(CURRENT_GAME_BUILD) && name.ToLower().EndsWith(".mem"))
                                        {
                                            //It's a already built MEM file. Move MEM to build folder
                                            Log.Information("MoveDirectly on MEM file specified - moving MEM to output: " + fname);
                                            int fileprefix = Interlocked.Increment(ref PREBUILT_MEM_INDEX);
                                            string paddedVer = fileprefix.ToString("000");
                                            string movename = getOutputDir(CURRENT_GAME_BUILD) + paddedVer + "_" + fname;
                                            if (File.Exists(movename))
                                            {
                                                File.Delete(movename);
                                            }
                                            File.Move(moveableFile, movename); pf.Processed = true; //no more ops on this package file.
                                            break;
                                        }
                                        if (pf.MoveDirectly && pf.SourceName == name && pf.AppliesToGame(CURRENT_GAME_BUILD))
                                        {
                                            Log.Information("MoveDirectly specified - moving TPF/MEM to staging: " + fname);
                                            File.Move(moveableFile, ADDON_FULL_STAGING_DIRECTORY + fname);
                                            pf.Processed = true; //no more ops on this package file.
                                            break;
                                        }
                                        if (pf.CopyDirectly && pf.SourceName == name && pf.AppliesToGame(CURRENT_GAME_BUILD))
                                        {
                                            Log.Information("CopyDirectly specified - copy TPF/MEM to staging: " + fname);
                                            File.Copy(moveableFile, ADDON_FULL_STAGING_DIRECTORY + fname, true);
                                            pf.Processed = true; //We will still extract this as it is a copy step.
                                            break;
                                        }
                                        if (pf.Delete && pf.SourceName == name && pf.AppliesToGame(CURRENT_GAME_BUILD))
                                        {
                                            Log.Information("Delete specified - deleting unused TPF/MEM: " + fname);
                                            File.Delete(moveableFile);
                                            pf.Processed = true; //no more ops on this package file.
                                            break;
                                        }
                                    }
                                }

                                //Extract the TPFs
                                exe = BINARY_DIRECTORY + MEM_EXE_NAME;
                                args = "-extract-tpf \"" + extractpath + "\" \"" + extractpath + "\"";
                                Utilities.runProcess(exe, args);
                            }

                            string[] modfiles = Directory.GetFiles(extractpath, "*.mod", SearchOption.AllDirectories);
                            if (modfiles.Length > 0)
                            {
                                if (af.ProcessAsModFile)
                                {
                                    //Move to MEM_STAGING

                                    foreach (string modfile in modfiles)
                                    {
                                        Log.Information(prefix + "Copying modfile to staging directory (ProcessAsModFile=true): " + modfile);
                                        File.Copy(modfile, ADDON_FULL_STAGING_DIRECTORY + Path.GetFileName(modfile), true);
                                    }
                                }
                                else
                                {
                                    //Extract the MOD
                                    Log.Information("Extracting modfiles in directory: " + extractpath);

                                    exe = BINARY_DIRECTORY + MEM_EXE_NAME;
                                    args = "-extract-mod " + CURRENT_GAME_BUILD + " \"" + extractpath + "\" \"" + extractpath + "\"";
                                    Utilities.runProcess(exe, args);
                                }
                            }
                            string[] memfiles = Directory.GetFiles(extractpath, "*.mem");
                            if (memfiles.Length > 0)
                            {
                                //Copy MEM File - append game
                                foreach (string memfile in memfiles)

                                {
                                    Log.Information("-- Subtask: Move MEM file to staging directory" + memfile);

                                    string name = Path.GetFileNameWithoutExtension(memfile);
                                    string ext = Path.GetExtension(memfile);
                                    File.Move(memfile, stagingdirectory + "\\" + name + " - ME" + CURRENT_GAME_BUILD + ext);
                                }
                            }

                            //get free space for debug purposes
                            Utilities.GetDiskFreeSpaceEx(stagingdirectory, out freeBytes, out diskSize, out totalFreeBytes);
                            Log.Information("[SIZE] ADDONFULLEXTRACT Free Space on current drive: " + ByteSize.FromBytes(freeBytes) + " " + freeBytes);

                            List<string> files = new List<string>();
                            foreach (string file in Directory.EnumerateFiles(extractpath, "*.dds", SearchOption.AllDirectories))
                            {
                                files.Add(file);
                            }

                            foreach (string file in files)
                            {

                                string destination = extractpath + "\\" + Path.GetFileName(file);
                                if (!destination.ToLower().Equals(file.ToLower()))
                                {
                                    //Log.Information("Deleting existing file (if any): " + extractpath + "\\" + Path.GetFileName(file));
                                    if (File.Exists(destination))
                                    {
                                        Log.Information("Deleted existing file " + extractpath + "\\" + Path.GetFileName(file));
                                        File.Delete(destination);
                                    }
                                    Log.Information(file + " -> " + destination);
                                    File.Move(file, destination);
                                }
                                else
                                {
                                    Log.Information("File is already in correct place, no further processing necessary: " + extractpath + "\\" + Path.GetFileName(file));
                                }
                            }
                            BuildWorker.ReportProgress(0, new ThreadCommand(INCREMENT_COMPLETION_EXTRACTION));
                            break;
                        }
                    case ".tpf":
                        {
                            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Preparing " + af.FriendlyName));
                            string destination = EXE_DIRECTORY + "Extracted_Mods\\" + Path.GetFileName(fileToUse);
                            if (af.UserFile)
                            {
                                destination = extractpath + "\\" + Path.GetFileName(fileToUse);
                            }
                            File.Copy(extractSource, destination, true);
                            BuildWorker.ReportProgress(0, new ThreadCommand(INCREMENT_COMPLETION_EXTRACTION));
                            break;
                        }
                    case ".mod":
                        {
                            //Currently not used
                            //InstallWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Preparing " + af.FriendlyName));
                            //completed++;
                            //int progress = (int)((float)completed / (float)addonstoinstall.Count * 100);
                            //InstallWorker.ReportProgress(progress);
                            break;
                        }
                    case ".mem":
                        {
                            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Preparing " + af.FriendlyName));
                            //Copy to output folder
                            if (!isSingleFileMode)
                            {
                                if (af.UserFile)
                                {
                                    //we will move this later in user build step. STage everything here
                                    File.Copy(af.UserFilePath, extractpath + "\\" + fileToUse, true);
                                    af.ReadyStatusText = "Waiting for Addon to complete build";
                                    af.SetIdle();
                                    BuildWorker.ReportProgress(0, new ThreadCommand(INCREMENT_COMPLETION_EXTRACTION));
                                    return true;
                                }
                                else
                                {
                                    File.Copy(EXE_DIRECTORY + "Downloaded_Mods\\" + fileToUse, getOutputDir(CURRENT_GAME_BUILD) + fileToUse, true);
                                }
                            }
                            else
                            {
                                if (af.ALOTVersion > 0)
                                {
                                    //It's an ALOT file. We will move this directly to the output directory.
                                    Log.Information("ALOT MAIN FILE - Unpacked - moving to output: " + fileToUse);
                                    string movename = getOutputDir(CURRENT_GAME_BUILD) + "000_" + fileToUse;
                                    if (File.Exists(movename))
                                    {
                                        File.Delete(movename);
                                    }
                                    File.Move(EXE_DIRECTORY + "Downloaded_Mods\\" + fileToUse, getOutputDir(CURRENT_GAME_BUILD) + "000_" + fileToUse);
                                    foreach (PackageFile p in af.PackageFiles)
                                    {
                                        p.Processed = true; //No more processing on this addonfile. It has packagefiles since it could also be zipped still
                                    }
                                    break;
                                }
                            }
                            BuildWorker.ReportProgress(0, new ThreadCommand(INCREMENT_COMPLETION_EXTRACTION));
                            break;
                        }
                }
                BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, false));
            }
            catch (Exception e)
            {
                Log.Error("ERROR EXTRACTING AND PROCESSING FILE!");
                Log.Error(e.ToString());
                BuildWorker.ReportProgress(0, new ThreadCommand("ERROR_OCCURED", e.Message));
                TasksDisplayEngine.ReleaseTask(af.FriendlyName);
                af.ReadyStatusText = "Error occured during extraction";
                af.SetError();
                return false;
            }
            Utilities.GetDiskFreeSpaceEx(stagingdirectory, out freeBytes, out diskSize, out totalFreeBytes);

            Log.Information("[SIZE] ADDON EXTRACTADDON COMPLETED Free Space on current drive: " + ByteSize.FromBytes(freeBytes) + " " + freeBytes);
            string newText = TasksDisplayEngine.ReleaseTask(processingStr);
            if (newText != null)
            {
                BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, newText));
            }
            af.ReadyStatusText = "Queued for staging";
            af.SetIdle();
            return true;
        }

        private bool ExtractAddons(int game)
        {
            string stagingdirectory = EXE_DIRECTORY + ADDON_STAGING_DIR + "\\";
            PREBUILT_MEM_INDEX = 9;
            SHOULD_HAVE_OUTPUT_FILE = false; //will set to true later
            Log.Information("Extracting Addons and files for Mass Effect " + game);
            ulong freeBytes;
            ulong diskSize;
            ulong totalFreeBytes;
            bool gotFreeSpace = Utilities.GetDiskFreeSpaceEx(stagingdirectory, out freeBytes, out diskSize, out totalFreeBytes);
            Log.Information("[SIZE] PREBUILD Free Space on current drive: " + ByteSize.FromBytes(freeBytes) + " " + freeBytes);

            string basepath = EXE_DIRECTORY + @"Downloaded_Mods\";
            string destinationpath = EXE_DIRECTORY + @"Extracted_Mods\";
            Log.Information("Creating Extracted_Mods folder");
            Directory.CreateDirectory(destinationpath);

            ADDONFILES_TO_BUILD = new List<AddonFile>();
            ALOTVersionInfo CurrentGameALOTInfo = null;
            switch (game)
            {
                case 1:
                    CurrentGameALOTInfo = CURRENTLY_INSTALLED_ME1_ALOT_INFO;
                    break;
                case 2:
                    CurrentGameALOTInfo = CURRENTLY_INSTALLED_ME2_ALOT_INFO;
                    break;
                case 3:
                    CurrentGameALOTInfo = CURRENTLY_INSTALLED_ME3_ALOT_INFO;
                    break;
            }


            foreach (AddonFile af in alladdonfiles)
            {
                if (af.Ready && (game == 1 && af.Game_ME1 || game == 2 && af.Game_ME2 || game == 3 && af.Game_ME3))
                {

                    if (CurrentGameALOTInfo != null)
                    {
                        //Detected MEMI tag
                        //Check if ALOT main file is installed. If it is and this is ALOT file, skip
                        if (af.ALOTVersion > 0 && CurrentGameALOTInfo.ALOTVER >= 0 && BUILD_ALOT == false)
                        {
                            Log.Information("ALOT File in queue for processing but ALOT is already installed. Skipping...");
                            af.ReadyStatusText = "ALOT already installed";
                            continue; //skip
                        }

                        if (af.ALOTUpdateVersion > 0 && CurrentGameALOTInfo.ALOTVER == 0)
                        {
                            Log.Information("ALOT update file in queue but unknown if it applies due to unknown ALOT version. Skipping...");
                            af.ReadyStatusText = "Restore to unmodified to install this file";
                            af.SetError();
                            continue; //skip
                        }

                        //Check if ALOT update file of this version or higher is installed. If it is and this is ALOT update file, skip
                        if (af.ALOTUpdateVersion > 0 && CurrentGameALOTInfo.ALOTUPDATEVER >= af.ALOTUpdateVersion)
                        {
                            Log.Information("ALOT Update File in queue for processing, but ALOT update of this version or greater is already installed. Skipping...");
                            af.ReadyStatusText = "Update already applied";
                            continue;
                        }

                        //Check if ALOT update file applies to this version of ALOT main
                        if (af.ALOTUpdateVersion > 0 && CurrentGameALOTInfo.ALOTUPDATEVER != af.ALOTMainVersionRequired)
                        {
                            Log.Information("ALOT Update available but currently installed ALOT version does not match. Skipping...");
                            af.ReadyStatusText = "Update not applicable";
                            continue;
                        }
                    }

                    if (af.UserFile && BUILD_USER_FILES)
                    {
                        Log.Information("Adding User to build list: " + af.FriendlyName);
                        af.Building = true;
                        ADDONFILES_TO_BUILD.Add(af);
                    }

                    if (af.ALOTUpdateVersion == 0 && af.ALOTVersion == 0 && !af.UserFile && BUILD_ADDON_FILES)
                    {
                        Log.Information("Adding AddonFile to build list: " + af.FriendlyName);
                        af.Building = true;
                        ADDONFILES_TO_BUILD.Add(af);
                    }

                    if ((af.ALOTVersion > 0 && BUILD_ALOT) || af.ALOTUpdateVersion > 0)
                    {
                        ADDONFILES_TO_BUILD.Add(af);
                        af.Building = true;
                    }
                }
            }

            BuildWorker.ReportProgress(completed, new ThreadCommand(SET_VISIBILE_ITEMS_LIST, ADDONFILES_TO_BUILD));

            //DISK SPACE CHECK
            ulong fullsize = 0;
            foreach (AddonFile af in ADDONFILES_TO_BUILD)
            {
                string file = af.GetFile();
                ulong size = (ulong)((new FileInfo(file).Length) * 2.5);
                fullsize += size;
            }

            Utilities.GetDiskFreeSpaceEx(".", out freeBytes, out diskSize, out totalFreeBytes);
            Log.Information("We will need around " + ByteSize.FromBytes(fullsize) + " to build this set. The free space is " + ByteSize.FromBytes(freeBytes));
            if (freeBytes < fullsize)
            {
                //not enough disk space for build
                BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_HEADER_LABEL, "Not enough free space to build textures.\nYou will need around " + ByteSize.FromKiloBytes(fullsize) + " of free space on " + Path.GetPathRoot(Utilities.GetGamePath(CURRENT_GAME_BUILD) + " to build the installation packages.")));
                BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Build aborted"));
                BuildWorker.ReportProgress(completed, new ThreadCommand(SHOW_DIALOG, new KeyValuePair<string, string>("Not enough free space to build textures", "You will need around " + ByteSize.FromKiloBytes(fullsize) + " of free space on " + Path.GetPathRoot(Utilities.GetGamePath(CURRENT_GAME_BUILD)) + " to build the installation packages.")));

                BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, false));
                return false;
            }

            foreach (AddonFile af in ADDONFILES_TO_BUILD)
            {
                af.ReadyStatusText = "Queued for processing";
                af.SetWorking();
            }

            ADDONSTOBUILD_COUNT = ADDONFILES_TO_BUILD.Count;
            completed = 0;
            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_HEADER_LABEL, "Preparing files for installation.\nDon't close the window until this operation completes."));
            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Extracting Mods..."));
            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, true));

            BuildWorker.ReportProgress(0);

            bool modextractrequired = false; //not used currently.

            int threads = Environment.ProcessorCount;
            if (threads > 1)
            {
                threads--; //cores - 1
            }
            bool[] results = ADDONFILES_TO_BUILD.AsParallel().WithDegreeOfParallelism(threads).WithExecutionMode(ParallelExecutionMode.ForceParallelism).Select(ExtractAddon).ToArray();
            foreach (bool result in results)
            {
                if (!result)
                {
                    Log.Error("Failed to extract a file! Check previous entries in this log");
                    BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, false));
                    BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Failed to extract a file - check logs"));
                    CURRENT_GAME_BUILD = 0; //reset
                    return false;
                }
            }
            TasksDisplayEngine.ClearTasks();
            //if (tpfextractrequired)
            {
                BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, true));
                BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Extracting TPFs..."));
                BuildWorker.ReportProgress(0);

                Log.Information("Extracting TPF files.");
                string exe = BINARY_DIRECTORY + MEM_EXE_NAME;
                string args = "-extract-tpf \"" + EXE_DIRECTORY + "Extracted_Mods\" \"" + EXE_DIRECTORY + "Extracted_Mods\"";
                Utilities.runProcess(exe, args);
            }

            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Extracting MOD files..."));
            if (modextractrequired)
            {
                BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, true));
                BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Extracting MOD files..."));
                BuildWorker.ReportProgress(0);

                Log.Information("Extracting MOD files.");
                string exe = BINARY_DIRECTORY + MEM_EXE_NAME;
                string args = "-extract-mod " + game + " \"" + EXE_DIRECTORY + "Downloaded_Mods\" \"" + EXE_DIRECTORY + "Extracted_Mods\"";
                Utilities.runProcess(exe, args);
            }

            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Staging Addon texture files for building..."));
            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, false));

            //Calculate how many files to install...
            int totalfiles = 0;
            foreach (AddonFile af in ADDONFILES_TO_BUILD)
            {
                if (af.UserFile)
                {
                    continue;
                }
                totalfiles += af.PackageFiles.Count;
            }

            basepath = EXE_DIRECTORY + @"Extracted_Mods\";
            Directory.CreateDirectory(stagingdirectory);
            int numcompleted = 0;
            foreach (AddonFile af in ADDONFILES_TO_BUILD)
            {
                if (af.UserFile)
                {
                    continue;
                }
                af.SetWorking();
            }

            foreach (AddonFile af in ADDONFILES_TO_BUILD)
            {
                if (af.UserFile)
                {
                    continue;
                }
                if (af.PackageFiles.Count > 0)
                {
                    af.SetWorking();
                    af.ReadyStatusText = "Staging files for ALOT Addon";
                    foreach (PackageFile pf in af.PackageFiles)
                    {
                        if ((game == 1 && pf.ME1 || game == 2 && pf.ME2 || game == 3 && pf.ME3) && !pf.Processed)
                        {
                            string extractedpath = basepath + Path.GetFileNameWithoutExtension(af.Filename) + "\\" + pf.SourceName;
                            if (File.Exists(extractedpath) && pf.DestinationName != null)
                            {
                                //Log.Information("Copying Package File: " + pf.SourceName + "->" + pf.DestinationName);
                                string destination = stagingdirectory + pf.DestinationName;
                                File.Copy(extractedpath, destination, true);
                                SHOULD_HAVE_OUTPUT_FILE = true;
                            }
                            else if (pf.DestinationName == null)
                            {
                                Log.Error("File destination is null. This means there is a problem in the manifest or manifest parser. File: " + pf.SourceName);
                                errorOccured = true;
                            }
                            else
                            {
                                Log.Error("File specified by manifest doesn't exist after extraction: " + extractedpath);
                                errorOccured = true;
                            }

                            numcompleted++;
                            int progress = (int)((float)numcompleted / (float)totalfiles * 100);
                            BuildWorker.ReportProgress(progress);
                        }
                        //  Thread.Sleep(1000);
                    }
                }
                af.SetIdle();
                af.ReadyStatusText = "Waiting for other files to finish staging";
            }
            bool hasUserFiles = false;
            bool hasAddonFiles = false;
            foreach (AddonFile af in ADDONFILES_TO_BUILD)
            {
                if (af.ALOTUpdateVersion == 0 && af.ALOTVersion == 0 && !af.UserFile && !af.MEUITM)
                {
                    hasAddonFiles = true;
                }
                if (af.ALOTUpdateVersion > 0 || af.ALOTVersion > 0 || af.UserFile || af.MEUITM)
                {
                    if (af.UserFile)
                    {
                        hasUserFiles = true;
                    }
                    af.ReadyStatusText = "Waiting for Addon to complete build";
                    af.SetIdle();
                    continue;
                }
                af.ReadyStatusText = "Building into Addon MEM file";
                af.SetWorking();
            }


            Utilities.GetDiskFreeSpaceEx(stagingdirectory, out freeBytes, out diskSize, out totalFreeBytes);
            Log.Information("[SIZE] POSTEXTRACT_PRESTAGING Free Space on current drive: " + ByteSize.FromBytes(freeBytes) + " " + freeBytes);

            //COLEANUP EXTRACTION DIR
            Log.Information("Completed staging. Now cleaning up extraction directory");
            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Cleaning up extraction directory"));
            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, true));
            BuildWorker.ReportProgress(100);
            try
            {
                //Directory.Delete("Extracted_Mods", true);
                Log.Information("Deleted Extracted_Mods directory");
            }
            catch (IOException e)
            {
                Log.Error("Unable to delete extraction directory.");
            }

            Utilities.GetDiskFreeSpaceEx(stagingdirectory, out freeBytes, out diskSize, out totalFreeBytes);
            Log.Information("[SIZE] AFTER_EXTRACTION_CLEANUP Free Space on current drive: " + ByteSize.FromBytes(freeBytes) + " " + freeBytes);

            //BUILD MEM PACKAGE
            int mainBuildResult = hasAddonFiles ? -2 : 0; //if we have no files just set return code for addon to 0
            if (hasAddonFiles)
            {
                BuildWorker.ReportProgress(0);
                BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_HEADER_LABEL, "Building ALOT Addon for Mass Effect" + getGameNumberSuffix(CURRENT_GAME_BUILD) + ".\nDon't close the window until this operation completes."));

                BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Building Addon MEM Package..."));
                {
                    Log.Information("Building MEM Package.");
                    string exe = BINARY_DIRECTORY + MEM_EXE_NAME;
                    string filename = "002_ALOT_ME" + game + "_Addon.mem";
                    string args = "-convert-to-mem " + game + " \"" + EXE_DIRECTORY + ADDON_STAGING_DIR + "\" \"" + getOutputDir(game) + filename + "\" -ipc";

                    runMEM_BackupAndBuild(exe, args, BuildWorker);
                    while (BACKGROUND_MEM_PROCESS.State == AppState.Running)
                    {
                        Thread.Sleep(250);
                    }

                    foreach (AddonFile af in ADDONFILES_TO_BUILD)
                    {
                        if (af.ALOTVersion > 0)
                        {
                            af.ReadyStatusText = "ALOT ready to install";
                            af.SetIdle();
                        }
                        else if (af.ALOTUpdateVersion > 0)
                        {
                            af.ReadyStatusText = "ALOT update ready to install";
                            af.SetIdle();
                        }
                        if (!af.UserFile)
                        {
                            af.ReadyStatusText = "Built into ALOT Addon file";
                            af.SetIdle();
                        }
                        else
                        {
                            af.SetWorking();
                            af.ReadyStatusText = "Queued for user file build";
                        }
                    }

                    Utilities.GetDiskFreeSpaceEx(stagingdirectory, out freeBytes, out diskSize, out totalFreeBytes);
                    Log.Information("[SIZE] POST_MEM_BUILD Free Space on current drive: " + ByteSize.FromBytes(freeBytes) + " " + freeBytes);

                    mainBuildResult = BACKGROUND_MEM_PROCESS.ExitCode ?? 6000;
                    BACKGROUND_MEM_PROCESS = null;
                    BACKGROUND_MEM_PROCESS_ERRORS = null;
                    BACKGROUND_MEM_PROCESS_PARSED_ERRORS = null;
                    if (mainBuildResult != 0)
                    {
                        Log.Error("Non-Zero return code! Something probably went wrong.");
                    }
                    if (mainBuildResult == 0 && !File.Exists(getOutputDir(game) + filename) && SHOULD_HAVE_OUTPUT_FILE)
                    {

                        Log.Error("Process went OK but no outputfile... Something probably went wrong.");
                        mainBuildResult = -1;
                    }
                }
            }
            //cleanup staging
            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Cleaning up addon staging directory"));
            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, true));
            BuildWorker.ReportProgress(100);
            try
            {
                Utilities.DeleteFilesAndFoldersRecursively(ADDON_STAGING_DIR);
                Log.Information("Deleted " + ADDON_STAGING_DIR);
            }
            catch (IOException e)
            {
                Log.Error("Unable to delete staging directory. Addon should have been built however.\n" + e.ToString());
            }

            //build each user file
            bool hadUserErrors = false;
            int userBuildResult = 0;
            string userBuildErrors = "The following user supplied files did not compile into usable MEM packages:";
            if (hasUserFiles)
            {
                USERFILE_INDEX = 100;
                //BUILD USER FILES
                foreach (AddonFile af in ADDONFILES_TO_BUILD)
                {
                    if (!af.UserFile)
                    {
                        continue;
                    }
                    string userFileExtractedPath = USER_FULL_STAGING_DIRECTORY + System.IO.Path.GetFileNameWithoutExtension(af.UserFilePath);
                    if (!Directory.Exists(userFileExtractedPath))
                    {
                        continue;
                    }
                    BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, false));
                    BuildWorker.ReportProgress(0);

                    var mems = Directory.GetFiles(userFileExtractedPath, "*.mem", SearchOption.AllDirectories);
                    var installerinis = Directory.GetFiles(userFileExtractedPath, "*.mem", SearchOption.AllDirectories);

                    bool hasMems = mems.Count() > 0;
                    foreach (string mem in mems)
                    {
                        string memdest = getOutputDir(game) + USERFILE_INDEX.ToString("000") + "_UserAddon_" + Path.GetFileNameWithoutExtension(mem) + ".mem";
                        File.Move(mem, memdest);
                        USERFILE_INDEX++;
                    }

                    if (hasMems)
                    {
                        //check if any files remain
                        var remainingFiles = Directory.GetFiles(userFileExtractedPath, "*", SearchOption.AllDirectories);
                        if (remainingFiles.Count() == 0)
                        {
                            af.SetIdle();
                            af.ReadyStatusText = "User file is ready for install";
                            continue;
                        }
                    }


                    BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_HEADER_LABEL, "Building User Addons for Mass Effect" + getGameNumberSuffix(CURRENT_GAME_BUILD) + ".\nDon't close the window until this operation completes."));
                    af.ReadyStatusText = "Building user MEM file from mod files";
                    Log.Information("Building User MEM file from staging directory: " + userFileExtractedPath);
                    string exe = BINARY_DIRECTORY + MEM_EXE_NAME;
                    string filename = USERFILE_INDEX.ToString("000") + "_UserAddon_" + Path.GetFileNameWithoutExtension(af.UserFilePath) + ".mem";
                    string args = "-convert-to-mem " + game + " \"" + userFileExtractedPath + "\" \"" + getOutputDir(game) + filename + "\" -ipc";
                    CURRENT_USER_BUILD_FILE = af.FriendlyName;
                    runMEM_BackupAndBuild(exe, args, BuildWorker);
                    while (BACKGROUND_MEM_PROCESS.State == AppState.Running)
                    {
                        Thread.Sleep(250);
                    }

                    userBuildResult |= BACKGROUND_MEM_PROCESS.ExitCode ?? 6000;
                    if (hasMems)
                    {
                        userBuildResult = 0; //if it has mems we don't care
                    }
                    if (BACKGROUND_MEM_PROCESS_PARSED_ERRORS.Count() == 0 || hasMems)
                    {
                        af.SetIdle();
                        af.ReadyStatusText = "User file is ready for install";
                    }
                    else
                    {
                        if (!hasMems)
                        {
                            hadUserErrors = true;
                            af.SetError();
                            af.ReadyStatusText = "Error building MEM file";
                            foreach (string str in BACKGROUND_MEM_PROCESS_PARSED_ERRORS)
                            {
                                userBuildErrors += "\n - " + str;
                            }
                        }
                    }
                    if (BACKGROUND_MEM_PROCESS.ExitCode != 0 && !hasMems)
                    {
                        Log.Error("Non-Zero return code for user file! Something probably went wrong.");
                    }
                    if (BACKGROUND_MEM_PROCESS.ExitCode == 0 && !File.Exists(getOutputDir(game) + filename) && SHOULD_HAVE_OUTPUT_FILE && !hasMems)
                    {
                        Log.Error("Process went OK but no outputfile... Something probably went wrong with userfile.");
                    }

                    USERFILE_INDEX++;
                    BACKGROUND_MEM_PROCESS = null;
                    BACKGROUND_MEM_PROCESS_ERRORS = null;
                    CURRENT_USER_BUILD_FILE = null;
                }
            }
            //cleanup staging
            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Cleaning up user files staging directory"));
            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, true));
            BuildWorker.ReportProgress(100);
            try
            {
                Utilities.DeleteFilesAndFoldersRecursively(USER_FULL_STAGING_DIRECTORY);
                Log.Information("Deleted " + USER_FULL_STAGING_DIRECTORY);
            }
            catch (IOException e)
            {
                Log.Error("Unable to delete user staging directory. User addon's have (attempted) build however.\n" + e.ToString());
            }
            if (hadUserErrors)
            {
                BuildWorker.ReportProgress(completed, new ThreadCommand(SHOW_DIALOG, new KeyValuePair<string, string>("Some user files did not successfully build", userBuildErrors)));
            }

            Utilities.GetDiskFreeSpaceEx(stagingdirectory, out freeBytes, out diskSize, out totalFreeBytes);
            Log.Information("[SIZE] FINAL Free Space on current drive: " + ByteSize.FromBytes(freeBytes) + " " + freeBytes);
            BuildWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, false));
            //CURRENT_GAME_BUILD = 0; //reset
            bool addonResult = true;
            bool userResult = true;
            if (hasAddonFiles)
            {
                addonResult = mainBuildResult == 0;
            }
            if (hasUserFiles)
            {
                userResult = userBuildResult == 0;
            }
            return addonResult && userResult;
        }

        private void MusicIcon_Click(object sender, RoutedEventArgs e)
        {
            if (MusicIsPlaying)
            {
                MusicPaused = !MusicPaused;
                if (MusicPaused)
                {
                    waveOut.Pause();
                    MusicButtonIcon.Kind = MahApps.Metro.IconPacks.PackIconModernKind.SoundMute;
                }
                else
                {
                    MusicButtonIcon.Kind = MahApps.Metro.IconPacks.PackIconModernKind.Sound3;
                    waveOut.Play();
                }
            }
        }

        private async void BackupWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.UserState is null)
            {
                Build_ProgressBar.Value = e.ProgressPercentage;
                TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Normal, this);
                TaskbarManager.Instance.SetProgressValue(e.ProgressPercentage, 100);
            }
            else
            {
                ThreadCommand tc = (ThreadCommand)e.UserState;
                switch (tc.Command)
                {
                    case RESTORE_FAILED_COULD_NOT_DELETE_FOLDER:
                        await this.ShowMessageAsync("Restore failed", "Could not delete the existing game directory. This is usually due to something open or running from within the game folder. Close other programs and try again.");
                        return;
                    case UPDATE_OPERATION_LABEL:
                        AddonFilesLabel.Text = (string)tc.Data;
                        break;
                    case UPDATE_HEADER_LABEL:
                        HeaderLabel.Text = (string)tc.Data;
                        break;
                    case UPDATE_PROGRESSBAR_INDETERMINATE:
                        TaskbarManager.Instance.SetProgressState((bool)tc.Data ? TaskbarProgressBarState.Indeterminate : TaskbarProgressBarState.Normal);
                        Build_ProgressBar.IsIndeterminate = (bool)tc.Data;
                        break;
                    case ERROR_OCCURED:
                        Build_ProgressBar.IsIndeterminate = false;
                        Build_ProgressBar.Value = 0;
                        //await this.ShowMessageAsync("Error building Addon MEM Package", "An error occured building the addon. The logs will provide more information. The error message given is:\n" + (string)tc.Data);
                        break;
                    case SHOW_DIALOG:
                        KeyValuePair<string, string> messageStr = (KeyValuePair<string, string>)tc.Data;
                        await this.ShowMessageAsync(messageStr.Key, messageStr.Value);
                        break;
                    case SHOW_DIALOG_YES_NO:
                        ThreadCommandDialogOptions tcdo = (ThreadCommandDialogOptions)tc.Data;
                        MetroDialogSettings settings = new MetroDialogSettings();
                        settings.NegativeButtonText = tcdo.NegativeButtonText;
                        settings.AffirmativeButtonText = tcdo.AffirmativeButtonText;
                        MessageDialogResult result = await this.ShowMessageAsync(tcdo.title, tcdo.message, MessageDialogStyle.AffirmativeAndNegative, settings);
                        if (result == MessageDialogResult.Negative)
                        {
                            CONTINUE_BACKUP_EVEN_IF_VERIFY_FAILS = false;
                        }
                        else
                        {
                            CONTINUE_BACKUP_EVEN_IF_VERIFY_FAILS = true;
                        }
                        tcdo.signalHandler.Set();
                        break;
                    case INCREMENT_COMPLETION_EXTRACTION:
                        TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Normal);

                        Interlocked.Increment(ref completed);
                        Build_ProgressBar.Value = (completed / (double)ADDONSTOBUILD_COUNT) * 100;

                        break;
                }
            }
        }

        private void BackupGame(object sender, DoWorkEventArgs e)
        {

            //verify vanilla
            Log.Information("Verifying game: Mass Effect " + BACKUP_THREAD_GAME);
            string exe = BINARY_DIRECTORY + MEM_EXE_NAME;
            string args = "-check-game-data-only-vanilla " + BACKUP_THREAD_GAME + " -ipc";
            List<string> acceptedIPC = new List<string>();
            acceptedIPC.Add("OVERALL_PROGRESS");
            acceptedIPC.Add("ERROR");
            BackupWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Verifying game data..."));

            runMEM_BackupAndBuild(exe, args, BackupWorker, acceptedIPC);
            while (BACKGROUND_MEM_PROCESS.State == AppState.Running)
            {
                Thread.Sleep(250);
            }
            int buildresult = BACKGROUND_MEM_PROCESS.ExitCode ?? 1;
            if (buildresult != 0)
            {
                string modified = "";
                string gameDir = Utilities.GetGamePath(BACKUP_THREAD_GAME);
                foreach (String error in BACKGROUND_MEM_PROCESS_ERRORS)
                {
                    modified += "\n - " + error.Remove(0, gameDir.Length + 1);
                }
                ThreadCommandDialogOptions tcdo = new ThreadCommandDialogOptions();
                tcdo.signalHandler = new EventWaitHandle(false, EventResetMode.AutoReset);
                tcdo.title = "Game is modified";
                tcdo.message = "Mass Effect" + getGameNumberSuffix(BACKUP_THREAD_GAME) + " has files that do not match what is in the MEM database.\nYou can continue to back this installation up, but it may not be truly unmodified." + modified;
                tcdo.NegativeButtonText = "Abort";
                tcdo.AffirmativeButtonText = "Continue";
                BackupWorker.ReportProgress(completed, new ThreadCommand(SHOW_DIALOG_YES_NO, tcdo));
                tcdo.signalHandler.WaitOne();
                //Thread resumes
                if (!CONTINUE_BACKUP_EVEN_IF_VERIFY_FAILS)
                {
                    e.Result = null;
                    return;
                }
                else
                {
                    CONTINUE_BACKUP_EVEN_IF_VERIFY_FAILS = false; //reset
                }
            }
            string gamePath = Utilities.GetGamePath(BACKUP_THREAD_GAME);
            string backupPath = (string)e.Argument;
            string[] ignoredExtensions = { ".wav", ".pdf", ".bak" };
            if (gamePath != null)
            {
                CopyDir.CopyAll_ProgressBar(new DirectoryInfo(gamePath), new DirectoryInfo(backupPath), BackupWorker, -1, 0, ignoredExtensions);
            }
            if (BACKUP_THREAD_GAME == 3)
            {
                //Create Mod Manaager vanilla backup marker
                string file = backupPath + "\\cmm_vanilla";
                File.Create(file);
            }
            e.Result = backupPath;
        }

        private void InstallALOT(int game, List<AddonFile> filesToInstall)
        {
            ADDONFILES_TO_INSTALL = filesToInstall;
            WARN_USER_OF_EXIT = true;
            InstallingOverlay_TopLabel.Text = "Preparing installer";
            InstallWorker = new BackgroundWorker();
            InstallWorker.DoWork += InstallALOT;
            InstallWorker.WorkerReportsProgress = true;
            InstallWorker.ProgressChanged += InstallWorker_ProgressChanged;
            InstallWorker.RunWorkerCompleted += InstallCompleted;
            INSTALLING_THREAD_GAME = game;
            WindowButtonCommandsOverlayBehavior = WindowCommandsOverlayBehavior.Flyouts;
            InstallingOverlayFlyout.Theme = FlyoutTheme.Dark;

            //Set BG for this game
            string bgPath = "images/me" + game + "_bg.jpg";
            ImageBrush background = new ImageBrush(new BitmapImage(new Uri(BaseUriHelper.GetBaseUri(this), bgPath)));
            background.Stretch = Stretch.UniformToFill;
            InstallingOverlayFlyout.Background = background;
            Button_InstallDone.Visibility = System.Windows.Visibility.Hidden;
            Installing_Spinner.Visibility = System.Windows.Visibility.Visible;
            Installing_Checkmark.Visibility = System.Windows.Visibility.Hidden;
            PreventFileRefresh = true;
            HeaderLabel.Text = "Installing MEMs...";
            AddonFilesLabel.Text = "Running in installer mode.";
            InstallingOverlay_Tip.Visibility = System.Windows.Visibility.Visible;
            InstallingOverlay_StageLabel.Visibility = System.Windows.Visibility.Visible;
            InstallingOverlay_OverallLabel.Visibility = System.Windows.Visibility.Visible;
            InstallingOverlay_OverallLabel.Text = "";
            InstallingOverlay_StageLabel.Text = "Getting ready";
            InstallingOverlay_BottomLabel.Text = "Please wait";

            SolidColorBrush backgroundShadeBrush = null;
            switch (INSTALLING_THREAD_GAME)
            {
                case 1:
                    backgroundShadeBrush = new SolidColorBrush(Color.FromArgb(0x77, 0, 0, 0));
                    Panel_ME1LODLimit.Visibility = System.Windows.Visibility.Visible;
                    LODLIMIT = 0;
                    break;
                case 2:
                    backgroundShadeBrush = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0));
                    Panel_ME1LODLimit.Visibility = System.Windows.Visibility.Collapsed;
                    break;
                case 3:
                    backgroundShadeBrush = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0));
                    Panel_ME1LODLimit.Visibility = System.Windows.Visibility.Collapsed;
                    break;
            }
            InstallingOverlayFlyout_Border.Background = backgroundShadeBrush;
            TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Normal, this);

            bool playMusic = false;
            foreach (AddonFile af in ADDONFILES_TO_INSTALL)
            {
                if (af.ALOTVersion > 0 || af.MEUITM)
                {
                    playMusic = true;
                    break;
                }
            }

            //Set music
            if (playMusic)
            {
                string musfile = GetMusicDirectory() + "me" + game + ".ogg";
                if (File.Exists(musfile))
                {
                    MusicIsPlaying = true;
                    MusicPaused = false;
                    waveOut = new WaveOut();
                    vorbisStream = new NAudio.Vorbis.VorbisWaveReader(musfile);
                    LoopStream ls = new LoopStream(vorbisStream);
                    fadeoutProvider = new FadeInOutSampleProvider(ls.ToSampleProvider());
                    waveOut.Init(fadeoutProvider);
                    fadeoutProvider.BeginFadeIn(2000);
                    InstallingOverlay_MusicButton.Visibility = Visibility.Visible;
                    MusicButtonIcon.Kind = MahApps.Metro.IconPacks.PackIconModernKind.Sound3;
                    waveOut.Play();
                }
            }
            SetInstallFlyoutState(true);

            //Load Tips
            TIPS_LIST = new List<string>();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AlotAddOnGUI.ui.installtips.xml"))
            using (StreamReader reader = new StreamReader(stream))
            {
                string result = reader.ReadToEnd();
                try
                {
                    XElement rootElement = XElement.Parse(result);
                    IEnumerable<XElement> xNames;

                    xNames = rootElement.Element("me" + INSTALLING_THREAD_GAME).Descendants("tip");

                    foreach (XElement element in xNames)
                    {
                        TIPS_LIST.Add(element.Value);
                    }
                }
                catch
                {
                    //no tips.
                }
            }
            InstallingOverlay_OverallLabel.Visibility = System.Windows.Visibility.Visible;
            InstallingOverlay_Tip.Text = "";
            tipticker = new System.Windows.Threading.DispatcherTimer();
            tipticker.Tick += newTipTimer_Tick;
            tipticker.Interval = new TimeSpan(0, 0, 20);
            tipticker.Start();
            newTipTimer_Tick(null, null);
            InstallWorker.RunWorkerAsync(getOutputDir(INSTALLING_THREAD_GAME));
        }

        private void MusicPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (MusicIsPlaying)
            {
                vorbisStream.Position = 0;
            } else
            {
                waveOut.Stop();
                waveOut.Dispose();
                waveOut = null;
            }
        }

        private void newTipTimer_Tick(object sender, EventArgs e)
        {
            // code goes here
            if (TIPS_LIST.Count > 1)
            {
                string currentTip = InstallingOverlay_Tip.Text;
                string newTip = InstallingOverlay_Tip.Text;

                while (currentTip == newTip)
                {
                    int r = RANDOM.Next(TIPS_LIST.Count);
                    newTip = TIPS_LIST[r];
                }
                InstallingOverlay_Tip.Text = newTip;
            }
        }

        private string GetMusicDirectory()
        {
            return EXE_DIRECTORY + "music\\";
        }

        private void InstallCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            InstallingOverlay_MusicButton.Visibility = Visibility.Collapsed;
            if (MusicIsPlaying)
            {
                MusicIsPlaying = false;
                if (MusicPaused)
                {
                    waveOut.Stop();
                    waveOut.Dispose();
                }
                else
                {
                    fadeoutProvider.BeginFadeOut(3000);
                }
            }
            
            WARN_USER_OF_EXIT = false;
            Log.Information("InstallCompleted()");
            InstallingOverlay_OverallLabel.Visibility = System.Windows.Visibility.Collapsed;

            tipticker.Stop();
            InstallingOverlay_StageLabel.Visibility = System.Windows.Visibility.Collapsed;
            Button_InstallViewLogs.Visibility = System.Windows.Visibility.Collapsed;
            switch (INSTALLING_THREAD_GAME)
            {
                case 1:
                    CURRENTLY_INSTALLED_ME1_ALOT_INFO = Utilities.GetInstalledALOTInfo(1);
                    break;
                case 2:
                    CURRENTLY_INSTALLED_ME1_ALOT_INFO = Utilities.GetInstalledALOTInfo(2);
                    break;
                case 3:
                    CURRENTLY_INSTALLED_ME1_ALOT_INFO = Utilities.GetInstalledALOTInfo(3);
                    break;
            }
            CURRENTLY_INSTALLED_ME1_ALOT_INFO = Utilities.GetInstalledALOTInfo(1);

            if (e.Result != null)
            {
                int result = (int)e.Result;
                string gameName = "Mass Effect" + getGameNumberSuffix(INSTALLING_THREAD_GAME);
                switch (result)
                {
                    case INSTALL_OK:
                        {
                            var uriSource = new Uri(@"images/greencheck_large.png", UriKind.Relative);
                            Installing_Checkmark.Source = new BitmapImage(uriSource);
                            Log.Information("Installation result: OK");
                            HeaderLabel.Text = "Installation has completed.";
                            AddonFilesLabel.Text = "Thanks for using ALOT Installer.";
                            InstallingOverlay_Tip.Text = "Do not install any new DLC or mods that add/replace pcc or upk files from here on out - doing so will break your game!";
                            break;
                        }
                    case RESULT_ME1LAA_FAILED:
                        {
                            InstallingOverlay_TopLabel.Text = "Failed to set Mass Effect to LAA";
                            InstallingOverlay_BottomLabel.Text = "Check the logs for details";
                            HeaderLabel.Text = "ME1 LAA fix failed. ME1 may be unstable.";
                            break;
                        }
                    case RESULT_SCAN_REMOVE_FAILED:
                        {
                            InstallingOverlay_TopLabel.Text = "Failed to remove empty mipmaps";
                            InstallingOverlay_BottomLabel.Text = "Check the logs for details";
                            HeaderLabel.Text = "Error occured removing mipmaps from " + gameName;
                            break;
                        }
                    case RESULT_TEXTUREINSTALL_FAILED:
                        {
                            InstallingOverlay_TopLabel.Text = "Failed to install textures";
                            InstallingOverlay_BottomLabel.Text = "Check the logs for details";
                            HeaderLabel.Text = "Error occured removing mipmaps from " + gameName;
                            break;
                        }
                    case RESULT_UNPACK_FAILED:
                        {
                            InstallingOverlay_TopLabel.Text = "Failed to unpack DLCs";
                            InstallingOverlay_BottomLabel.Text = "Check the logs for details";
                            HeaderLabel.Text = "Error occured removing mipmaps from " + gameName;
                            break;
                        }
                }
                if (result != INSTALL_OK)
                {
                    Button_InstallViewLogs.Visibility = System.Windows.Visibility.Visible;
                    Log.Error("Installation result: Error occured");
                    var uriSource = new Uri(@"images/redx_large.png", UriKind.Relative);
                    Installing_Checkmark.Source = new BitmapImage(uriSource);
                    AddonFilesLabel.Text = "Check the logs for more detailed information.";
                }
            }
            else
            {
                //Null or not OK
                Button_InstallViewLogs.Visibility = System.Windows.Visibility.Visible;
                InstallingOverlay_TopLabel.Text = "Unknown installation error has occured";
                InstallingOverlay_BottomLabel.Text = "Check the logs for details";

                var uriSource = new Uri(@"images/redx_large.png", UriKind.Relative);
                Installing_Checkmark.Source = new BitmapImage(uriSource);
                Log.Error("Installation result: Error occured");
                HeaderLabel.Text = "Installation failed! Check the logs for more detailed information";
                AddonFilesLabel.Text = "Check the logs for more detailed information.";
            }
            INSTALLING_THREAD_GAME = 0;
            ADDONFILES_TO_INSTALL = null;
            INSTALL_STAGE = 0;
            PreventFileRefresh = false;
            UpdateALOTStatus();
            Installing_Spinner.Visibility = System.Windows.Visibility.Collapsed;
            Installing_Checkmark.Visibility = System.Windows.Visibility.Visible;
            Button_InstallDone.Visibility = System.Windows.Visibility.Visible;
            TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.NoProgress, this);
            TaskbarManager.Instance.SetProgressValue(0, 0);
            var helper = new FlashWindowHelper(System.Windows.Application.Current);
            helper.FlashApplicationWindow();
        }

        private void InstallALOT(object sender, DoWorkEventArgs e)
        {
            Log.Information("InstallWorker Thread starting for ME" + INSTALLING_THREAD_GAME);
            ProgressWeightPercentages.ClearTasks();
            ALOTVersionInfo versionInfo = Utilities.GetInstalledALOTInfo(INSTALLING_THREAD_GAME);
            bool RemoveMipMaps = (versionInfo == null); //remove mipmaps only if alot is not installed
            if (INSTALLING_THREAD_GAME == 1)
            {
                STAGE_COUNT = 4;
            }
            else if (INSTALLING_THREAD_GAME == 2)
            {
                STAGE_COUNT = 4;
            }
            else
            {
                //me3
                ProgressWeightPercentages.AddTask(ProgressWeightPercentages.JOB_UNPACK);
                STAGE_COUNT = 5;
            }

            if (!RemoveMipMaps)
            {
                STAGE_COUNT -= 2; //Scanning, Removing
            }
            else
            {
                ProgressWeightPercentages.AddTask(ProgressWeightPercentages.JOB_SCAN);
                ProgressWeightPercentages.AddTask(ProgressWeightPercentages.JOB_REMOVE);
            }
            ProgressWeightPercentages.AddTask(ProgressWeightPercentages.JOB_INSTALL);
            ProgressWeightPercentages.AddTask(ProgressWeightPercentages.JOB_SAVE);
            ProgressWeightPercentages.ScaleWeights();
            INSTALL_STAGE = 0;
            //Checking files for title

            AddonFile alotAddonFile = null;
            bool installedALOT = false;
            byte justInstalledUpdate = 0;
            bool installingMEUITM = false;
            //Check if ALOT is in files that will be installed
            foreach (AddonFile af in ADDONFILES_TO_INSTALL)
            {
                if (af.MEUITM)
                {
                    Log.Information("InstallWorker: We are installing MEUITM in this pass.");
                    installingMEUITM = true;
                }
                if (af.ALOTVersion > 0)
                {
                    alotAddonFile = af;
                    Log.Information("InstallWorker: We are installing ALOT v" + af.ALOTVersion + " in this pass.");
                    installedALOT = true;
                }
                if (af.ALOTUpdateVersion > 0)
                {
                    Log.Information("InstallWorker: We are installing ALOT Update v" + af.ALOTUpdateVersion + " in this pass.");
                    justInstalledUpdate = af.ALOTUpdateVersion;
                }
            }
            string primary = "ALOT";
            if (installingMEUITM)
            {
                primary += " & MEUITM";
            }
            MAINTASK_TEXT = "Installing " + primary + " for Mass Effect" + getGameNumberSuffix(INSTALLING_THREAD_GAME);
            if (!installedALOT)
            {
                if (justInstalledUpdate > 0)
                {
                    MAINTASK_TEXT = "Installing ALOT Update for Mass Effect" + getGameNumberSuffix(INSTALLING_THREAD_GAME);
                }
                else
                {
                    MAINTASK_TEXT = "Installing texture mods for Mass Effect" + getGameNumberSuffix(INSTALLING_THREAD_GAME);
                }
            }


            InstallWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OVERALL_TASK, MAINTASK_TEXT));


            List<string> acceptedIPC = new List<string>();
            acceptedIPC.Add("TASK_PROGRESS");
            acceptedIPC.Add("PHASE");
            acceptedIPC.Add("ERROR");
            acceptedIPC.Add("PROCESSING_MOD");
            acceptedIPC.Add("PROCESSING_FILE");

            string exe = BINARY_DIRECTORY + MEM_EXE_NAME;
            string args = "";
            int processResult = 0;
            int overallProgress = 0;
            stopwatch = Stopwatch.StartNew();
            if (INSTALLING_THREAD_GAME == 3)
            {
                //Unpack DLC
                Log.Information("InstallWorker(): ME3 -> Unpacking DLC.");
                CurrentTask = "Unpacking DLC";
                CurrentTaskPercent = 0;
                InstallWorker.ReportProgress(0, new ThreadCommand(UPDATE_OPERATION_LABEL, CurrentTask));
                InstallWorker.ReportProgress(0, new ThreadCommand(UPDATE_TASK_PROGRESS, CurrentTaskPercent));
                Interlocked.Increment(ref INSTALL_STAGE); //unpack-dlcs does not output phase 
                InstallWorker.ReportProgress(0, new ThreadCommand(UPDATE_STAGE_LABEL));
                args = "-unpack-dlcs -ipc";
                runMEM_Install(exe, args, InstallWorker);
                while (BACKGROUND_MEM_PROCESS.State == AppState.Running)
                {
                    Thread.Sleep(250);
                }

                processResult = BACKGROUND_MEM_PROCESS.ExitCode ?? 1;
                if (processResult != 0)
                {
                    Log.Error("UNPACK RETURN CODE WAS NOT 0: " + processResult);
                    e.Result = RESULT_UNPACK_FAILED;
                    return;
                }
                Log.Warning("[TASK TIMING] End of stage " + INSTALL_STAGE + " " + stopwatch.ElapsedMilliseconds);
                overallProgress = ProgressWeightPercentages.SubmitProgress(INSTALL_STAGE, 100);
                InstallWorker.ReportProgress(0, new ThreadCommand(SET_OVERALL_PROGRESS, overallProgress));
                //Interlocked.Increment(ref INSTALL_STAGE);
                //InstallWorker.ReportProgress(0, new ThreadCommand(UPDATE_STAGE_LABEL));
            }

            //Scan and remove empty MipMaps
            if (RemoveMipMaps)
            {
                Log.Information("InstallWorker(): Removing Empty MipMaps");

                args = "-scan-with-remove " + INSTALLING_THREAD_GAME + " -ipc";
                runMEM_Install(exe, args, InstallWorker); //output's 2 phase's
                while (BACKGROUND_MEM_PROCESS.State == AppState.Running)
                {
                    Thread.Sleep(250);
                }
                processResult = BACKGROUND_MEM_PROCESS.ExitCode ?? 6000;
                if (processResult != 0)
                {
                    Log.Error("SCAN/REMOVE RETURN CODE WAS NOT 0: " + processResult);
                    e.Result = RESULT_SCAN_REMOVE_FAILED;
                    return;
                }
                Log.Warning("[TASK TIMING] End of stage " + INSTALL_STAGE + " " + stopwatch.ElapsedMilliseconds);
                overallProgress = ProgressWeightPercentages.SubmitProgress(INSTALL_STAGE, 100);
                InstallWorker.ReportProgress(0, new ThreadCommand(SET_OVERALL_PROGRESS, overallProgress));
                //scan with remove or install textures will increment this

            }

            //Install Textures
            Interlocked.Increment(ref INSTALL_STAGE);
            InstallWorker.ReportProgress(0, new ThreadCommand(UPDATE_STAGE_LABEL));
            string outputDir = getOutputDir(INSTALLING_THREAD_GAME, false);
            CurrentTask = "Installing textures";
            CurrentTaskPercent = 0;
            InstallWorker.ReportProgress(0, new ThreadCommand(UPDATE_OPERATION_LABEL, CurrentTask));
            InstallWorker.ReportProgress(0, new ThreadCommand(UPDATE_TASK_PROGRESS, CurrentTaskPercent));
            args = "-install-mods " + INSTALLING_THREAD_GAME + " \"" + outputDir + "\" -ipc";
            runMEM_Install(exe, args, InstallWorker);
            while (BACKGROUND_MEM_PROCESS.State == AppState.Running)
            {
                Thread.Sleep(250);
            }
            processResult = BACKGROUND_MEM_PROCESS.ExitCode ?? 1;
            if (processResult != 0)
            {
                Log.Error("TEXTURE INSTALLATION RETURN CODE WAS NOT 0: " + processResult);
                e.Result = RESULT_TEXTUREINSTALL_FAILED;
                return;
            }
            Log.Warning("[TASK TIMING] End of stage " + INSTALL_STAGE + " " + stopwatch.ElapsedMilliseconds);
            ProgressWeightPercentages.SubmitProgress(INSTALL_STAGE, 100);
            InstallWorker.ReportProgress(0, new ThreadCommand(SET_OVERALL_PROGRESS, overallProgress));

            InstallWorker.ReportProgress(0, new ThreadCommand(UPDATE_OVERALL_TASK, "Finishing installation"));
            InstallWorker.ReportProgress(0, new ThreadCommand(HIDE_STAGE_LABEL));


            //Apply LOD
            CurrentTask = "Updating Mass Effect" + getGameNumberSuffix(INSTALLING_THREAD_GAME) + "'s graphics settings";
            InstallWorker.ReportProgress(0, new ThreadCommand(UPDATE_SUBTASK, CurrentTask));

            InstallWorker.ReportProgress(0, new ThreadCommand(HIDE_LOD_LIMIT, CurrentTask));

            args = "-apply-lods-gfx " + INSTALLING_THREAD_GAME;
            if (LODLIMIT == 2)
            {
                args += " -limit2k";
            }
            runMEM_Install(exe, args, InstallWorker);
            while (BACKGROUND_MEM_PROCESS.State == AppState.Running)
            {
                Thread.Sleep(250);
            }
            processResult = BACKGROUND_MEM_PROCESS.ExitCode ?? 6000;
            if (processResult != 0)
            {
                Log.Error("APPLYLOD RETURN CODE WAS NOT 0: " + processResult);
            }

            if (INSTALLING_THREAD_GAME == 1)
            {
                //Apply ME1 LAA
                CurrentTask = "Making Mass Effect Large Address Aware";
                InstallWorker.ReportProgress(0, new ThreadCommand(UPDATE_SUBTASK, CurrentTask));

                args = "-apply-me1-laa";
                runMEM_Install(exe, args, InstallWorker);
                while (BACKGROUND_MEM_PROCESS.State == AppState.Running)
                {
                    Thread.Sleep(250);
                }
                processResult = BACKGROUND_MEM_PROCESS.ExitCode ?? 1;
                if (processResult != 0)
                {
                    Log.Error("Error setting ME1 to large address aware: " + processResult);
                    e.Result = RESULT_ME1LAA_FAILED;
                    return;
                }
            }
            Utilities.TurnOffOriginAutoUpdateForGame(INSTALLING_THREAD_GAME);

            //Create/Update Marker File
            short ALOTVersion = 0;
            int meuitmFlag = (installingMEUITM) ? 1 : 0; //if 0 we can or it with existing info
            if (versionInfo == null)
            {
                //Check if ALOT is in files that were installed
                foreach (AddonFile af in ADDONFILES_TO_INSTALL)
                {
                    if (af.ALOTVersion > 0)
                    {
                        ALOTVersion = af.ALOTVersion;
                        break;
                    }
                }
            }
            else
            {
                ALOTVersion = versionInfo.ALOTVER;
                meuitmFlag |= versionInfo.MEUITMVER;
            }

            //Update Marker
            byte updateVersion = 0;
            if (justInstalledUpdate > 0)
            {
                updateVersion = justInstalledUpdate;
            }
            else
            {
                updateVersion = versionInfo.ALOTUPDATEVER;
            }



            //Write Marker
            ALOTVersionInfo newVersion = new ALOTVersionInfo(ALOTVersion, updateVersion, 0, meuitmFlag);
            Utilities.CreateMarkerFile(INSTALLING_THREAD_GAME, newVersion);
            ALOTVersionInfo test = Utilities.GetInstalledALOTInfo(INSTALLING_THREAD_GAME);
            if (test == null || test.ALOTVER != newVersion.ALOTVER || test.ALOTUPDATEVER != newVersion.ALOTUPDATEVER)
            {
                //Marker file written was bad
                Log.Error("Marker file was not properly written!");
                if (test == null)
                {
                    Log.Error("Marker file does not indicate anything was installed.");
                }
                else
                {
                    if (test.ALOTVER != newVersion.ALOTVER)
                    {
                        Log.Error("Marker file does not show that ALOT was installed, but we detect some version was installed.");
                    }
                    if (test.ALOTUPDATEVER != newVersion.ALOTUPDATEVER)
                    {
                        Log.Error("Marker file does not show that ALOT Update was applied or installed to our current version");
                    }
                }
            }
            //Install Binkw32
            if (INSTALLING_THREAD_GAME == 2 || INSTALLING_THREAD_GAME == 3)
            {
                Utilities.InstallBinkw32Bypass(INSTALLING_THREAD_GAME);
            }

            //If MAIN alot file is here, move it back to downloaded_mods
            if (installedALOT && alotAddonFile.UnpackedSingleFilename != null)
            {
                //ALOT was just installed. We are going to move it back to mods folder
                string extractedName = alotAddonFile.UnpackedSingleFilename;

                Log.Information("ALOT MAIN FILE - Unpacked - moving to downloaded_mods from install dir: " + extractedName);
                string source = getOutputDir(INSTALLING_THREAD_GAME) + "000_" + extractedName;
                string dest = EXE_DIRECTORY + "Downloaded_Mods\\" + extractedName;

                if (File.Exists(dest))
                {
                    File.Delete(dest);
                }
                File.Move(source, dest);
                Log.Information("Moved main alot file back to downloaded_mods");

                //Delete original
                dest = EXE_DIRECTORY + "Downloaded_Mods\\" + alotAddonFile.Filename;
                if (File.Exists(dest))
                {
                    Log.Information("Deleted original alot archive file from downloaded_mods");
                    File.Delete(dest);
                }
            }

            InstallWorker.ReportProgress(0, new ThreadCommand(HIDE_STAGE_LABEL));

            string taskString = "Installation of ALOT for Mass Effect" + getGameNumberSuffix(INSTALLING_THREAD_GAME);
            if (!installedALOT)
            {
                //use different end string

                if (justInstalledUpdate > 0)
                {
                    //installed update
                    taskString = "Installation of ALOT Update for Mass Effect" + getGameNumberSuffix(INSTALLING_THREAD_GAME);
                }
                else
                {
                    //addon or other files
                    taskString = "Installation of texture mods for Mass Effect" + getGameNumberSuffix(INSTALLING_THREAD_GAME);
                }
            }
            InstallWorker.ReportProgress(0, new ThreadCommand(UPDATE_OVERALL_TASK, taskString));
            InstallWorker.ReportProgress(0, new ThreadCommand(UPDATE_SUBTASK, "has completed"));

            e.Result = INSTALL_OK;
        }

        private async void BuildWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.UserState is null)
            {
                Build_ProgressBar.Value = e.ProgressPercentage;
                TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Normal);
                TaskbarManager.Instance.SetProgressValue(e.ProgressPercentage, 100);
            }
            else
            {
                ThreadCommand tc = (ThreadCommand)e.UserState;
                switch (tc.Command)
                {
                    case SET_VISIBILE_ITEMS_LIST:
                        List<AddonFile> afs = (List<AddonFile>)tc.Data;
                        ShowBuildingOnly = true;
                        ApplyFiltering();
                        break;
                    case UPDATE_OPERATION_LABEL:
                        AddonFilesLabel.Text = (string)tc.Data;
                        break;
                    case UPDATE_HEADER_LABEL:
                        HeaderLabel.Text = (string)tc.Data;
                        break;
                    case UPDATE_PROGRESSBAR_INDETERMINATE:
                        TaskbarManager.Instance.SetProgressState((bool)tc.Data ? TaskbarProgressBarState.Indeterminate : TaskbarProgressBarState.Normal);
                        Build_ProgressBar.IsIndeterminate = (bool)tc.Data;
                        break;
                    case ERROR_OCCURED:

                        Build_ProgressBar.IsIndeterminate = false;
                        Build_ProgressBar.Value = 0;
                        if (!ERROR_SHOWING)
                        {
                            ERROR_SHOWING = true;
                            await this.ShowMessageAsync("Error building Addon MEM Package", "An error occured building the addon. The logs will provide more information. The error message given is:\n" + (string)tc.Data);
                            ERROR_SHOWING = false;
                        }
                        break;
                    case SHOW_DIALOG:
                        KeyValuePair<string, string> messageStr = (KeyValuePair<string, string>)tc.Data;
                        await this.ShowMessageAsync(messageStr.Key, messageStr.Value);
                        break;
                    case INCREMENT_COMPLETION_EXTRACTION:
                        Interlocked.Increment(ref completed);
                        Build_ProgressBar.Value = (completed / (double)ADDONSTOBUILD_COUNT) * 100;
                        break;
                }
            }
        }

        private async void InstallWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            ThreadCommand tc = (ThreadCommand)e.UserState;
            switch (tc.Command)
            {
                case UPDATE_STAGE_LABEL:
                    InstallingOverlay_StageLabel.Text = "Stage " + INSTALL_STAGE + " of " + STAGE_COUNT;
                    break;
                case HIDE_LOD_LIMIT:
                    Panel_ME1LODLimit.Visibility = System.Windows.Visibility.Collapsed;
                    break;
                case HIDE_STAGE_LABEL:
                    InstallingOverlay_StageLabel.Visibility = System.Windows.Visibility.Collapsed;
                    InstallingOverlay_OverallLabel.Visibility = System.Windows.Visibility.Collapsed;
                    break;
                case UPDATE_OVERALL_TASK:
                    InstallingOverlay_TopLabel.Text = (string)tc.Data;
                    break;
                case UPDATE_SUBTASK:
                    InstallingOverlay_BottomLabel.Text = (string)tc.Data;
                    break;
                case UPDATE_OPERATION_LABEL:
                    CurrentTask = (string)tc.Data;
                    InstallingOverlay_BottomLabel.Text = CurrentTask + " " + CurrentTaskPercent + "%";
                    break;
                case UPDATE_TASK_PROGRESS:
                    int oldTaskProgress = CurrentTaskPercent;
                    if (tc.Data is string)
                    {
                        CurrentTaskPercent = Convert.ToInt32((string)tc.Data);
                    }
                    else
                    {
                        CurrentTaskPercent = (int)tc.Data;
                    }

                    if (CurrentTaskPercent != oldTaskProgress)
                    {
                        int progressval = ProgressWeightPercentages.SubmitProgress(INSTALL_STAGE, CurrentTaskPercent);
                        InstallingOverlay_BottomLabel.Text = CurrentTask + " " + CurrentTaskPercent + "%";
                        InstallingOverlay_OverallLabel.Text = "(" + progressval.ToString() + "%)";
                        TaskbarManager.Instance.SetProgressValue(progressval, 100);
                    }
                    break;
                case SET_OVERALL_PROGRESS:
                    //InstallingOverlay_BottomLabel.Text = CurrentTask + " " + CurrentTaskPercent + "%";
                    //TaskbarManager.Instance.SetProgressValue((int)tc.Data, 100);
                    int progress = ProgressWeightPercentages.GetOverallProgress();
                    InstallingOverlay_OverallLabel.Text = "(" + progress.ToString() + "%)";
                    TaskbarManager.Instance.SetProgressValue(progress, 100);
                    break;
                case UPDATE_PROGRESSBAR_INDETERMINATE:
                    //Install_ProgressBar.IsIndeterminate = (bool)tc.Data;
                    break;
                case ERROR_OCCURED:
                    Build_ProgressBar.IsIndeterminate = false;
                    Build_ProgressBar.Value = 0;
                    //await this.ShowMessageAsync("Error building Addon MEM Package", "An error occured building the addon. The logs will provide more information. The error message given is:\n" + (string)tc.Data);
                    break;
                case SHOW_DIALOG:
                    KeyValuePair<string, string> messageStr = (KeyValuePair<string, string>)tc.Data;
                    await this.ShowMessageAsync(messageStr.Key, messageStr.Value);
                    break;
                case SHOW_DIALOG_YES_NO:
                    //ThreadCommandDialogOptions tcdo = (ThreadCommandDialogOptions)tc.Data;
                    //MetroDialogSettings settings = new MetroDialogSettings();
                    //settings.NegativeButtonText = tcdo.NegativeButtonText;
                    //settings.AffirmativeButtonText = tcdo.AffirmativeButtonText;
                    //MessageDialogResult result = await this.ShowMessageAsync(tcdo.title, tcdo.message, MessageDialogStyle.AffirmativeAndNegative, settings);
                    //if (result == MessageDialogResult.Negative)
                    //{
                    //    CONTINUE_BACKUP_EVEN_IF_VERIFY_FAILS = false;
                    //}
                    //else
                    //{
                    //    CONTINUE_BACKUP_EVEN_IF_VERIFY_FAILS = true;
                    //}
                    //tcdo.signalHandler.Set();
                    break;
                case INCREMENT_COMPLETION_EXTRACTION:
                    //Interlocked.Increment(ref completed);
                    //Install_ProgressBar.Value = (completed / (double)ADDONSTOINSTALL_COUNT) * 100;
                    break;
            }
        }

        private void runMEM_BackupAndBuild(string exe, string args, BackgroundWorker worker, List<string> acceptedIPC = null)
        {
            Debug.WriteLine("Running process: " + exe + " " + args);
            Log.Information("Running process: " + exe + " " + args);
            BACKGROUND_MEM_PROCESS = new ConsoleApp(exe, args);
            BACKGROUND_MEM_PROCESS_ERRORS = new List<string>();
            BACKGROUND_MEM_PROCESS_PARSED_ERRORS = new List<string>();
            BACKGROUND_MEM_PROCESS.ConsoleOutput += (o, args2) =>
            {
                string str = args2.Line;
                if (str.StartsWith("[IPC]"))
                {
                    string command = str.Substring(5);
                    int endOfCommand = command.IndexOf(' ');
                    if (endOfCommand >= 0)
                    {
                        command = command.Substring(0, endOfCommand);
                    }
                    if (acceptedIPC == null || acceptedIPC.Contains(command))
                    {
                        string param = str.Substring(endOfCommand + 5).Trim();
                        switch (command)
                        {
                            case "OVERALL_PROGRESS":
                                worker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, false));
                                int percentInt = Convert.ToInt32(param);
                                worker.ReportProgress(percentInt);
                                break;
                            case "PROCESSING_FILE":
                                worker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, param));
                                break;
                            case "ERROR":
                                Log.Information("[ERROR] Realtime Process Output: " + param);
                                BACKGROUND_MEM_PROCESS_ERRORS.Add(param);
                                break;
                            case "ERROR_NO_BUILDABLE_FILES":
                                BACKGROUND_MEM_PROCESS_PARSED_ERRORS.Add(CURRENT_USER_BUILD_FILE + " has no files that can be used for building");
                                Log.Error("User buildable file has no files that can be converted to MEM format.");
                                break;
                            case "ERROR_FILE_NOT_COMPATIBLE":
                                Log.Error("MEM reporting file is not compatible: " + param);
                                break;
                            default:
                                Log.Information("Unknown IPC command: " + command);
                                break;
                        }
                    }
                }
                else
                {
                    Log.Information("Realtime Process Output: " + str);
                }
            };
            BACKGROUND_MEM_PROCESS.Run();
        }

        private void runMEM_Install(string exe, string args, BackgroundWorker worker, List<string> acceptedIPC = null)
        {
            Debug.WriteLine("Running process: " + exe + " " + args);
            Log.Information("Running process: " + exe + " " + args);


            BACKGROUND_MEM_PROCESS = new ConsoleApp(exe, args);
            BACKGROUND_MEM_PROCESS_ERRORS = new List<string>();

            BACKGROUND_MEM_PROCESS.ConsoleOutput += (o, args2) =>
            {
                string str = args2.Line;
                if (str.StartsWith("[IPC]"))
                {
                    string command = str.Substring(5);
                    int endOfCommand = command.IndexOf(' ');
                    command = command.Substring(0, endOfCommand);
                    if (acceptedIPC == null || acceptedIPC.Contains(command))
                    {
                        string param = str.Substring(endOfCommand + 5).Trim();
                        switch (command)
                        {
                            //worker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, false));
                            //int percentInt = Convert.ToInt32(param);
                            //worker.ReportProgress(percentInt);
                            case "PROCESSING_FILE":
                            case "PROCESSING_MOD":
                                Log.Information("MEM Reports processing file: " + param);
                                //worker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, param));
                                break;
                            case "OVERALL_PROGRESS":
                            //This will be changed later
                            case "TASK_PROGRESS":
                                worker.ReportProgress(completed, new ThreadCommand(UPDATE_TASK_PROGRESS, param));
                                break;
                            case "PHASE":
                                Log.Warning("[TASK TIMING] End of stage " + INSTALL_STAGE + " " + stopwatch.ElapsedMilliseconds);
                                worker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, param));
                                int overallProgress = ProgressWeightPercentages.SubmitProgress(INSTALL_STAGE, 100);
                                worker.ReportProgress(completed, new ThreadCommand(SET_OVERALL_PROGRESS, overallProgress));
                                Interlocked.Increment(ref INSTALL_STAGE);
                                worker.ReportProgress(completed, new ThreadCommand(UPDATE_STAGE_LABEL));
                                break;
                            case "ERROR":
                                Log.Information("[ERROR] Realtime Process Output: " + param);
                                BACKGROUND_MEM_PROCESS_ERRORS.Add(param);
                                break;
                            default:
                                Log.Information("Unknown IPC command: " + command);
                                break;
                        }
                    }
                }
                else
                {
                    Log.Information("Realtime Process Output: " + str);
                }
            };
            BACKGROUND_MEM_PROCESS.Run();
        }

        private void runMEM_DetectBadMods(string exe, string args, BackgroundWorker worker, List<string> acceptedIPC = null)
        {
            Debug.WriteLine("Running process: " + exe + " " + args);
            Log.Information("Running process: " + exe + " " + args);


            BACKGROUND_MEM_PROCESS = new ConsoleApp(exe, args);
            BACKGROUND_MEM_PROCESS_ERRORS = new List<string>();

            BACKGROUND_MEM_PROCESS.ConsoleOutput += (o, args2) =>
            {
                string str = args2.Line;
                if (str.StartsWith("[IPC]"))
                {
                    string command = str.Substring(5);
                    int endOfCommand = command.IndexOf(' ');
                    command = command.Substring(0, endOfCommand);
                    if (acceptedIPC == null || acceptedIPC.Contains(command))
                    {
                        string param = str.Substring(endOfCommand + 5).Trim();
                        switch (command)
                        {
                            case "ERROR":
                                Log.Information("[ERROR] Realtime Process Output: " + param);
                                BACKGROUND_MEM_PROCESS_ERRORS.Add(param);
                                break;
                            default:
                                Log.Information("Unknown IPC command: " + command);
                                break;
                        }
                    }
                }
                else
                {
                    Log.Information("Realtime Process Output: " + str);
                }
            };
            BACKGROUND_MEM_PROCESS.Run();
        }


        private void RestoreGame(object sender, DoWorkEventArgs e)
        {
            string gamePath = Utilities.GetGamePath(BACKUP_THREAD_GAME, true);
            string backupPath = Utilities.GetGameBackupPath(BACKUP_THREAD_GAME);
            BackupWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, true));
            BackupWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Deleting existing game installation"));
            if (Directory.Exists(gamePath))
            {
                Log.Information("Deleting existing game directory: " + gamePath);
                try
                {
                    bool deletedDirectory = Utilities.DeleteFilesAndFoldersRecursively(gamePath);
                    if (deletedDirectory != true)
                    {
                        BackupWorker.ReportProgress(completed, new ThreadCommand(RESTORE_FAILED_COULD_NOT_DELETE_FOLDER));
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Exception deleting game directory: " + gamePath + ": " + ex.Message);
                }
            }
            Log.Information("Reverting lod settings");
            IniSettingsHandler.removeLOD(BACKUP_THREAD_GAME);
            Directory.CreateDirectory(gamePath);
            BackupWorker.ReportProgress(completed, new ThreadCommand(UPDATE_PROGRESSBAR_INDETERMINATE, false));
            BackupWorker.ReportProgress(completed, new ThreadCommand(UPDATE_OPERATION_LABEL, "Restoring game from backup "));
            if (gamePath != null)
            {
                CopyDir.CopyAll_ProgressBar(new DirectoryInfo(backupPath), new DirectoryInfo(gamePath), BackupWorker, -1, 0);
            }
            if (BACKUP_THREAD_GAME == 3)
            {
                //Check for cmmvanilla file and remove it present
                string file = gamePath + "cmm_vanilla";
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            e.Result = true;
        }
    }

}