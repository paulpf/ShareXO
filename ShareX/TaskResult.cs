using System;
using System.Collections.Generic;
using System.Linq;

namespace ShareX
{
    /// <summary>
    /// Outcome of a task. Replaces the uploader result object that used to carry the
    /// resulting URLs; only the error list survived the removal of the uploaders.
    /// </summary>
    public class TaskResult
    {
        public List<string> Errors { get; } = new List<string>();

        public bool IsError => Errors.Count > 0;

        public void AddError(string error)
        {
            if (!string.IsNullOrEmpty(error))
            {
                Errors.Add(error);
            }
        }

        public void AddError(Exception exception)
        {
            if (exception != null)
            {
                AddError(exception.ToString());
            }
        }

        public string ErrorsToString()
        {
            return IsError ? string.Join(Environment.NewLine + Environment.NewLine, Errors) : null;
        }

        public string FirstErrorLine()
        {
            return Errors.FirstOrDefault()?.Split('\n').FirstOrDefault()?.Trim();
        }
    }
}
