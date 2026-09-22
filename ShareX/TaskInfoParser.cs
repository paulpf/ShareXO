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
using System.IO;

namespace ShareX
{
    public class TaskInfoParser : NameParser
    {
        public string Parse(TaskInfo info, string pattern)
        {
            if (info != null && !string.IsNullOrEmpty(pattern))
            {
                pattern = Parse(pattern);

                pattern = pattern.Replace("$result", info.FilePath ?? "");
                pattern = pattern.Replace("$filenamenoext", !string.IsNullOrEmpty(info.FileName) ? Path.GetFileNameWithoutExtension(info.FileName) : "");
                pattern = pattern.Replace("$filename", info.FileName ?? "");
                pattern = pattern.Replace("$filepath", info.FilePath ?? "");
                pattern = pattern.Replace("$folderpath", !string.IsNullOrEmpty(info.FilePath) ? Path.GetDirectoryName(info.FilePath) : "");
                pattern = pattern.Replace("$foldername", !string.IsNullOrEmpty(info.FilePath) ? Path.GetFileName(Path.GetDirectoryName(info.FilePath)) : "");
                pattern = pattern.Replace("$thumbnailfilenamenoext", !string.IsNullOrEmpty(info.ThumbnailFilePath) ? Path.GetFileNameWithoutExtension(info.ThumbnailFilePath) : "");
                pattern = pattern.Replace("$thumbnailfilename", !string.IsNullOrEmpty(info.ThumbnailFilePath) ? Path.GetFileName(info.ThumbnailFilePath) : "");
            }

            return pattern;
        }
    }
}