using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

#if UNITY_6000_0_OR_NEWER
namespace CrazyGames
{
    public class Builder
    {
        private WebGLCompressionFormat _initialCompressionFormat;
        private WebGLExceptionSupport _initialExceptionSupport;
        private WebGLDebugSymbolMode _initialDebugSymbolMode;
        private bool _initialDataCaching;
        private bool _initialNameFilesAsHashes;
        private bool _initialStripEngineCode;
        private UnityEditor.WebGL.WasmCodeOptimization _initialCodeOptimization;
        private Il2CppCodeGeneration _initialIl2CppCodeGeneration;
        private bool _initialShowSplashScreen;
        private bool _initialShowUnityLogo;
        private bool _initialWasm2023;
        private bool _initialWebAssemblyTable;
        private bool _initialWebAssemblyBigInt;
        private WebGLTextureSubtarget _initialWebGLTextureSubtarget;
        private string _initialWebGLTemplate;

        public const string SUMMARY_FILE_NAME = "CGReleaseBuildReportSummary-v1.json";
        public const string INTERMEDIATE_BUILD_SUFFIX = "_cg_intermediate_build";

        // files from intermediate builds are kept here until the main build is done, outside of any build output folder
        private static readonly string INTERMEDIATE_FILES_PATH = Path.Combine("Temp", "CrazyGamesIntermediateFiles");

        // the template generates files.json in the build output, which is used to detect the generated files
        private const string WEBGL_TEMPLATE = "PROJECT:CrazyGames";
        private const string WEBGL_TEMPLATE_PATH = "Assets/WebGLTemplates/CrazyGames";

        public void DoDevelopmentBuild()
        {
            Build(BuildVariant.Development, null);
        }

        public void DoReleaseBuild(AdditionalBuildOptions additionalOptions)
        {
            Build(BuildVariant.Release, additionalOptions);
        }

        private void Build(BuildVariant selectedVariant, AdditionalBuildOptions additionalOptions)
        {
            var startTime = System.DateTime.Now;

            var scenePaths = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();

            if (scenePaths.Length == 0)
            {
                Debug.LogError("There aren't any enabled scenes in the build settings. Please add at least one scene.");
                return;
            }

            if (!Directory.Exists(WEBGL_TEMPLATE_PATH))
            {
                Debug.LogError($"CrazyGames WebGL template not found at {WEBGL_TEMPLATE_PATH}. Please reimport the CrazyGames SDK.");
                return;
            }

            var buildPath = GetBuildPath(selectedVariant);

            var buildOptions = new BuildPlayerOptions
            {
                scenes = scenePaths,
                locationPathName = buildPath,
                target = BuildTarget.WebGL,
                options = GetBuildOptions(selectedVariant),
            };
            if (Directory.Exists(buildOptions.locationPathName))
            {
                Directory.Delete(buildOptions.locationPathName, true);
            }

            StoreInitialSettings();
            var buildOptimizations = ConfigureWebGLSettings(selectedVariant);
            var optimizationsDebugString = string.Join(
                "\n",
                buildOptimizations.Select(o => $"{o.optimization}: {o.initialValue} => {o.buildValue}")
            );

            try
            {
                EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.DXT;

                // intermediate builds are done before the main build, because Unity deletes the Build folder of the previous build
                // when building to a different path, so the main build output would be deleted by the intermediate builds
                var intermediateFiles = new List<IntermediateFile>();
                if (selectedVariant == BuildVariant.Release)
                {
                    if (Directory.Exists(INTERMEDIATE_FILES_PATH))
                    {
                        Directory.Delete(INTERMEDIATE_FILES_PATH, true);
                    }
                    Directory.CreateDirectory(INTERMEDIATE_FILES_PATH);
                    intermediateFiles.AddRange(DoASTCBuild(buildOptions));
                    if (additionalOptions.supportsMobile)
                    {
                        intermediateFiles.AddRange(DoLimitedMemoryBuilds(buildOptions));
                    }
                }

                var report = BuildPipeline.BuildPlayer(buildOptions);

                if (report.summary.result == BuildResult.Succeeded)
                {
                    Debug.Log(
                        $"Build succeeded in {(DateTime.Now - startTime).TotalMinutes:F1} minutes\nWith these settings:\n{optimizationsDebugString}"
                    );
                    if (selectedVariant == BuildVariant.Release)
                    {
                        foreach (var file in intermediateFiles)
                        {
                            var to = Path.Combine(report.summary.outputPath, file.path);
                            Directory.CreateDirectory(Path.GetDirectoryName(to));
                            File.Copy(Path.Combine(INTERMEDIATE_FILES_PATH, file.path), to, true);
                        }
                        var reportGenerator = new BuildReportGenerator();
                        reportGenerator.GenerateReport(
                            report,
                            selectedVariant,
                            additionalOptions,
                            buildPath,
                            startTime,
                            buildOptimizations,
                            intermediateFiles
                        );
                    }
                }
                else
                {
                    Debug.LogError(
                        $"Build failed! {(DateTime.Now - startTime).TotalMinutes:F1} minutes\nWith these settings:\n{optimizationsDebugString}"
                    );
                    foreach (var step in report.steps)
                    {
                        foreach (var message in step.messages.Where(m => m.type == LogType.Error))
                        {
                            Debug.LogError($"Build Error: {message.content}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Build failed with exception: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                if (Directory.Exists(INTERMEDIATE_FILES_PATH))
                {
                    Directory.Delete(INTERMEDIATE_FILES_PATH, true);
                }
                RestoreWebGLSettings();
                AssetDatabase.SaveAssets(); // restore changed PlayerSettings
            }
        }

        /// <summary>
        /// Does a second build with ASTC compression format and copies its data files to the intermediate files folder.
        /// Without progressive asset loading, the single data file is prefixed with "astc_", ex Build/astc_cdadfac5d5a5d2c11639cfca63e6d4f8.data.br
        /// With progressive asset loading, the data files can't be renamed, as the Unity loader uses their path to place them in the virtual file system,
        /// so they are placed in an "astc" folder that mirrors the build output folder, ex astc/level0.br, astc/Resources/unity default resources.br
        /// </summary>
        private List<IntermediateFile> DoASTCBuild(BuildPlayerOptions originalBuildOptions)
        {
            var astcFiles = new List<IntermediateFile>();
            var initialWebGLBuildSubtarget = EditorUserBuildSettings.webGLBuildSubtarget;
            EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.ASTC;
            var astcBuildOptions = originalBuildOptions;
            astcBuildOptions.locationPathName += INTERMEDIATE_BUILD_SUFFIX;
            if (Directory.Exists(astcBuildOptions.locationPathName))
            {
                Directory.Delete(astcBuildOptions.locationPathName, true);
            }
            // reset options, don't need autorun, and don't clean cache for quicker build
            astcBuildOptions.options = BuildOptions.None;
            var astcReport = BuildPipeline.BuildPlayer(astcBuildOptions);
            if (astcReport.summary.result == BuildResult.Succeeded)
            {
                var outputPath = astcReport.summary.outputPath;
                var buildFiles = BuildFiles.Read(outputPath);
                if (buildFiles.IsProgressive)
                {
                    foreach (var file in buildFiles.primaryDataFiles)
                    {
                        astcFiles.Add(CopyToIntermediateFiles(outputPath, file, Path.Combine("astc", file), "data_astc_primary"));
                    }
                    foreach (var file in buildFiles.secondaryDataFiles)
                    {
                        astcFiles.Add(CopyToIntermediateFiles(outputPath, file, Path.Combine("astc", file), "data_astc_secondary"));
                    }
                }
                else
                {
                    var astcDataFile = Path.Combine("Build", "astc_" + Path.GetFileName(buildFiles.dataFile));
                    astcFiles.Add(CopyToIntermediateFiles(outputPath, buildFiles.dataFile, astcDataFile, "data_astc"));
                }
            }
            if (Directory.Exists(astcBuildOptions.locationPathName))
            {
                Directory.Delete(astcBuildOptions.locationPathName, true);
            }
            EditorUserBuildSettings.webGLBuildSubtarget = initialWebGLBuildSubtarget;
            return astcFiles;
        }

        /// <summary>
        /// Does 2 separate builds with 512MB and 1024MB max memory size.
        /// Copies the wasm files to the intermediate files folder, prefixed with "max_{memory}mb_", ex Build/max_512mb_cdadfac5d5a5d2c11639cfca63e6d4f8.wasm.br
        /// </summary>
        private List<IntermediateFile> DoLimitedMemoryBuilds(BuildPlayerOptions originalBuildOptions)
        {
            var files = new List<IntermediateFile>();
            var initialMaxMemorySize = PlayerSettings.WebGL.maximumMemorySize;
            var max512mb = DoBuildWithMaxMemorySize(512, originalBuildOptions);
            if (max512mb != null)
            {
                files.Add(max512mb);
            }
            var max1024mb = DoBuildWithMaxMemorySize(1024, originalBuildOptions);
            if (max1024mb != null)
            {
                files.Add(max1024mb);
            }
            PlayerSettings.WebGL.maximumMemorySize = initialMaxMemorySize;
            return files;
        }

        private IntermediateFile DoBuildWithMaxMemorySize(int memorySize, BuildPlayerOptions originalBuildOptions)
        {
            IntermediateFile wasmFile = null;
            PlayerSettings.WebGL.maximumMemorySize = memorySize;
            var buildOptions = originalBuildOptions;
            buildOptions.locationPathName += INTERMEDIATE_BUILD_SUFFIX;
            if (Directory.Exists(buildOptions.locationPathName))
            {
                Directory.Delete(buildOptions.locationPathName, true);
            }
            // reset options, don't need autorun, and don't clean cache for quicker build
            buildOptions.options = BuildOptions.None;
            var report = BuildPipeline.BuildPlayer(buildOptions);
            if (report.summary.result == BuildResult.Succeeded)
            {
                var buildFiles = BuildFiles.Read(report.summary.outputPath);
                var prefixedFile = Path.Combine("Build", "max_" + memorySize + "mb_" + Path.GetFileName(buildFiles.codeFile));
                wasmFile = CopyToIntermediateFiles(report.summary.outputPath, buildFiles.codeFile, prefixedFile, $"wasm_max_{memorySize}mb");
            }
            if (Directory.Exists(buildOptions.locationPathName))
            {
                Directory.Delete(buildOptions.locationPathName, true);
            }
            return wasmFile;
        }

        /// <param name="from">Path relative to the intermediate build output folder</param>
        /// <param name="to">Path relative to the intermediate files folder, and later to the main build output folder</param>
        private IntermediateFile CopyToIntermediateFiles(string buildOutputPath, string from, string to, string type)
        {
            var destination = Path.Combine(INTERMEDIATE_FILES_PATH, to);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(Path.Combine(buildOutputPath, from), destination, true);
            return new IntermediateFile { path = to, type = type };
        }

        private BuildOptions GetBuildOptions(BuildVariant variant)
        {
            switch (variant)
            {
                case BuildVariant.Development:
                    return BuildOptions.Development | BuildOptions.AutoRunPlayer; // to start the game in browser;
                case BuildVariant.Release:
                    return BuildOptions.AutoRunPlayer // to start the game in browser
#if !UNITY_6000_4_OR_NEWER
                        // to avoid sometimes packedAssets being empty
                        // however Unity 6.4 has an issue and the build fails if CleanBuildCache is used (internal build error, backend has requested build program 6 times)
                        // but CleanBuildCache doesn't seems necessary for 6.4 anyways, as packedAssets was always populated there
                        | BuildOptions.CleanBuildCache
#endif
                    ;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(variant), variant, "Unknown build variant");
            }
        }

        private void StoreInitialSettings()
        {
            _initialCompressionFormat = PlayerSettings.WebGL.compressionFormat;
            _initialExceptionSupport = PlayerSettings.WebGL.exceptionSupport;
            _initialDebugSymbolMode = PlayerSettings.WebGL.debugSymbolMode;
            _initialDataCaching = PlayerSettings.WebGL.dataCaching;
            _initialNameFilesAsHashes = PlayerSettings.WebGL.nameFilesAsHashes;
            _initialStripEngineCode = PlayerSettings.stripEngineCode;
            _initialIl2CppCodeGeneration = PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.WebGL);
            _initialCodeOptimization = UnityEditor.WebGL.UserBuildSettings.codeOptimization;
            _initialShowSplashScreen = PlayerSettings.SplashScreen.show;
            _initialShowUnityLogo = PlayerSettings.SplashScreen.showUnityLogo;
            _initialWebAssemblyBigInt = PlayerSettings.WebGL.webAssemblyBigInt;
            _initialWebAssemblyTable = PlayerSettings.WebGL.webAssemblyTable;
            _initialWasm2023 = PlayerSettings.WebGL.wasm2023;
            _initialWebGLTextureSubtarget = EditorUserBuildSettings.webGLBuildSubtarget;
            _initialWebGLTemplate = PlayerSettings.WebGL.template;
        }

        private List<BuildOptimization> ConfigureWebGLSettings(BuildVariant variant)
        {
            var optimizations = new List<BuildOptimization>();

            try
            {
                var webglTarget = NamedBuildTarget.WebGL;

                var buildIl2CppCodeGeneration = Il2CppCodeGeneration.OptimizeSize;
                optimizations.Add(
                    new BuildOptimization(
                        "il2CppCodeGeneration",
                        _initialIl2CppCodeGeneration.ToString(),
                        buildIl2CppCodeGeneration.ToString()
                    )
                );
                PlayerSettings.SetIl2CppCodeGeneration(webglTarget, buildIl2CppCodeGeneration);

                optimizations.Add(new BuildOptimization("showSplashScreen", _initialShowSplashScreen.ToString(), "False"));
                PlayerSettings.SplashScreen.show = false;

                optimizations.Add(new BuildOptimization("showUnityLogo", _initialShowUnityLogo.ToString(), "False"));
                PlayerSettings.SplashScreen.showUnityLogo = false;

                optimizations.Add(new BuildOptimization("template", _initialWebGLTemplate, WEBGL_TEMPLATE));
                PlayerSettings.WebGL.template = WEBGL_TEMPLATE;

                switch (variant)
                {
                    case BuildVariant.Development:
                        ApplyDevelopmentSettings(optimizations);
                        break;
                    case BuildVariant.Release:
                        ApplyReleaseSettings(optimizations);
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not configure WebGL: {e.Message}");
            }

            return optimizations;
        }

        private void ApplyDevelopmentSettings(List<BuildOptimization> optimizations)
        {
            var buildCompression = WebGLCompressionFormat.Disabled;
            optimizations.Add(
                new BuildOptimization("compressionFormat", _initialCompressionFormat.ToString(), buildCompression.ToString())
            );
            PlayerSettings.WebGL.compressionFormat = buildCompression;

            var buildExceptionSupport = WebGLExceptionSupport.FullWithStacktrace;
            optimizations.Add(
                new BuildOptimization("exceptionSupport", _initialExceptionSupport.ToString(), buildExceptionSupport.ToString())
            );
            PlayerSettings.WebGL.exceptionSupport = buildExceptionSupport;

            var buildDebugSymbols = WebGLDebugSymbolMode.External;
            optimizations.Add(new BuildOptimization("debugSymbolMode", _initialDebugSymbolMode.ToString(), buildDebugSymbols.ToString()));
            PlayerSettings.WebGL.debugSymbolMode = buildDebugSymbols;

            optimizations.Add(new BuildOptimization("dataCaching", _initialDataCaching.ToString(), "False"));
            PlayerSettings.WebGL.dataCaching = false;

            optimizations.Add(new BuildOptimization("nameFilesAsHashes", _initialNameFilesAsHashes.ToString(), "False"));
            PlayerSettings.WebGL.nameFilesAsHashes = false;

            optimizations.Add(new BuildOptimization("stripEngineCode", _initialStripEngineCode.ToString(), "False"));
            PlayerSettings.stripEngineCode = false;

            var buildCodeOptimization = UnityEditor.WebGL.WasmCodeOptimization.BuildTimes;
            optimizations.Add(
                new BuildOptimization("codeOptimization", _initialCodeOptimization.ToString(), buildCodeOptimization.ToString())
            );
            UnityEditor.WebGL.UserBuildSettings.codeOptimization = buildCodeOptimization;
        }

        private void ApplyReleaseSettings(List<BuildOptimization> optimizations)
        {
            var buildCompression = WebGLCompressionFormat.Brotli;
            optimizations.Add(
                new BuildOptimization("compressionFormat", _initialCompressionFormat.ToString(), buildCompression.ToString())
            );
            PlayerSettings.WebGL.compressionFormat = buildCompression;

            if (_initialExceptionSupport == WebGLExceptionSupport.None)
            {
                // if exception support is set to None, which is even better than ExplicitlyThrownExceptionsOnly, we keep it as is
                optimizations.Add(
                    new BuildOptimization("exceptionSupport", _initialExceptionSupport.ToString(), _initialExceptionSupport.ToString())
                );
            }
            else
            {
                var buildExceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
                optimizations.Add(
                    new BuildOptimization("exceptionSupport", _initialExceptionSupport.ToString(), buildExceptionSupport.ToString())
                );
                PlayerSettings.WebGL.exceptionSupport = buildExceptionSupport;
            }

            var buildDebugSymbols = WebGLDebugSymbolMode.Off;
            optimizations.Add(new BuildOptimization("debugSymbolMode", _initialDebugSymbolMode.ToString(), buildDebugSymbols.ToString()));
            PlayerSettings.WebGL.debugSymbolMode = buildDebugSymbols;

            optimizations.Add(new BuildOptimization("dataCaching", _initialDataCaching.ToString(), "True"));
            PlayerSettings.WebGL.dataCaching = true;

            var buildNameFilesAsHashes = true;
#if UNITY_6000_6_OR_NEWER
            // Unity doesn't support naming files as hashes when progressive asset loading is enabled
            if (PlayerSettings.WebGL.progressiveAssetLoading)
            {
                buildNameFilesAsHashes = false;
            }
#endif
            optimizations.Add(
                new BuildOptimization("nameFilesAsHashes", _initialNameFilesAsHashes.ToString(), buildNameFilesAsHashes.ToString())
            );
            PlayerSettings.WebGL.nameFilesAsHashes = buildNameFilesAsHashes;

            optimizations.Add(new BuildOptimization("stripEngineCode", _initialStripEngineCode.ToString(), "True"));
            PlayerSettings.stripEngineCode = true;

            var buildCodeOptimization = UnityEditor.WebGL.WasmCodeOptimization.DiskSizeLTO;
            optimizations.Add(
                new BuildOptimization("codeOptimization", _initialCodeOptimization.ToString(), buildCodeOptimization.ToString())
            );
            UnityEditor.WebGL.UserBuildSettings.codeOptimization = buildCodeOptimization;

            optimizations.Add(new BuildOptimization("webAssemblyBigInt", _initialWebAssemblyBigInt.ToString(), "True"));
            PlayerSettings.WebGL.webAssemblyBigInt = true;

            optimizations.Add(new BuildOptimization("webAssemblyTable", _initialWebAssemblyTable.ToString(), "True"));
            PlayerSettings.WebGL.webAssemblyTable = true;

            optimizations.Add(new BuildOptimization("wasm2023", _initialWasm2023.ToString(), "True"));
            PlayerSettings.WebGL.wasm2023 = true;
        }

        private void RestoreWebGLSettings()
        {
            try
            {
                PlayerSettings.WebGL.compressionFormat = _initialCompressionFormat;
                PlayerSettings.WebGL.exceptionSupport = _initialExceptionSupport;
                PlayerSettings.WebGL.debugSymbolMode = _initialDebugSymbolMode;
                PlayerSettings.WebGL.dataCaching = _initialDataCaching;
                PlayerSettings.WebGL.nameFilesAsHashes = _initialNameFilesAsHashes;
                PlayerSettings.stripEngineCode = _initialStripEngineCode;
                PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, _initialIl2CppCodeGeneration);
                UnityEditor.WebGL.UserBuildSettings.codeOptimization = _initialCodeOptimization;
                PlayerSettings.SplashScreen.show = _initialShowSplashScreen;
                PlayerSettings.SplashScreen.showUnityLogo = _initialShowUnityLogo;
                PlayerSettings.WebGL.webAssemblyBigInt = _initialWebAssemblyBigInt;
                PlayerSettings.WebGL.webAssemblyTable = _initialWebAssemblyTable;
                PlayerSettings.WebGL.wasm2023 = _initialWasm2023;
                EditorUserBuildSettings.webGLBuildSubtarget = _initialWebGLTextureSubtarget;
                PlayerSettings.WebGL.template = _initialWebGLTemplate;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not restore WebGL settings: {e.Message}");
            }
        }

        private string GetBuildPath(BuildVariant buildVariant)
        {
            var folder = buildVariant == BuildVariant.Development ? "CrazyGamesDevelopment" : "CrazyGamesRelease";
            return Path.Combine("Builds", folder);
        }
    }

    public enum BuildVariant
    {
        Development,
        Release,
    }

    /// <summary>
    /// A file generated by an intermediate build (ASTC or limited memory), that is copied to the main build output.
    /// </summary>
    public class IntermediateFile
    {
        /// <summary>
        /// Path relative to the build output folder, ex Build/astc_cdadfac5d5a5d2c11639cfca63e6d4f8.data.br
        /// </summary>
        public string path;

        /// <summary>
        /// Type of the file in the build report, ex data_astc, wasm_max_512mb
        /// </summary>
        public string type;
    }

    [System.Serializable]
    public class AdditionalBuildOptions
    {
        public bool supportsMobile;
    }
}
#endif
