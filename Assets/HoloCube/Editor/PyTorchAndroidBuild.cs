using System;
using System.IO;
using UnityEditor.Android;

namespace HoloCube.Editor
{
    // Build-time packaging only. All Python code, dependencies, and the original
    // checkpoint are embedded in the APK; the headset needs no Python install.
    public sealed class PyTorchAndroidBuild : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1000;
        public static string PythonPath => Environment.GetEnvironmentVariable("HOLOCUBE_PYTHON") ??
            Path.GetFullPath(".build-tools/pt/bin/python");

        public void OnPostGenerateGradleAndroidProject(string libraryPath)
        {
            if (!File.Exists(PythonPath)) throw new Exception("Run bash Tools/setup-python.sh before building the .pt app.");
            string root = Directory.GetParent(libraryPath).FullName;
            string rootBuild = Path.Combine(root, "build.gradle");
            string rootText = File.ReadAllText(rootBuild);
            if (!rootText.Contains("com.chaquo.python"))
                File.WriteAllText(rootBuild, rootText.Replace("plugins {", "plugins {\n    id 'com.chaquo.python' version '16.1.0' apply false"));

            string build = Path.Combine(libraryPath, "build.gradle");
            string text = File.ReadAllText(build);
            if (!text.Contains("apply plugin: 'com.chaquo.python'"))
            {
                string python = PythonPath.Replace("\\", "/").Replace("'", "\\'");
                text += "\napply plugin: 'com.chaquo.python'\nchaquopy {\n    defaultConfig {\n" +
                    "        version '3.8'\n        buildPython '" + python + "'\n" +
                    "        pip {\n            install 'torch==1.8.1'\n            install 'numpy==1.19.5'\n        }\n" +
                    "    }\n}\n";
                File.WriteAllText(build, text);
            }

            string pythonDir = Path.Combine(libraryPath, "src/main/python");
            Directory.CreateDirectory(pythonDir);
            foreach (string file in Directory.GetFiles("Assets/HoloCube/YOLO Inference/Python", "*.py"))
                File.Copy(file, Path.Combine(pythonDir, Path.GetFileName(file)), true);
            File.Copy(QuestYoloSceneSetup.ModelFolder + "yolo26n.pt", Path.Combine(pythonDir, "yolo26n.pt"), true);
            File.Copy(QuestYoloSceneSetup.ModelFolder + "LICENSE.txt", Path.Combine(pythonDir, "YOLO-LICENSE.txt"), true);

            File.Copy("Tools/Fixtures/YOLO26/input.f32", Path.Combine(pythonDir, "reference.f32"), true);
            File.Copy("Tools/Fixtures/YOLO26/expected.json", Path.Combine(pythonDir, "reference.json"), true);

            string javaDir = Path.Combine(libraryPath, "src/main/java/com/holocube/pytorch");
            Directory.CreateDirectory(javaDir);
            File.Copy("Assets/HoloCube/YOLO Inference/Android/PyTorchWorker.java", Path.Combine(javaDir, "PyTorchWorker.java"), true);
        }
    }
}
