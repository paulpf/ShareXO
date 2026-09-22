#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using ShareX.HistoryLib;
using ShareX.Localization;
using ShareX.ScreenCaptureLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessageBox = ShareX.AvaloniaUI.MessageBox;
using MessageBoxButtons = ShareX.AvaloniaUI.MessageBoxButtons;
using MessageBoxIcon = ShareX.AvaloniaUI.MessageBoxIcon;

namespace ShareX
{
    internal static class SettingManager
    {
        private const string ApplicationConfigFileName = "ApplicationConfig.json";

        private static string ApplicationConfigFilePath
        {
            get
            {
                if (StartupOptions.Sandbox) return null;

                return Path.Combine(AppPaths.PersonalFolder, ApplicationConfigFileName);
            }
        }

        private const string HotkeysConfigFileName = "HotkeysConfig.json";

        private static string HotkeysConfigFilePath
        {
            get
            {
                if (StartupOptions.Sandbox) return null;

                string hotkeysConfigFolder;

                if (Settings != null && !string.IsNullOrEmpty(Settings.CustomHotkeysConfigPath))
                {
                    hotkeysConfigFolder = FileHelpers.ExpandFolderVariables(Settings.CustomHotkeysConfigPath);
                }
                else
                {
                    hotkeysConfigFolder = AppPaths.PersonalFolder;
                }

                return Path.Combine(hotkeysConfigFolder, HotkeysConfigFileName);
            }
        }

        public static string BackupFolder => Path.Combine(AppPaths.PersonalFolder, "Backup");

        private static ApplicationConfig Settings { get => ApplicationState.SettingsOrNull; set => ApplicationState.Settings = value; }
        private static TaskSettings DefaultTaskSettings { get => ApplicationState.DefaultTaskSettings; set => ApplicationState.DefaultTaskSettings = value; }
        private static HotkeysConfig HotkeysConfig { get => ApplicationState.HotkeysConfigOrNull; set => ApplicationState.HotkeysConfig = value; }

        private static ManualResetEvent hotkeysConfigResetEvent = new ManualResetEvent(false);

        public static void LoadInitialSettings()
        {
            LoadApplicationConfig();

            Task.Run(() =>
            {
                LoadHotkeysConfig();
                hotkeysConfigResetEvent.Set();
            });
        }

        public static void WaitHotkeysConfig()
        {
            if (HotkeysConfig == null)
            {
                hotkeysConfigResetEvent.WaitOne();
            }
        }

        public static void LoadApplicationConfig(bool fallbackSupport = true)
        {
            Settings = ApplicationConfig.Load(ApplicationConfigFilePath, BackupFolder, fallbackSupport);
            Settings.CreateBackup = true;
            Settings.CreateWeeklyBackup = true;
            Settings.SettingsSaveFailed += Settings_SettingsSaveFailed;
            DefaultTaskSettings = Settings.DefaultTaskSettings;
            ApplicationConfigBackwardCompatibilityTasks();
            Settings.ThemeOptions ??= new ApplicationThemeOptions();
            ThemeManager.Configure(Settings.ThemeOptions);
            MigrateHistoryFile();
            HistoryConnect();
        }

        private static void Settings_SettingsSaveFailed(Exception e)
        {
            string message;

            if (e is UnauthorizedAccessException || e is FileNotFoundException)
            {
                message = Strings.YourAntiVirusSoftwareOrTheControlledFolderAccessFeatureInWindowsCouldBeBlockingShareX;
            }
            else
            {
                message = e.Message;
            }

            TaskHelpers.ShowNotificationTip(message, "ShareX - " + Strings.FailedToSaveSettings, 5000);
        }

        public static void LoadHotkeysConfig(bool fallbackSupport = true)
        {
            HotkeysConfig = HotkeysConfig.Load(HotkeysConfigFilePath, BackupFolder, fallbackSupport);
            HotkeysConfig.CreateBackup = true;
            HotkeysConfig.CreateWeeklyBackup = true;
            HotkeysConfigBackwardCompatibilityTasks();
        }

        public static void LoadAllSettings()
        {
            LoadApplicationConfig();
            LoadHotkeysConfig();
        }

        private static void ApplicationConfigBackwardCompatibilityTasks()
        {
            if (Settings.IsUpgradeFrom("14.1.2"))
            {
                if (!Environment.Is64BitOperatingSystem && !string.IsNullOrEmpty(DefaultTaskSettings.CaptureSettings.FFmpegOptions.CLIPath))
                {
                    DefaultTaskSettings.CaptureSettings.FFmpegOptions.OverrideCLIPath = true;
                }
            }

            if (Settings.IsUpgradeFrom("15.0.1"))
            {
                DefaultTaskSettings.CaptureSettings.ScrollingCaptureOptions = new ScrollingCaptureOptions();
                DefaultTaskSettings.CaptureSettings.FFmpegOptions.FixSources();
            }

            if (Settings.IsUpgradeFrom("16.0.2"))
            {
                if (Settings.CheckPreReleaseUpdates)
                {
                    Settings.UpdateChannel = UpdateChannel.PreRelease;
                }
            }
        }

        public static void HistoryConnect()
        {
            HistoryClose();
            ApplicationState.HistoryManager = new HistoryManagerSQLite(AppPaths.HistoryFilePath);
        }

        public static void HistoryClose()
        {
            if (ApplicationState.HistoryManager != null)
            {
                ApplicationState.HistoryManager.Dispose();
                ApplicationState.HistoryManager = null;
            }
        }

        private static void MigrateHistoryFile()
        {
            if (File.Exists(AppPaths.HistoryFilePathOld))
            {
                try
                {
                    if (!File.Exists(AppPaths.HistoryFilePath))
                    {
                        DebugHelper.WriteLine($"Migrating JSON history file \"{AppPaths.HistoryFilePathOld}\" to SQLite history file \"{AppPaths.HistoryFilePath}\"");

                        using (HistoryManagerSQLite historyManager = new HistoryManagerSQLite(AppPaths.HistoryFilePath))
                        {
                            historyManager.MigrateFromJSON(AppPaths.HistoryFilePathOld);
                        }
                    }

                    FileHelpers.MoveFile(AppPaths.HistoryFilePathOld, BackupFolder);
                }
                catch (Exception e)
                {
                    DebugHelper.WriteException(e);
                    e.ShowError();
                }
            }
        }

        private static void HotkeysConfigBackwardCompatibilityTasks()
        {
            if (Settings.IsUpgradeFrom("15.0.1"))
            {
                foreach (TaskSettings taskSettings in HotkeysConfig.Hotkeys.Select(x => x.TaskSettings))
                {
                    if (taskSettings != null && taskSettings.CaptureSettings != null)
                    {
                        taskSettings.CaptureSettings.ScrollingCaptureOptions = new ScrollingCaptureOptions();
                        taskSettings.CaptureSettings.FFmpegOptions.FixSources();
                    }
                }
            }
        }

        public static void CleanupHotkeysConfig()
        {
            foreach (TaskSettings taskSettings in HotkeysConfig.Hotkeys.Select(x => x.TaskSettings))
            {
                taskSettings.Cleanup();
            }
        }

        public static void SaveAllSettings()
        {
            if (Settings != null)
            {
                Settings.Save(ApplicationConfigFilePath);
            }

            if (HotkeysConfig != null)
            {
                CleanupHotkeysConfig();
                HotkeysConfig.Save(HotkeysConfigFilePath);
            }
        }

        public static void SaveApplicationConfigAsync()
        {
            if (Settings != null)
            {
                Settings.SaveAsync(ApplicationConfigFilePath);
            }
        }

        public static void SaveHotkeysConfigAsync()
        {
            if (HotkeysConfig != null)
            {
                CleanupHotkeysConfig();
                HotkeysConfig.SaveAsync(HotkeysConfigFilePath);
            }
        }

        public static void SaveAllSettingsAsync()
        {
            SaveApplicationConfigAsync();
            SaveHotkeysConfigAsync();
        }

        public static void ResetSettings()
        {
            if (File.Exists(ApplicationConfigFilePath)) File.Delete(ApplicationConfigFilePath);
            LoadApplicationConfig(false);

            if (File.Exists(HotkeysConfigFilePath)) File.Delete(HotkeysConfigFilePath);
            LoadHotkeysConfig(false);
        }

        public static bool Export(string archivePath, bool settings, bool history)
        {
            MemoryStream msApplicationConfig = null, msHotkeysConfig = null;

            try
            {
                List<ZipEntryInfo> entries = new List<ZipEntryInfo>();

                if (settings)
                {
                    msApplicationConfig = Settings.SaveToMemoryStream(false);
                    entries.Add(new ZipEntryInfo(msApplicationConfig, ApplicationConfigFileName));

                    msHotkeysConfig = HotkeysConfig.SaveToMemoryStream(false);
                    entries.Add(new ZipEntryInfo(msHotkeysConfig, HotkeysConfigFileName));
                }

                if (history)
                {
                    entries.Add(new ZipEntryInfo(AppPaths.HistoryFilePath));
                    HistoryClose();
                }

                ZipManager.Compress(archivePath, entries);
                return true;
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e);
                MessageBox.Show(string.Format(Strings.SettingManager_ExportBackupError, e), Strings.SettingManager_ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                msApplicationConfig?.Dispose();
                msHotkeysConfig?.Dispose();

                if (history)
                {
                    HistoryConnect();
                }
            }

            return false;
        }

        public static bool Import(string archivePath)
        {
            try
            {
                HistoryClose();

                ZipManager.Extract(archivePath, AppPaths.PersonalFolder, true, entry =>
                {
                    return FileHelpers.CheckExtension(entry.Name, new string[] { "json", "xml" });
                }, 1_000_000_000);

                return true;
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e);
                MessageBox.Show(string.Format(Strings.SettingManager_ImportBackupError, e), Strings.SettingManager_ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                HistoryConnect();
            }

            return false;
        }
    }
}
