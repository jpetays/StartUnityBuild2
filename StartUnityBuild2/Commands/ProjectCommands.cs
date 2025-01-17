using System.Diagnostics.CodeAnalysis;
using System.Text;
using Newtonsoft.Json;
using NLog;
using Prg.Util;
using PrgBuild;
using PrgFrame.Util;

namespace StartUnityBuild.Commands;

/// <summary>
/// Commands to prepare to build UNITY player.
/// </summary>
public static class ProjectCommands
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly Encoding Encoding = new UTF8Encoding(false);

    public static void WriteWebGLBuildHistory(BuildSettings settings, bool isSuccess)
    {
        const string outPrefix = "post";
        if (!isSuccess
            || string.IsNullOrWhiteSpace(settings.WebGlBuildHistoryJson)
            || string.IsNullOrWhiteSpace(settings.WebGlBuildHistoryUrl)
            || !settings.HasPostProcessingFor(BuildName.WebGL))
        {
            Form1.AddError("Can not do WebGL build history: conditions are not met");
            return;
        }
        var linkLabel = $"{settings.ProductVersion}";
        var linkHref = settings.WebGlBuildHistoryUrl;
        var releaseNotes = GetReleaseNotesText();
        var buildLogEntryFile = settings.WebGlBuildHistoryJson;
        WriteBuildLogEntry(settings.DeliveryTrack, DateTime.Now, linkLabel, linkHref, releaseNotes, buildLogEntryFile);
        var htmlFile = settings.WebGlBuildHistoryHtml;
        if (File.Exists(htmlFile))
        {
            var lastWriteTime = DateTime.Now;
            var builder = new StringBuilder(File.ReadAllText(htmlFile, Encoding));
            builder.AppendLine().Append($"<!-- {lastWriteTime:G} -->");
            File.WriteAllText(htmlFile, builder.ToString(), Encoding);
            Form1.AddLine(outPrefix, $".touch {lastWriteTime:G} {htmlFile}");
        }
        else
        {
            Form1.AddLine(outPrefix, $"-touch FILE NOT FOUND: {htmlFile}");
        }
        return;

        string GetReleaseNotesText()
        {
            var path = Files.GetReleaseNotesFileName(settings.WorkingDirectory);
            if (File.Exists(path))
            {
                var builder = new StringBuilder();
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        break;
                    }
                    if (builder.Length > 0)
                    {
                        builder.Append("\r\n");
                    }
                    builder.Append(line);
                }
                if (builder.Length > 0)
                {
                    return builder.ToString();
                }
            }
            return $"{settings.ProductName} {settings.ProductVersion} built on {DateTime.Today:yyyy-MM-dd}";
        }
    }

    public static void ModifyProject(BuildSettings settings, Action<bool> finished)
    {
        const string outPrefix = "update";
        Task.Run(() =>
        {
            Form1.AddLine($">{outPrefix}", $"update {settings.ProductName} in {settings.WorkingDirectory}");
            var today = DateTime.Now;
            Form1.AddLine($">{outPrefix}", $"today is {today:yyyy-MM-dd HH:mm}");
            int result;
            try
            {
                UpdateProjectSettings();
                UpdateBuildProperties();
                Form1.AddLine($">{outPrefix}", "Update done");
                result = 0;
            }
            catch (Exception x)
            {
                Form1.AddError($"Update failed: {x.GetType().Name} {x.Message}");
                Logger.Trace(x.StackTrace);
                result = -1;
            }
            var isSuccess = result == 0;
            Form1.AddExitCode(outPrefix, result, isSuccess, showSuccess: true);
            finished(false);
            return;

            void UpdateProjectSettings()
            {
                // Update Project settings: ProductVersion and BundleVersion
                var productVersion = settings.ProductVersion;
                // Always increment bundleVersion.
                var bundleVersion = $"{int.Parse(settings.BundleVersion) + 1}";
                if (SemVer.IsVersionDateWithPatch(productVersion))
                {
                    // Set version as date + patch.
                    productVersion = SemVer.CreateVersionDateWithPatch(productVersion, today, int.Parse(bundleVersion));
                }
                else if (SemVer.IsVersionDate(productVersion))
                {
                    // Set version as date.
                    productVersion = SemVer.CreateVersionDate(productVersion, today);
                }
                else if (SemVer.HasDigits(productVersion, 3))
                {
                    // Synchronize productVersion with bundleVersion in MAJOR.MINOR.PATCH format.
                    productVersion = SemVer.SetDigit(productVersion, 2, int.Parse(bundleVersion));
                }
                var updateCount = ProjectSettings.UpdateProjectSettingsFile(
                    settings.WorkingDirectory, productVersion, bundleVersion);
                switch (updateCount)
                {
                    case -1:
                        Form1.AddError($"Could not update ProjectSettingsFile");
                        break;
                    case 0:
                        Form1.AddLine($".{outPrefix}", $"Did not update ProjectSettingsFile, it is same");
                        break;
                    case 1:
                        if (settings.ProductVersion != productVersion)
                        {
                            Form1.AddLine($".{outPrefix}",
                                $"update ProductVersion {settings.ProductVersion} <- {productVersion}");
                            settings.ProductVersion = productVersion;
                        }
                        Form1.AddLine($".{outPrefix}",
                            $"update BundleVersion {settings.BundleVersion} <- {bundleVersion}");
                        settings.BundleVersion = bundleVersion;
                        break;
                }
            }

            void UpdateBuildProperties()
            {
                // Update BuildProperties.cs
                var assetFolder = Files.GetAssetFolder(settings.WorkingDirectory);
                var buildPropertiesPath = BuildInfoUpdater.BuildPropertiesPath(assetFolder);
                if (buildPropertiesPath.Length < settings.WorkingDirectory.Length)
                {
                    Form1.AddError($"File not found '{buildPropertiesPath}' in {assetFolder}");
                    return;
                }
                var shortName = buildPropertiesPath[(settings.WorkingDirectory.Length + 1)..];
                var isMuteOtherAudioSources = settings.IsMuteOtherAudioSources;
                if (BuildInfoUpdater.UpdateBuildInfo(buildPropertiesPath,
                        today, settings.BundleVersion, isMuteOtherAudioSources))
                {
                    Form1.AddLine($".{outPrefix}", $"update BuildProperties {shortName}");
                }
                else
                {
                    Form1.AddLine($".{outPrefix}", $"Did not update BuildProperties {shortName}, it is same");
                }
            }
        });
    }

    public static bool CopyFilesToProject(BuildSettings settings)
    {
        const string outPrefix = "copy";
        try
        {
            var copyFiles = Files.GetCopyFiles(settings);
            foreach (var tuple in copyFiles)
            {
                var sourceFile = tuple.Item1;
                var targetFile = tuple.Item2;
                Form1.AddLine($">{outPrefix}", $"copy {sourceFile} to {targetFile}");
                File.Copy(sourceFile, targetFile, overwrite: true);
            }
            return true;
        }
        catch (Exception x)
        {
            Form1.AddError($"Copy failed: {x.GetType().Name} {x.Message}");
            Logger.Trace(x.StackTrace);
            return false;
        }
    }

    public static bool CopyFilesToSecretKeys(BuildSettings settings)
    {
        const string outPrefix = "copy";
        try
        {
            var copyFiles = Files.GetCopyFiles(settings);
            foreach (var tuple in copyFiles)
            {
                var sourceFile = tuple.Item1;
                var targetFile = tuple.Item2;
                Form1.AddLine($">{outPrefix}", $"copy {sourceFile} to {targetFile}");
                PathUtil.CreateDirectoryForFile(targetFile);
                File.Copy(sourceFile, targetFile, overwrite: true);
            }
            return true;
        }
        catch (Exception x)
        {
            Form1.AddError($"Copy failed: {x.GetType().Name} {x.Message}");
            Logger.Trace(x.StackTrace);
            return false;
        }
    }

    private static void WriteBuildLogEntry(string deliveryTrack, DateTime date, string linkLabel, string linkHref,
        string releaseNotes, string jsonFilename)
    {
        var entries = Serializer.LoadStateJson<BuildLogEntries>(jsonFilename) ?? new BuildLogEntries();
        entries.List.Insert(0, new BuildLogEntry
        {
            Ver = "2",
            Track = deliveryTrack,
            Date = $"{date:yyyy-MM-dd HH:mm}",
            Label = linkLabel,
            HRef = linkHref,
            Notes = releaseNotes
        });
        Form1.AddLine($".update", $"Build history log entries #{entries.List.Count} in {jsonFilename}");
        if (!File.Exists(jsonFilename))
        {
            var directory = Path.GetDirectoryName(jsonFilename) ?? ".";
            Form1.AddLine($".update", $"Create directory {directory}");
            Directory.CreateDirectory(directory);
        }
        Serializer.SaveStateJson(entries, jsonFilename, Formatting.Indented);
    }

    /// <summary>
    /// JSON serialized build log entry.<br />
    /// This can be used for example to create table of content for all (recent) builds.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    private class BuildLogEntry
    {
        public string Ver { get; set; } = "";
        public string Track { get; set; } = "";
        public string Date { get; set; } = "";
        public string Label { get; set; } = "";
        public string HRef { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    /// <summary>
    /// JSON serialized container for <c>BuildLogEntry</c> instances.
    /// </summary>
    [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
    private class BuildLogEntries
    {
        public List<BuildLogEntry> List = [];
    }
}
