﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using ALOTInstallerCore.Helpers;
using Serilog;

namespace ALOTInstallerCore
{
    // WINDOWS SPECIFIC ITEMS IN UTILITIES
#if WINDOWS
    public static partial class Utilities
    {

        public const int WIN32_EXCEPTION_ELEVATED_CODE = -98763;
        [DllImport("kernel32.dll")]
        static extern uint GetLastError();

        public static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static bool GrantAccess(string fullPath)
        {
            try
            {
                DirectoryInfo dInfo = new DirectoryInfo(fullPath);
                DirectorySecurity dSecurity = dInfo.GetAccessControl();
                dSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.FullControl, InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit, PropagationFlags.NoPropagateInherit, AccessControlType.Allow));
                dInfo.SetAccessControl(dSecurity);
            }
            catch (Exception e)
            {
                Log.Error("[AICORE] Error granting write access: " + e.Message);
                return false;
            }
            return true;
        }

        public static bool MakeAllFilesInDirReadWrite(string directory)
        {
            Log.Information("[AICORE] Marking all files in directory to read-write: " + directory);
            //Log.Warning("[AICORE] If the application crashes after this statement, please come to to the ALOT discord - this is an issue we have not yet been able to reproduce and thus can't fix without outside assistance.");
            var di = new DirectoryInfo(directory);
            foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
            {
                if (!file.Exists)
                {
                    Log.Warning("[AICORE] File is no longer in the game directory: " + file.FullName);
                    Log.Error("[AICORE] This file was found when the read-write filescan took place, but is no longer present. The application is going to crash.");
                    Log.Error("[AICORE] Another session may be running, or there may be a bug. Please come to the ALOT Discord so we can analyze this as we cannot reproduce it.");
                }

                //Utilities.WriteDebugLog("Clearing read-only marker (if any) on file: " + file.FullName);
                try
                {
                    file.Attributes &= ~FileAttributes.ReadOnly;
                }
                catch (DirectoryNotFoundException ex)
                {
                    if (file.FullName.Length > 260)
                    {
                        Log.Error("[AICORE] Path of file is too long - Windows API length limitations for files will cause errors. File: " + file.FullName);
                        Log.Error("[AICORE] The game is either nested too deep or a mod has been improperly installed causing a filepath to be too long.");
                        ex.WriteToLog("[AICORE] ");
                    }
                    return false;
                }
            }
            return true;
        }

        public static bool OpenAndSelectFileInExplorer(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
            {
                return false;
            }
            //Clean up file path so it can be navigated OK
            filePath = System.IO.Path.GetFullPath(filePath);
            System.Diagnostics.Process.Start("explorer.exe", string.Format("/select,\"{0}\"", filePath));
            return true;
        }

        public static void GetAntivirusInfo()
        {
            ManagementObjectSearcher wmiData = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT * FROM AntivirusProduct");
            ManagementObjectCollection data = wmiData.Get();

            foreach (ManagementObject virusChecker in data)
            {
                var virusCheckerName = virusChecker["displayName"];
                var productState = virusChecker["productState"];
                uint productVal = (uint)productState;
                var bytes = BitConverter.GetBytes(productVal);
                Log.Information("[AICORE] Antivirus info: " + virusCheckerName + " with state " + bytes[1].ToString("X2") + " " + bytes[2].ToString("X2") + " " + bytes[3].ToString("X2"));
            }
        }
}
#endif

}
