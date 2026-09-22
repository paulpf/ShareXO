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

using ShareX.HelpersLib;
using ShareX.Tools;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ShareX
{
    public static class TaskLauncher
    {
        public static void RunImageTask(Bitmap bmp, TaskSettings taskSettings, bool skipQuickTaskMenu = false, bool skipAfterCaptureWindow = false)
        {
            TaskMetadata metadata = new TaskMetadata(bmp);
            RunImageTask(metadata, taskSettings, skipQuickTaskMenu, skipAfterCaptureWindow);
        }

        public static void RunImageTask(TaskMetadata metadata, TaskSettings taskSettings, bool skipQuickTaskMenu = false, bool skipAfterCaptureWindow = false)
        {
            if (taskSettings == null) taskSettings = TaskSettings.GetDefaultTaskSettings();

            if (metadata != null && metadata.Image != null && taskSettings != null)
            {
                if (!skipQuickTaskMenu && taskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.ShowQuickTaskMenu))
                {
                    QuickTaskMenu quickTaskMenu = new QuickTaskMenu();

                    quickTaskMenu.TaskInfoSelected += taskInfo =>
                    {
                        if (taskInfo == null)
                        {
                            RunImageTask(metadata, taskSettings, true);
                        }
                        else if (taskInfo.IsValid)
                        {
                            taskSettings.AfterCaptureJob = taskInfo.AfterCaptureTasks;
                            RunImageTask(metadata, taskSettings, true);
                        }
                    };

                    quickTaskMenu.ShowMenu();

                    return;
                }

                void StartImageTask(string customFileName)
                {
                    WorkerTask task = WorkerTask.CreateImageUploaderTask(metadata, taskSettings, customFileName);
                    TaskManager.Start(task);
                }

                if (!skipAfterCaptureWindow)
                {
                    TaskHelpers.ShowAfterCaptureWindow(taskSettings, result =>
                    {
                        if (result.Accepted)
                        {
                            StartImageTask(result.FileName);
                        }
                    }, metadata);

                    return;
                }

                StartImageTask(null);
            }
        }

        public static void IndexFolder(TaskSettings taskSettings = null)
        {
            string selectedPath = FileHelpers.BrowseFolder();

            if (!string.IsNullOrEmpty(selectedPath))
            {
                IndexFolder(selectedPath, taskSettings);
            }
        }

        public static void IndexFolder(string folderPath, TaskSettings taskSettings = null)
        {
            if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
            {
                if (taskSettings == null) taskSettings = TaskSettings.GetDefaultTaskSettings();

                taskSettings.ToolsSettings.IndexerSettings.BinaryUnits = ApplicationState.Settings.BinaryUnits;

                string source = null;

                Task.Run(() =>
                {
                    source = Indexer.Index(folderPath, taskSettings.ToolsSettings.IndexerSettings);
                }).ContinueInCurrentContext(() =>
                {
                    if (!string.IsNullOrEmpty(source))
                    {
                        string extension = taskSettings.ToolsSettings.IndexerSettings.Output.ToString().ToLower();
                        string fileName = TaskHelpers.GetFileName(taskSettings, extension);
                        string screenshotsFolder = TaskHelpers.GetScreenshotsFolder(taskSettings);
                        string filePath = TaskHelpers.HandleExistsFile(screenshotsFolder, fileName, taskSettings);

                        if (!string.IsNullOrEmpty(filePath))
                        {
                            FileHelpers.CreateDirectoryFromFilePath(filePath);
                            File.WriteAllText(filePath, source, Encoding.UTF8);
                            FileHelpers.OpenFile(filePath);
                        }
                    }
                });
            }
        }
    }
}
