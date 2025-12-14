// Assets/MotionRetargeting/Editor/MotionRetargetingJobs.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MotionRetargeting.Editor
{
    [Serializable]
    public class JobRecord
    {
        public string jobId;
        public string fileName;
        public string status; // queued, running, done, error, downloaded ...
        public string message;
        public int r2etEtaSeconds;
        public int studentEtaSeconds; 
    }

    [Serializable]
    public class JobRecordList
    {
        public List<JobRecord> jobs = new List<JobRecord>();
    }

    public static class MotionRetargetingJobs
    {
        private static string JobsFilePath =>
            Path.Combine(Application.dataPath, "MotionRetargetingJobs.json");

        private static JobRecordList _cache;

        public static JobRecordList Load()
        {
            if (_cache != null) return _cache;

            if (!File.Exists(JobsFilePath))
            {
                _cache = new JobRecordList();
                return _cache;
            }

            var json = File.ReadAllText(JobsFilePath);
            _cache = JsonUtility.FromJson<JobRecordList>(json);
            if (_cache == null)
                _cache = new JobRecordList();
            return _cache;
        }

        public static void Save(JobRecordList list = null)
        {
            if (list != null)
                _cache = list;
            if (_cache == null)
                _cache = new JobRecordList();

            var json = JsonUtility.ToJson(_cache, true);
            File.WriteAllText(JobsFilePath, json);
        }
    }
}
