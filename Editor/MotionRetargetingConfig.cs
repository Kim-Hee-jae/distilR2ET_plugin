// Assets/MotionRetargeting/Editor/MotionRetargetingConfig.cs
using UnityEngine;

namespace MotionRetargeting.Editor
{
    public class MotionRetargetingConfig : ScriptableObject
    {
        public string serverBaseUrl = "https://mariela-multifurcate-semioptimistically.ngrok-free.dev";
        public string downloadFolderRelative = "DistilledModels";
        // => Assets/DistilledModels/<jobId>/ ¿¡ Ç®¸²

        private const string RESOURCE_PATH = "MotionRetargetingConfig";
        private const string ASSET_PATH = "Assets/MotionRetargeting/Editor/Resources/MotionRetargetingConfig.asset";

        public static MotionRetargetingConfig LoadOrCreate()
        {
            var config = Resources.Load<MotionRetargetingConfig>(RESOURCE_PATH);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<MotionRetargetingConfig>();
                System.IO.Directory.CreateDirectory("Assets/MotionRetargeting/Editor/Resources");
                UnityEditor.AssetDatabase.CreateAsset(config, ASSET_PATH);
                UnityEditor.AssetDatabase.SaveAssets();
                UnityEditor.AssetDatabase.Refresh();
            }
            return config;
        }
    }
}
