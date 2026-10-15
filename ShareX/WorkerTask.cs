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
using ShareX.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using MessageBox = ShareX.AvaloniaUI.MessageBox;
using MessageBoxButtons = ShareX.AvaloniaUI.MessageBoxButtons;
using MessageBoxIcon = ShareX.AvaloniaUI.MessageBoxIcon;

namespace ShareX
{
    public class WorkerTask : IDisposable
    {
        public delegate void TaskEventHandler(WorkerTask task);
        public delegate void TaskImageEventHandler(WorkerTask task, Bitmap image);

        public event TaskEventHandler StatusChanged, TaskCompleted;
        public event TaskImageEventHandler ImageReady;

        public TaskInfo Info { get; private set; }
        public TaskStatus Status { get; private set; }
        public bool IsBusy => Status == TaskStatus.InQueue || IsWorking;
        public bool IsWorking => Status == TaskStatus.Preparing || Status == TaskStatus.Working || Status == TaskStatus.Stopping;
        public bool StopRequested { get; private set; }
        public bool RequestSettingUpdate { get; private set; }
        public Stream Data { get; private set; }
        public Bitmap Image { get; private set; }
        public bool KeepImage { get; set; }
        public string Text { get; private set; }

        private ThreadWorker threadWorker;

        #region Constructors

        private WorkerTask(TaskSettings taskSettings)
        {
            Status = TaskStatus.InQueue;
            Info = new TaskInfo(taskSettings);
        }

        public static WorkerTask CreateHistoryTask(RecentTask recentTask)
        {
            WorkerTask task = new WorkerTask(null);
            task.Status = TaskStatus.History;
            task.Info.FilePath = recentTask.FilePath;
            task.Info.FileName = recentTask.FileName;
            task.Info.TaskEndTime = recentTask.Time;

            return task;
        }

        public static WorkerTask CreateImageUploaderTask(TaskMetadata metadata, TaskSettings taskSettings, string customFileName = null)
        {
            WorkerTask task = new WorkerTask(taskSettings);
            task.Info.Job = TaskJob.Job;
            task.Info.DataType = EDataType.Image;

            if (!string.IsNullOrEmpty(customFileName))
            {
                task.Info.FileName = FileHelpers.AppendExtension(customFileName, "bmp");
            }
            else
            {
                task.Info.FileName = TaskHelpers.GetFileName(taskSettings, "bmp", metadata);
            }

            task.Info.Metadata = metadata;
            task.Image = metadata.Image;
            return task;
        }

        public static WorkerTask CreateFileJobTask(string filePath, TaskMetadata metadata, TaskSettings taskSettings, string customFileName = null)
        {
            WorkerTask task = new WorkerTask(taskSettings);
            task.Info.FilePath = filePath;
            task.Info.DataType = TaskHelpers.FindDataType(task.Info.FilePath, taskSettings);

            if (!string.IsNullOrEmpty(customFileName))
            {
                string ext = FileHelpers.GetFileNameExtension(task.Info.FilePath);
                task.Info.FileName = FileHelpers.AppendExtension(customFileName, ext);
            }
            else if (task.Info.TaskSettings.UploadSettings.FileUploadUseNamePattern)
            {
                string ext = FileHelpers.GetFileNameExtension(task.Info.FilePath);
                task.Info.FileName = TaskHelpers.GetFileName(task.Info.TaskSettings, ext);
            }

            task.Info.Metadata = metadata;
            task.Info.Job = TaskJob.Job;

            return task;
        }

        #endregion Constructors

        public void Start()
        {
            if (Status == TaskStatus.InQueue && !StopRequested)
            {
                Info.TaskStartTime = DateTime.Now;

                threadWorker = new ThreadWorker();
                Prepare();
                threadWorker.DoWork += ThreadDoWork;
                threadWorker.Completed += ThreadCompleted;
                threadWorker.Start(ApartmentState.STA);
            }
        }

        private void Prepare()
        {
            Status = TaskStatus.Preparing;

            Info.Status = Info.Job == TaskJob.Job
                ? Strings.UploadTask_Prepare_Preparing
                : Strings.UploadTask_Prepare_Starting;

            OnStatusChanged();
        }

        public void Stop()
        {
            StopRequested = true;

            switch (Status)
            {
                case TaskStatus.InQueue:
                    OnTaskCompleted();
                    break;
                case TaskStatus.Preparing:
                case TaskStatus.Working:
                    Status = TaskStatus.Stopping;
                    Info.Status = Strings.UploadTask_Stop_Stopping;
                    OnStatusChanged();
                    break;
            }
        }

        public void ShowErrorWindow()
        {
            if (Info != null && Info.Result != null && Info.Result.IsError)
            {
                string errors = Info.Result.ErrorsToString();

                if (!string.IsNullOrEmpty(errors))
                {
                    ErrorWindowIntegration.Show(Strings.UploadInfoManager_ShowErrors_Upload_errors, errors,
                        AppPaths.LogsFilePath, Links.GitHubIssues, false);
                }
            }
        }

        private void ThreadDoWork()
        {
            try
            {
                StopRequested = !DoThreadJob();

                OnImageReady();
            }
            finally
            {
                KeepImage = Image != null && Info.TaskSettings.GeneralSettings.ShowToastNotificationAfterTaskCompleted;

                Dispose();

                if (Info.Job == TaskJob.Job
                    && Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.DeleteFile) && !string.IsNullOrEmpty(Info.FilePath) && File.Exists(Info.FilePath))
                {
                    File.Delete(Info.FilePath);
                }
            }
        }

        private void AddErrorMessage(string error)
        {
            Info.Result ??= new TaskResult();
            Info.Result.AddError(error);
        }

        private bool DoThreadJob()
        {
            if (Info.Job == TaskJob.Job)
            {
                if (!DoAfterCaptureJobs())
                {
                    return false;
                }

                if (!DoFileJobs())
                {
                    return false;
                }
            }

            if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.DoOCR))
            {
                DoOCR();
            }

            return true;
        }

        private bool DoAfterCaptureJobs()
        {
            if (Image == null)
            {
                return true;
            }

            if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.BeautifyImage))
            {
                Image = TaskHelpers.BeautifyImage(Image, Info.TaskSettings);

                if (Image == null)
                {
                    return false;
                }
            }

            if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.AddImageEffects))
            {
                Image = TaskHelpers.ApplyImageEffects(Image, Info.TaskSettings.ImageSettingsReference);

                if (Image == null)
                {
                    DebugHelper.WriteLine("Error: Applying image effects resulted empty image.");
                    return false;
                }
            }

            if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.AnnotateImage))
            {
                Image = TaskHelpers.AnnotateImage(Image, null, Info.TaskSettings, true);

                if (Image == null)
                {
                    return false;
                }
            }

            if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyImageToClipboard))
            {
                if (ClipboardHelpers.CopyImage(Image, Info.FileName))
                {
                    DebugHelper.WriteLine("Image copied to clipboard.");
                }
                else
                {
                    DebugHelper.WriteLine("Error: Image could not be copied to clipboard.");
                }
            }

            if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.PinToScreen))
            {
                Image imageCopy = Image.CloneSafe();
                TaskHelpers.PinToScreen(imageCopy, Info.TaskSettings);
            }

            if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.SendImageToPrinter))
            {
                TaskHelpers.PrintImage(Image);
            }

            Info.Metadata.Image = Image;

            if (Info.TaskSettings.AfterCaptureJob.HasFlagAny(AfterCaptureTasks.SaveImageToFile, AfterCaptureTasks.SaveImageToFileWithDialog,
                AfterCaptureTasks.DoOCR))
            {
                ImageData imageData = TaskHelpers.PrepareImage(Image, Info.TaskSettings);
                Data = imageData.ImageStream;
                Info.FileName = Path.ChangeExtension(Info.FileName, imageData.ImageFormat.GetDescription());

                if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.SaveImageToFile))
                {
                    string screenshotsFolder = TaskHelpers.GetScreenshotsFolder(Info.TaskSettings, Info.Metadata);
                    string filePath = TaskHelpers.HandleExistsFile(screenshotsFolder, Info.FileName, Info.TaskSettings);

                    if (!string.IsNullOrEmpty(filePath))
                    {
                        Info.FilePath = filePath;
                        imageData.Write(Info.FilePath);
                        DebugHelper.WriteLine("Image saved to file: " + Info.FilePath);
                    }
                }

                if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.SaveImageToFileWithDialog))
                {
                    using (SaveFileDialog sfd = new SaveFileDialog())
                    {
                        string initialDirectory = null;

                        if (!string.IsNullOrEmpty(HelpersOptions.LastSaveDirectory) && Directory.Exists(HelpersOptions.LastSaveDirectory))
                        {
                            initialDirectory = HelpersOptions.LastSaveDirectory;
                        }
                        else
                        {
                            initialDirectory = TaskHelpers.GetScreenshotsFolder(Info.TaskSettings, Info.Metadata);
                        }

                        bool imageSaved;

                        do
                        {
                            sfd.InitialDirectory = initialDirectory;
                            sfd.FileName = Info.FileName;
                            sfd.DefaultExt = Path.GetExtension(Info.FileName).Substring(1);
                            sfd.Filter = string.Format("*{0}|*{0}|{1}|*.*", Path.GetExtension(Info.FileName), Strings.WorkerTask_AllFilesFilter);
                            sfd.Title = Strings.UploadTask_DoAfterCaptureJobs_Choose_a_folder_to_save + " " + Path.GetFileName(Info.FileName);

                            if (sfd.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(sfd.FileName))
                            {
                                Info.FilePath = sfd.FileName;
                                HelpersOptions.LastSaveDirectory = Path.GetDirectoryName(Info.FilePath);
                                imageSaved = imageData.Write(Info.FilePath);

                                if (imageSaved)
                                {
                                    DebugHelper.WriteLine("Image saved to file with dialog: " + Info.FilePath);
                                }
                            }
                            else
                            {
                                break;
                            }
                        } while (!imageSaved);
                    }
                }

                if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.SaveThumbnailImageToFile))
                {
                    string thumbnailFileName, thumbnailFolder;

                    if (!string.IsNullOrEmpty(Info.FilePath))
                    {
                        thumbnailFileName = Path.GetFileName(Info.FilePath);
                        thumbnailFolder = Path.GetDirectoryName(Info.FilePath);
                    }
                    else
                    {
                        thumbnailFileName = Info.FileName;
                        thumbnailFolder = TaskHelpers.GetScreenshotsFolder(Info.TaskSettings, Info.Metadata);
                    }

                    Info.ThumbnailFilePath = TaskHelpers.CreateThumbnail(Image, thumbnailFolder, thumbnailFileName, Info.TaskSettings);

                    if (!string.IsNullOrEmpty(Info.ThumbnailFilePath))
                    {
                        DebugHelper.WriteLine("Thumbnail saved to file: " + Info.ThumbnailFilePath);
                    }
                }
            }

            return true;
        }

        private bool DoFileJobs()
        {
            if (!string.IsNullOrEmpty(Info.FilePath) && File.Exists(Info.FilePath))
            {
                if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.PerformActions) && Info.TaskSettings.ExternalPrograms != null)
                {
                    IEnumerable<ExternalProgram> actions = Info.TaskSettings.ExternalPrograms.Where(x => x.IsActive);

                    if (actions.Count() > 0)
                    {
                        bool isFileModified = false;
                        string fileName = Info.FileName;

                        foreach (ExternalProgram fileAction in actions)
                        {
                            string modifiedPath = fileAction.Run(Info.FilePath);

                            string resultPath = string.IsNullOrEmpty(modifiedPath) ? Info.FilePath : modifiedPath;

                            if (!File.Exists(resultPath))
                            {
                                DebugHelper.WriteLine($"Action result file does not exist: \"{resultPath}\"");
                                return false;
                            }

                            if (!string.IsNullOrEmpty(modifiedPath))
                            {
                                isFileModified = true;
                                Info.FilePath = modifiedPath;

                                if (Data != null)
                                {
                                    Data.Dispose();
                                }

                                fileAction.DeletePendingInputFile();
                            }
                        }

                        if (isFileModified)
                        {
                            string extension = FileHelpers.GetFileNameExtension(Info.FilePath);
                            Info.FileName = FileHelpers.ChangeFileNameExtension(fileName, extension);

                            if (!LoadFileStream())
                            {
                                return false;
                            }
                        }
                    }
                }

                if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyFileToClipboard))
                {
                    ClipboardHelpers.CopyFile(Info.FilePath);
                }
                else if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyFilePathToClipboard))
                {
                    ClipboardHelpers.CopyText(Info.FilePath);
                }
                else if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.CopyFolderPathToClipboard))
                {
                    ClipboardHelpers.CopyText(Path.GetDirectoryName(Info.FilePath));
                }

                if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.ShowInExplorer))
                {
                    FileHelpers.OpenFolderWithFile(Info.FilePath);
                }

                if (Info.TaskSettings.AfterCaptureJob.HasFlag(AfterCaptureTasks.ScanQRCode) && Info.DataType == EDataType.Image)
                {
                    TaskHelpers.OpenQRCodeScanFromImageFile(Info.FilePath);
                }
            }

            return true;
        }

        private void DoOCR()
        {
            if (Image != null && Info.DataType == EDataType.Image)
            {
                TaskHelpers.OCRImage(Image, Info.TaskSettings).GetAwaiter().GetResult();
            }
        }

        private bool LoadFileStream()
        {
            try
            {
                Data = new FileStream(Info.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception e)
            {
                e.ShowError();
                return false;
            }

            return true;
        }

        private void ThreadCompleted()
        {
            OnTaskCompleted();
        }

        private void OnStatusChanged()
        {
            if (StatusChanged != null)
            {
                threadWorker.InvokeAsync(() => StatusChanged(this));
            }
        }

        private void OnImageReady()
        {
            if (ImageReady != null)
            {
                Bitmap image = null;

                if (Image != null)
                {
                    image = (Bitmap)Image.Clone();
                }

                threadWorker.InvokeAsync(() =>
                {
                    using (image)
                    {
                        ImageReady(this, image);
                    }
                });
            }
        }

        private void OnTaskCompleted()
        {
            Info.TaskEndTime = DateTime.Now;

            if (StopRequested)
            {
                Status = TaskStatus.Stopped;
                Info.Status = Strings.UploadTask_OnUploadCompleted_Stopped;
            }
            else if (Info.Result.IsError)
            {
                Status = TaskStatus.Failed;
                Info.Status = Strings.TaskManager_task_UploadCompleted_Error;
            }
            else
            {
                Status = TaskStatus.Completed;
                Info.Status = Strings.UploadTask_OnUploadCompleted_Done;
            }

            TaskCompleted?.Invoke(this);

            Dispose();
        }

        public void Dispose()
        {
            if (Data != null)
            {
                Data.Dispose();
                Data = null;
            }

            if (!KeepImage && Image != null)
            {
                Image.Dispose();
                Image = null;
            }
        }
    }
}
