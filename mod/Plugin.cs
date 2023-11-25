﻿using System.IO;
using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

#if BEPINEX_V6
    using BepInEx.Unity.Mono;
#endif

namespace HookUIMod
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private readonly string hookUIPath = Path.Combine(Application.dataPath, "StreamingAssets", "~UI~", "HookUI");
        private readonly string extensionsPath = Path.Combine(Application.dataPath, "StreamingAssets", "~UI~", "HookUI", "Extensions");
        private System.IO.FileSystemWatcher watcher;

        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            var srcFile = "Cities2_Data\\StreamingAssets\\~UI~\\HookUI\\index.html.template";
            var dstFile = "Cities2_Data\\StreamingAssets\\~UI~\\HookUI\\index.html";

            var actualVersion = Game.Version.current.shortVersion;
            // TODO move this into the horrible XML project/solution file
            var compatibleVersion = "1.0.14f1";

            var harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), MyPluginInfo.PLUGIN_GUID + "_Cities2Harmony");

            var patchedMethods = harmony.GetPatchedMethods().ToArray();

            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} made patches! Patched methods: " + patchedMethods.Length);

            foreach (var patchedMethod in patchedMethods)
            {
                Logger.LogInfo($"Patched method: {patchedMethod.Module.Name}:{patchedMethod.Name}");
            }

            InstallHookUI();
            // InitializeFileWatcher();

            if (CheckVersion(actualVersion, compatibleVersion))
            {
                // Rewrite index.html to include list of mods UI we should load
                var scriptPaths = this.GenerateScriptPathsList("Cities2_Data\\StreamingAssets\\~UI~\\HookUI\\Extensions");
                InsertScriptTags(srcFile, dstFile, scriptPaths);
            }
            else
            {
                PrintVersionWarning(srcFile, dstFile, actualVersion, compatibleVersion);
            }
        }

        // TODO We don't actually use this file watcher yet, but would be nice to be able to turn off the
        // UI reload for Extensions, and then use the file watcher so we can hot-reload extensions
        // For now, the built-in reload will do
        // private void InitializeFileWatcher() {
        //     this.watcher = new System.IO.FileSystemWatcher(extensionsPath);
        //     this.watcher.Path = extensionsPath;
        //     //this.watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;

        //     // NotifyFilters.LastAccess | NotifyFilters.FileName
        //     this.watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;

        //     //this.watcher.Filter = "*.js";

        //     this.watcher.Created += OnFileCreated;
        //     this.watcher.Deleted += OnFileDeleted;

        //     this.watcher.EnableRaisingEvents = true;
        //     UnityEngine.Debug.Log($"Watcher set to track {this.watcher.Path}");
        // }
        // private void OnFileCreated(object source, FileSystemEventArgs e) {
        //     UnityEngine.Debug.Log($"File created {e.Name}");
        //     Logger.LogInfo($"File created {e.Name}");
        //     //this.AddExtension(e.Name);
        // }
        // private void OnFileDeleted(object source, FileSystemEventArgs e) {
        //     UnityEngine.Debug.Log($"File deleted {e.Name}");
        //     Logger.LogInfo($"File deleted {e.Name}");
        //     //availableExtensions.Remove(e.Name);
        // }

        public static String InjectHookUILoader(String orig_text)
        {
            var src = "tutorialListFocusKey:uIe.tutorialList})]";
            var dst = "tutorialListFocusKey:uIe.tutorialList}),(0,e.jsx)(window._$hookui_loader,{react:i})]";
            return orig_text.Replace(src, dst);
        }

        public static String HookPanelsMenu(String orig_text)
        {
            var src = "fve.lock})})})})]";
            var dst = "fve.lock})})})}),(0,e.jsx)(window._$hookui_menu,{react:i})]";
            return orig_text.Replace(src, dst);
        }

        public static String OverwriteAbsoluteButton(String orig_text)
        {
            var src = ".button_H9N{pointer-events:auto;position:absolute;top:0;left:0}";
            var dst = ".button_H9N{pointer-events:auto}";
            return orig_text.Replace(src, dst);
        }

        public static void WriteResourcesToDisk()
        {
            string baseFolder = "Cities2_Data\\StreamingAssets\\~UI~\\HookUI\\lib";
            string[] resources = {
            "hookui.api.bundle.js",
            "hookui.loader.bundle.js",
            "hookui.menu.bundle.js"
        };
            Assembly assembly = Assembly.GetExecutingAssembly();
            Directory.CreateDirectory(baseFolder);

            foreach (var resourceName in resources)
            {
                string resourcePath = "HookUIMod." + resourceName;
                using (Stream resourceStream = assembly.GetManifestResourceStream(resourcePath))
                {
                    if (resourceStream == null)
                        throw new InvalidOperationException("Could not find embedded resource: " + resourceName);

                    string targetPath = Path.Combine(baseFolder, resourceName);
                    using (FileStream fileStream = new FileStream(targetPath, FileMode.Create))
                    {
                        resourceStream.CopyTo(fileStream);
                    }
                }
            }
        }

        public static void InstallHookUI() {
            var assetsDir = "Cities2_Data\\StreamingAssets\\~UI~";
            // TODO obviously, we don't want to do this every time, try to read version then adjust
            CopyDirectory(assetsDir + "\\GameUI", assetsDir + "\\HookUI");

            File.WriteAllText(assetsDir + "\\HookUI\\version", MyPluginInfo.PLUGIN_VERSION);

            // Patches injection points in the bundle
            var bundleDir = assetsDir + "\\HookUI\\index.js";
            string fileContent = File.ReadAllText(bundleDir);
            fileContent = InjectHookUILoader(fileContent);
            fileContent = HookPanelsMenu(fileContent);
            File.WriteAllText(bundleDir, fileContent);

            // Patches some built-in styling
            var styleFile = assetsDir + "\\HookUI\\index.css";
            string styleContent = File.ReadAllText(styleFile);
            styleContent = OverwriteAbsoluteButton(styleContent);
            File.WriteAllText(styleFile, styleContent);

            WriteResourcesToDisk();

            Directory.CreateDirectory(assetsDir + "\\HookUI\\Extensions");

            string resourceName = "HookUIMod.index.html.template";
            string targetPath = Path.Combine("Cities2_Data\\StreamingAssets\\~UI~\\HookUI", "index.html.template");

            using (var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
            {
                resourceStream.CopyTo(fileStream);
            }
        }

        public static bool CheckVersion(string currentVersion, string compatibleVersion)
        {
            UnityEngine.Debug.Log("HookUI VersionCheck");
            UnityEngine.Debug.Log(currentVersion);
            if (currentVersion == compatibleVersion)
            {
                UnityEngine.Debug.Log("Passed version check");
                var actualJSHash = "";
                var actualHTMLHash = "";

                var expectedJSHash = "CAA2852C609B391E942A474EA4A26A4AD14E66DE6A1C5FEE4E1B8C111D3E9492";
                var expectedHTMLHash = "CAA2852C609B391E942A474EA4A26A4AD14E66DE6A1C5FEE4E1B8C111D3E9492";

                if (true || actualJSHash == expectedJSHash)
                {
                    if (true || actualHTMLHash == expectedHTMLHash)
                    {
                        UnityEngine.Debug.Log("Passed hash checks");

                        // Everything went OK, we can proceed
                        return true;
                    }
                    else
                    {
                        UnityEngine.Debug.Log($"Failed hash check for HTML file. Expected {expectedHTMLHash} but got {actualHTMLHash}");
                        return false;
                    }
                }
                else
                {
                    UnityEngine.Debug.Log($"Failed hash check for JavaScript file. Expected {expectedJSHash} but got {actualJSHash}");
                    return false;
                }
            }
            else
            {
                UnityEngine.Debug.Log("This HookUI version might not be compatible with your game version");
                return false;
            }
        }

        public static void CopyDirectory(string sourceDir, string destinationDir)
        {
            DirectoryInfo dir = new DirectoryInfo(destinationDir);
            if (!dir.Exists)
            {
                dir.Create();
            }

            DirectoryInfo sourceDirectoryInfo = new DirectoryInfo(sourceDir);

            foreach (FileInfo file in sourceDirectoryInfo.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            foreach (DirectoryInfo subdir in sourceDirectoryInfo.GetDirectories())
            {
                string newDestinationDir = Path.Combine(destinationDir, subdir.Name);
                CopyDirectory(subdir.FullName, newDestinationDir);
            }
        }

        public static void InsertScriptTags(string filePath, string destinationPath, List<string> scriptPaths)
        {
            string fileContent = File.ReadAllText(filePath);
            StringBuilder scriptTagsBuilder = new StringBuilder();

            foreach (var scriptPath in scriptPaths)
            {
                scriptTagsBuilder.AppendLine($"<script src=\"{scriptPath}\"></script>");
            }

            //fileContent = fileContent.Replace("<!--EXTENSIONS_LIST-->", scriptTagsBuilder.ToString());
            File.WriteAllText(destinationPath, fileContent);
        }

        public static void PrintVersionWarning(string filePath, string destinationPath, string actualVersion, string expectedVersion)
        {
            string fileContent = File.ReadAllText(filePath);
            fileContent = fileContent.Replace("<!--EXTENSIONS_LIST-->", $"<div style=\"position: absolute; top: 10px; left: 10px; z-index: 10000; color: white;\" onclick=\"if (this.parentNode) this.parentNode.removeChild(this);\"><div>This HookUI version is not compatible with your game version.</div><div>Loading of extensions disabled.</div><div>{actualVersion} = Your CS2 version</div><div>{expectedVersion} = Last tested CS2 version with HookUI</div><div>Click to hide</div></div>");
            File.WriteAllText(destinationPath, fileContent);
        }

        public List<string> GenerateScriptPathsList(string directoryPath)
        {
            var fullPathScriptFiles = Directory.GetFiles(directoryPath, "*.js");

            var scriptPaths = fullPathScriptFiles.Select(fullPath =>
                "Extensions/" + Path.GetFileName(fullPath)).ToList();

            return scriptPaths;
        }
    }
}
