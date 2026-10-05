using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HoloCube.Editor
{
    // Unity menu and command-line entry points. This file never runs on the headset.
    public static class QuestYoloBuild
    {
        [MenuItem("HoloCube/Create standalone Quest scene")]
        public static void CreateScene() => QuestYoloSceneSetup.Create();

        [MenuItem("HoloCube/Validate standalone pipeline")]
        public static void Validate() => QuestYoloValidation.Run();

        public static void ValidateGPU() => QuestYoloGraphicsValidation.Run();

        [MenuItem("HoloCube/Build standalone Quest APK")]
        public static void Build()
        {
            CreateScene();
            Validate();
            Directory.CreateDirectory("Builds");
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { QuestYoloSceneSetup.ScenePath },
                locationPathName = "Builds/HoloCube.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Quest APK build failed: " + report.summary.result);

            Debug.Log("HOLOCUBE_APK_READY: " + Path.GetFullPath("Builds/HoloCube.apk"));
        }
    }
}
