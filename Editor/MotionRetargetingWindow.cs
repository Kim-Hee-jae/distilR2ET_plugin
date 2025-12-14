using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using System.IO.Compression;
using Unity.EditorCoroutines.Editor;

namespace MotionRetargeting.Editor
{
    public class MotionRetargetingWindow : EditorWindow
    {
        private Object fbxAsset;
        private string statusMessage = "대기 중";
        private Vector2 scroll;

        private MotionRetargetingConfig config;
        private JobRecordList jobList;

        [MenuItem("Tools/Motion Retargeting")]
        public static void ShowWindow()
        {
            var window = GetWindow<MotionRetargetingWindow>("Motion Retargeting");
            window.Show();
        }

        private void OnEnable()
        {
            config = MotionRetargetingConfig.LoadOrCreate();
            jobList = MotionRetargetingJobs.Load();

            if (jobList == null)
                jobList = new JobRecordList();
            if (jobList.jobs == null)
                jobList.jobs = new System.Collections.Generic.List<JobRecord>();

            // 단일 작업만 허용: 1개 초과면 앞에 것만 남기고 제거
            if (jobList.jobs.Count > 1)
            {
                jobList.jobs.RemoveRange(1, jobList.jobs.Count - 1);
                MotionRetargetingJobs.Save(jobList);
            }
        }

        private void OnGUI()
        {
            if (config == null)
                config = MotionRetargetingConfig.LoadOrCreate();

            EditorGUILayout.LabelField("서버 설정", EditorStyles.boldLabel);
            config.serverBaseUrl = EditorGUILayout.TextField("Server URL", config.serverBaseUrl);
            config.downloadFolderRelative = EditorGUILayout.TextField("Download Folder (under Assets)", config.downloadFolderRelative);
            if (GUILayout.Button("설정 저장"))
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("상태: " + statusMessage);
            EditorGUILayout.Space(10);

            // 항상 0 또는 1개의 Job만
            JobRecord currentJob = (jobList != null && jobList.jobs.Count > 0)
                ? jobList.jobs[0]
                : null;

            if (currentJob == null)
            {
                DrawNoJobUI();
            }
            else
            {
                DrawSingleJobUI(currentJob);
            }
        }

        // ==========================
        // Job 이 없는 경우: 업로드만 가능
        // ==========================
        private void DrawNoJobUI()
        {
            EditorGUILayout.LabelField("1. FBX 업로드 & 학습 요청", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("현재 진행 중인 Job이 없습니다. 새 FBX를 업로드해서 요청을 보낼 수 있습니다.", MessageType.Info);

            fbxAsset = EditorGUILayout.ObjectField("타겟 FBX", fbxAsset, typeof(Object), false);

            bool canUpload = false;
            string assetPath = null;
            string fullPath = null;

            if (fbxAsset != null)
            {
                assetPath = AssetDatabase.GetAssetPath(fbxAsset);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    fullPath = Path.GetFullPath(assetPath);
                    canUpload = File.Exists(fullPath);
                }
            }

            if (!string.IsNullOrEmpty(assetPath))
                EditorGUILayout.LabelField("Asset Path", assetPath);
            if (!string.IsNullOrEmpty(fullPath))
                EditorGUILayout.LabelField("Full Path", fullPath);

            EditorGUILayout.Space(10);

            GUI.enabled = canUpload;
            if (GUILayout.Button("업로드 및 Job 생성"))
            {
                if (jobList.jobs.Count > 0)
                {
                    EditorUtility.DisplayDialog(
                        "Job 이미 존재",
                        "이미 Job이 존재합니다. 새 요청을 보내기 전에 기존 Job을 삭제하세요.",
                        "확인"
                    );
                }
                else
                {
                    statusMessage = "업로드 중...";
                    EditorCoroutineUtility.StartCoroutineOwnerless(UploadAndCreateJob(fullPath));
                }
            }
            GUI.enabled = true;
        }

        // ==========================
        // Job 이 하나 있는 경우: 상태/다운로드/삭제
        // ==========================
        private void DrawSingleJobUI(JobRecord job)
        {
            EditorGUILayout.LabelField("현재 Job", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "한 번에 1개의 작업만 처리할 수 있습니다.\n" +
                "새 요청을 보내려면 이 Job을 삭제하거나, 결과를 다운로드한 뒤 삭제하세요.",
                MessageType.Info
            );

            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"Job ID: {job.jobId}");
            EditorGUILayout.LabelField($"File: {job.fileName}");
            EditorGUILayout.LabelField($"Status: {job.status}");
            EditorGUILayout.LabelField($"Message: {job.message}");
            EditorGUILayout.LabelField($"R2ET 남은 시간: {FormatEta(job.r2etEtaSeconds)}");
            EditorGUILayout.LabelField($"Student 남은 시간: {FormatEta(job.studentEtaSeconds)}");

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("상태 새로고침"))
            {
                EditorCoroutineUtility.StartCoroutineOwnerless(RefreshSingleJob(job));
            }

            GUI.enabled = (job.status == "done");
            if (GUILayout.Button("결과 다운로드"))
            {
                EditorCoroutineUtility.StartCoroutineOwnerless(DownloadResult(job));
            }
            GUI.enabled = true;

            if (GUILayout.Button("Job 삭제"))
            {
                bool confirm = EditorUtility.DisplayDialog(
                    "Job 삭제",
                    "이 Job을 삭제하면 서버 측 폴더와 로컬 Job 기록이 모두 삭제됩니다.\n계속하시겠습니까?",
                    "삭제", "취소"
                );
                if (confirm)
                {
                    EditorCoroutineUtility.StartCoroutineOwnerless(DeleteJob(job));
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }

        // ==========================
        // 서버 통신 코루틴
        // ==========================

        // 업로드 + Job 생성
        private IEnumerator UploadAndCreateJob(string fbxPath)
        {
            byte[] fileData = File.ReadAllBytes(fbxPath);
            var form = new WWWForm();
            form.AddBinaryData("file", fileData, Path.GetFileName(fbxPath), "application/octet-stream");

            using (UnityWebRequest www = UnityWebRequest.Post(config.serverBaseUrl + "/api/upload", form))
            {
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("업로드 실패: " + www.error);
                    statusMessage = "업로드 실패: " + www.error;
                    yield break;
                }

                var json = www.downloadHandler.text;
                var res = JsonUtility.FromJson<UploadResponse>(FixJson(json));

                statusMessage = $"Job 생성 완료: {res.jobId}";
                Debug.Log(statusMessage);

                if (jobList == null) jobList = new JobRecordList();
                jobList.jobs.Clear(); // 단일 Job 유지
                jobList.jobs.Add(new JobRecord
                {
                    jobId = res.jobId,
                    fileName = Path.GetFileName(fbxPath),
                    status = res.status,
                    message = "",
                    r2etEtaSeconds = -1,
                    studentEtaSeconds = -1
                });
                MotionRetargetingJobs.Save(jobList);
                Repaint();
            }
        }

        // 단일 Job 상태 조회
        private IEnumerator RefreshSingleJob(JobRecord job)
        {
            string url = $"{config.serverBaseUrl}/api/status?jobId={job.jobId}";
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("상태 조회 실패: " + www.error);
                    statusMessage = "상태 조회 실패: " + www.error;

                    // 상태 조회 실패 시 error로 표시
                    job.status = "error";
                    job.message = "상태조회 실패";
                    MotionRetargetingJobs.Save(jobList);
                    Repaint();
                    yield break;
                }

                var json = www.downloadHandler.text;
                var res = JsonUtility.FromJson<StatusResponse>(FixJson(json));

                job.status = res.status;
                job.message = res.message;
                job.r2etEtaSeconds = res.r2etEtaSeconds;
                job.studentEtaSeconds = res.studentEtaSeconds;

                MotionRetargetingJobs.Save(jobList);

                statusMessage = $"Job {job.jobId} 상태: {res.status} ({res.message})";
                Repaint();
            }
        }

        // 결과 ZIP 다운로드
        private IEnumerator DownloadResult(JobRecord job)
        {
            string url = $"{config.serverBaseUrl}/api/download?jobId={job.jobId}";
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("다운로드 실패: " + www.error);
                    statusMessage = "다운로드 실패: " + www.error;
                    yield break;
                }

                string targetRoot = Path.Combine(Application.dataPath, config.downloadFolderRelative, job.jobId);
                Directory.CreateDirectory(targetRoot);

                string zipPath = Path.Combine(targetRoot, $"job-{job.jobId}-result.zip");
                File.WriteAllBytes(zipPath, www.downloadHandler.data);

                ZipFile.ExtractToDirectory(zipPath, targetRoot, true);

                statusMessage = $"결과 다운로드 완료: Assets/{config.downloadFolderRelative}/{job.jobId}/";
                job.status = "downloaded";
                MotionRetargetingJobs.Save(jobList);

                AssetDatabase.Refresh();
                Repaint();
            }
        }

        // Job 삭제 (서버 폴더 삭제 + 로컬 Job 삭제)
        private IEnumerator DeleteJob(JobRecord job)
        {
            // 서버에 Job 삭제 요청 (DELETE /api/job/{jobId} 형식 예시)
            string url = $"{config.serverBaseUrl}/api/job/{job.jobId}";
            using (UnityWebRequest www = UnityWebRequest.Delete(url))
            {
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("서버 Job 삭제 실패: " + www.error);
                    statusMessage = "서버 Job 삭제 실패: " + www.error;
                    yield break;
                }
            }

            // 서버 삭제 성공 시, 로컬 기록 삭제
            if (jobList != null && jobList.jobs != null)
            {
                jobList.jobs.Clear();
                MotionRetargetingJobs.Save(jobList);
            }

            statusMessage = $"Job {job.jobId} 삭제 완료";
            Repaint();
        }

        // JSON response type

        [System.Serializable]
        private class UploadResponse
        {
            public string jobId;
            public string status;
        }

        [System.Serializable]
        private class StatusResponse
        {
            public string jobId;
            public string filename;
            public string status;
            public string message;
            public int r2etEtaSeconds;
            public int studentEtaSeconds;
        }

        private string FixJson(string raw)
        {
            return raw;
        }

        string FormatEta(int eta)
        {
            if (eta < 0) return "준비 중";
            if (eta == 0) return "완료";
            int min = eta / 60;
            int sec = eta % 60;
            return $"{min:D2}:{sec:D2}";
        }
    }
}
