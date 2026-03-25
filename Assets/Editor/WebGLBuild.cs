using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebGLBuild
{
    private const string DefaultOutputPath = "Builds/Nyctophobia 0.1.1";

    [MenuItem("Tools/Build/Build WebGL")]
    public static void BuildWebGLMenu()
    {
        Build(DefaultOutputPath);
    }

    public static void BuildWebGLBatch()
    {
        Build(DefaultOutputPath);
    }

    private static void Build(string outputPath)
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes found in Build Settings.");

        string fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(fullOutputPath);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = fullOutputPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"WebGL build failed with result {report.summary.result}.");

        Debug.Log($"WebGL build succeeded at {fullOutputPath}");
    }
}
