using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Media;
using System.Text;
using Prg.Util;
using PrgBuild;
using StartUnityBuild.Commands;

namespace StartUnityBuild;

[SuppressMessage("ReSharper", "LocalizableElement")]
public partial class Form1 : Form
{
    private const int WatchMinutes = 5;
    private readonly string _baseTitle;

    private static readonly char[] Separators = ['\r', '\n'];

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private static Form1 _instance;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private BuildSettings _settings;

    private bool _isCommandExecuting;
    private string _commandLabel = "";
    private int _commandWatchMinutes;
    private DateTime _commandStartTime;

    private readonly ListViewOut _listView;

    public Form1()
    {
        var appVersion = $"{Application.ProductVersion.Split('+', StringSplitOptions.TrimEntries)[0]}";
        _baseTitle = $"{(Args.Instance.IsTesting ? "*TEST* " : "")}{Application.ProductName} {appVersion} UNITY";
        _instance = this;
        _settings = new BuildSettings(Directory.GetCurrentDirectory());
        InitializeComponent();
        _listView = new ListViewOut(this, listView2, listView1);
        KeyPreview = true;
        KeyDown += OnKeyDown;

        label1.Text = "";
        timer1.Interval = 1000;

        SetupFileMenuCommands();
        SetupBuildMenuCommands();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Text = _baseTitle;
        if (Files.HasProjectVersionFile(Directory.GetCurrentDirectory()))
        {
            if (LoadProject())
            {
                return;
            }
        }
        AddLine("");
        AddLine("-Use menu File->Set Project Folder to set UNITY project location");
        AddLine("");
    }

    private void CheckUnityEditor()
    {
        var processes = Process.GetProcessesByName("Unity").ToList();
        processes.AddRange(Process.GetProcessesByName("bee_backend").ToList());
        if (processes.Count == 0)
        {
            return;
        }
        for (var i = 0; i < processes.Count; ++i)
        {
            var process = processes[i];
            var name = process.ProcessName;
            var moduleName = process.MainModule?.FileName ?? "";
            if (i == 0)
            {
                AddLine("", "");
            }
            AddLine("unity", $"+{name} - {process.MainWindowTitle} - {Path.GetFileName(moduleName)}");
        }
        AddLine("");
        AddLine("-It seems that UNITY Editor (or Bee build backend) is running");
        AddLine("-It is better to close them all to avoid any conflicts while building");
        AddLine("-If you do not have visible UNITY Editor but still get this error");
        AddLine("-then you have to reboot or manually kill all processes what remains after aborted or abandoned build");
        AddLine("-After that it might be good to do File->Delete UNITY Library folder to start from clean state");
        AddLine("");
    }

    private bool LoadProject()
    {
        try
        {
            LoadEnvironment();
            Text =
                $"{_baseTitle} {_settings.UnityEditorVersion} - {_settings.ProductName}" +
                $" - Target{(_settings.BuildTargets.Count > 1 ? "" : "s")} [{string.Join(',', _settings.BuildTargets)}]" +
                $" in {_settings.WorkingDirectory}";
            if (!string.IsNullOrWhiteSpace(_settings.DeliveryTrack))
            {
                Text = $"{Text} - Track {_settings.DeliveryTrack}";
            }
            UpdateProjectInfo(_settings.BuildTargets.Count > 0 ? Color.Magenta : Color.Red);
            StartupCommand();
            if (_settings.BuildTargets.Count == 0)
            {
                AddError("Could not find any build targets");
            }
            return true;
        }
        catch (Exception x)
        {
            AddLine($"Failed to LoadEnvironment");
            AddError($"{x.GetType().Name}: {x.Message}");
            if (x is not ApplicationException && x.StackTrace != null)
            {
                foreach (var line in x.StackTrace.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
                {
                    AddLine(line.Trim());
                }
            }
        }
        return false;
    }

    private void SetupFileMenuCommands()
    {
        reloadProjectToolStripMenuItem.Click += (_, _) => { ReloadProject(); };
        setProjectFolderToolStripMenuItem.Click += (_, _) => { SetProjectFolder(); };
        deleteUNITYLibraryFolderToolStripMenuItem.Click +=
            (_, _) => ExecuteMenuCommandSync("Executing", DeleteUnityLibraryFolder);
        copyProjectFilesToSecretKeysFolderToolStripMenuItem.Click +=
            (_, _) => ExecuteMenuCommandSync("Executing", CopyProjectFilesToSecretKeysFolder);
        openDebugLogToolStripMenuItem.Click += (_, _) => { OpenDebugLog(); };
        copyOutputToClipboardToolStripMenuItem.Click += (_, _) => CopyLines();
        exitToolStripMenuItem.Click += (_, _) => Application.Exit();
    }

    private void ReloadProject(bool clear = true)
    {
        if (clear)
        {
            ClearLines();
        }
        _settings = new BuildSettings(Directory.GetCurrentDirectory());
        LoadProject();
    }

    private void SetProjectFolder()
    {
        ClearLines();
        var folderBrowserDialog = new FolderBrowserDialog();
        if (folderBrowserDialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }
        var directoryName = folderBrowserDialog.SelectedPath;
        AddLine(".dir", directoryName);
        if (!Directory.Exists(directoryName))
        {
            return;
        }
        Directory.SetCurrentDirectory(directoryName);
        _settings = new BuildSettings(Directory.GetCurrentDirectory());
        LoadProject();
    }

    private void DeleteUnityLibraryFolder()
    {
        var unityOutputFolders = new List<string>()
        {
            "Library",
            "Temp",
            "Obj",
        };
        FileSystemCommands.DeleteDirectories(unityOutputFolders, (success) =>
        {
            if (!success)
            {
                CheckUnityProcesses();
            }
            ReleaseMenuCommandSync();
        });
        return;

        void CheckUnityProcesses()
        {
            var processes = Process.GetProcesses();
            var modules = new HashSet<string>();
            foreach (var process in processes)
            {
                string moduleName;
                try
                {
                    moduleName = process.MainModule?.FileName ?? "";
                    var isUnity = moduleName.Contains(@"\Editor\Data\Tools\") || moduleName.EndsWith("Unity.exe");
                    if (!isUnity)
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }
                modules.Add($"{process.ProcessName} | {process.MainWindowTitle} | {moduleName}");
            }
            var list = modules.ToList();
            list.Sort();
            AddLine("proc", $"-Running processes thing might have open files and needs to be shutdown");
            foreach (var item in list)
            {
                AddLine("proc", $"-{item}");
            }
        }
    }

    private void CopyProjectFilesToSecretKeysFolder()
    {
        if (!ProjectCommands.CopyFilesToSecretKeys(_settings))
        {
            AddError($"Unable copy all files");
            ReleaseMenuCommandSync();
        }
    }

    private static void OpenDebugLog()
    {
        var appDir = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
        var logFilename = Path.Combine(appDir, "logs", "StartUnityBuild_trace.log");
        if (!File.Exists(logFilename))
        {
            AddError($"File not found {logFilename}");
            return;
        }
        const string executable = "notepad.exe";
        try
        {
            AddLine(".file", $"open {logFilename}");
            Process.Start(executable, logFilename);
        }
        catch (Exception x)
        {
            AddError($"Unable to start {executable}: {x.GetType().Name} {x.Message}");
        }
    }

    private static void CopyLines()
    {
        var builder = new StringBuilder();
        var listView = _instance.listView1;
        foreach (var item in listView.Items)
        {
            builder.AppendLine(item is ListViewItem listViewItem ? listViewItem.Text : item.ToString());
        }
        Clipboard.SetText(builder.ToString());
    }

    private void SetupBuildMenuCommands()
    {
        timer1.Tick += (_, _) =>
        {
            var duration = DateTime.Now - _commandStartTime;
            SetStatus($"{_commandLabel} {duration:mm':'ss}", Color.Green);
            if (duration.Minutes >= _commandWatchMinutes)
            {
                _commandWatchMinutes += WatchMinutes;
                AddLine("timer", $"-just notice that {_commandLabel} has been running for {duration:mm':'ss}");
            }
        };
        var order = -1;
        SetCaption(gitStatusToolStripMenuItem, ++order);
        gitStatusToolStripMenuItem.Click += (_, _) => ExecuteMenuCommandSync("Querying",
            () => { GitCommands.GitStatus(_settings.WorkingDirectory, ReleaseMenuCommandSync); });

        SetCaption(gitPullToolStripMenuItem, ++order);
        gitPullToolStripMenuItem.Click += (_, _) => ExecuteMenuCommandSync("Executing",
            () =>
            {
                GitCommands.GitPull(_settings.WorkingDirectory, () =>
                {
                    // Wait for git command to finish its output.
                    Thread.Sleep(2000);
                    // Reload project - if might have been changed!
                    ReloadProject(clear: false);
                    AddExitCode("Reload", 0, true, showSuccess: true);
                    ReleaseMenuCommandSync();
                });
            });

        SetCaption(updateBuildToolStripMenuItem, ++order);
        updateBuildToolStripMenuItem.Click += (_, _) => ExecuteMenuCommandSync("Updating", () =>
        {
            ProjectCommands.ModifyProject(_settings,
                (updated) =>
                {
                    ReleaseMenuCommandSync();
                    UpdateProjectInfo(updated ? Color.Green : Color.Red);
                });
        });
        SetCaption(gitPushToolStripMenuItem, ++order);
        gitPushToolStripMenuItem.Click += (_, _) => ExecuteMenuCommandSync("Executing",
            () => { GitCommands.GitCommitAndPushWithTag(_settings, ReleaseMenuCommandSync); });

        SetCaption(startBuildToolStripMenuItem, ++order);
        startBuildToolStripMenuItem.Click += (_, _) => ExecuteMenuCommandSync("Building",
            () =>
            {
                // (1) Copy required (secret) files for build.
                // (2) Build the project.
                // (3) Revert copied and/or changed files.
                if (!ProjectCommands.CopyFilesToProject(_settings))
                {
                    AddError($"Unable to start build: can not copy required 'build project' files");
                    ReleaseMenuCommandSync();
                    return;
                }
                BuildCommands.BuildPlayer(_settings,
                    (_) =>
                    {
                        GitCommands.GitRevert(
                            _settings.WorkingDirectory, _settings.RevertFilesAfter, () =>
                            {
                                PlayNotification();
                                ReleaseMenuCommandSync();
                            });
                    });
            });

        // Post processing will be enabled when applicable.
        postProcessToolStripMenuItem.Enabled = false;
        SetCaption(postProcessToolStripMenuItem, ++order);
        postProcessToolStripMenuItem.Click += (_, _) => ExecuteMenuCommandSync("Executing", () =>
        {
            PostProcessBuild();
            ReleaseMenuCommandSync();
        });
        return;

        void SetCaption(ToolStripItem item, int itemNumber)
        {
            item.Text = $"[{itemNumber}] {item.Text}";
        }
    }

    private void PostProcessBuild()
    {
        if (!_settings.HasPostProcessingFor(BuildName.WebGL))
        {
            AddError($"{BuildName.WebGL} build is not in selected build targets");
            return;
        }
        if (Args.Instance.IsTesting)
        {
            _settings.BuildResult[_settings.BuildTargets.FindIndex(x => x == BuildName.WebGL)] = true;
        }
        if (!_settings.BuildSucceeded(BuildName.WebGL))
        {
            AddError($"{BuildName.WebGL} build was not successful, can not post process!");
            return;
        }
        ProjectCommands.WriteWebGLBuildHistory(_settings, true);
        FileSystemCommands.CopyDirectories(_settings.WebGlBuildDirName, _settings.WebGlDistFolderName,
            ReleaseMenuCommandSync);
    }

    private void UpdateProjectInfo(Color color)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateProjectInfo(color));
            return;
        }
        projectInfoToolStripMenuItem.Text = $"Version {_settings.ProductVersion} Bundle {_settings.BundleVersion}";
        projectInfoToolStripMenuItem.ForeColor = color;
    }

    private void StartupCommand()
    {
        Thread.Yield();
        ExecuteMenuCommand(() =>
            GitCommands.GitStatus(_settings.WorkingDirectory, () =>
            {
                SetStatus("Ready", Color.Blue);
                CheckUnityEditor();
            }));
    }

    private void ExecuteMenuCommandSync(string commandLabel, Action command)
    {
        if (_isCommandExecuting)
        {
            MessageBox.Show("A command is already executing", "UNITY Build", MessageBoxButtons.OK,
                MessageBoxIcon.Exclamation);
            return;
        }
        if (_settings.BuildTargets.Count == 0)
        {
            MessageBox.Show("No build target found", "UNITY Build", MessageBoxButtons.OK,
                MessageBoxIcon.Exclamation);
            return;
        }
        _isCommandExecuting = true;
        _commandLabel = commandLabel;
        _commandStartTime = DateTime.Now;
        _commandWatchMinutes = WatchMinutes;
        timer1.Start();
        ClearLines();
        try
        {
            command();
        }
        catch (Exception x)
        {
            AddError($"{x.Message}");
        }
    }

    private void ReleaseMenuCommandSync()
    {
        _isCommandExecuting = false;
        timer1.Stop();
        var duration = DateTime.Now - _commandStartTime;
        SetStatus($"Done in {duration:mm':'ss}", Color.Blue);
    }

    private void ExecuteMenuCommand(Action command)
    {
        try
        {
            command();
        }
        catch (Exception x)
        {
            AddError($"{x.Message}");
        }
    }

    private static void PlayNotification()
    {
        if (_instance.InvokeRequired)
        {
            _instance.Invoke(() => PlayNotification);
            return;
        }
        SystemSounds.Exclamation.Play();
    }

    private void SetStatus(string statusText, Color color)
    {
        if (InvokeRequired)
        {
            Invoke(() => SetStatus($"[{statusText}]", color));
            return;
        }
        label1.Text = statusText;
        label1.ForeColor = color;
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e is { Control: true, KeyCode: Keys.C })
        {
            CopyLines();
            e.SuppressKeyPress = true; // Stops other controls on the form receiving event.
        }
    }

    private void LoadEnvironment()
    {
        AddLine(">Project", $"{_settings.WorkingDirectory}");
        try
        {
            Files.LoadProjectVersionFile(_settings.WorkingDirectory, out var unityVersion);
            _settings.UnityEditorVersion = unityVersion;
            AppSettings.SetUnityProjectFolder(_settings.WorkingDirectory);
        }
        catch (DirectoryNotFoundException)
        {
            throw new ApplicationException($"ProjectVersion.txt not found, is this UNITY project folder?");
        }
        AddLine(".unity", $"{_settings.UnityEditorVersion}");
        ProjectSettings.LoadProjectSettingsFile(_settings.WorkingDirectory,
            out var productName, out var productVersion, out var bundleVersion, out var muteOtherAudioSources);
        _settings.ProductName = productName;
        _settings.ProductVersion = productVersion;
        _settings.BundleVersion = bundleVersion;
        _settings.IsMuteOtherAudioSources = muteOtherAudioSources;
        Files.LoadAutoBuildSettings(_settings);
        AddLine(".env.ver", $"{_settings.BuildEnvVer}");
        AddLine("Product", $"{_settings.ProductName}");
        var versionType = SemVer.GetVersionType(_settings.ProductVersion);
        AddLine("Product", $"{_settings.ProductName}");
        AddLine("Version", $"{_settings.ProductVersion} ({versionType})");
        AddLine("Bundle", $"{_settings.BundleVersion}");
        AddLine("Builds", $"{string.Join(',', _settings.BuildTargets)}");
        // List files used in build.
        var assetFolder = Files.GetAssetFolder(_settings.WorkingDirectory);
        _settings.BuildInfoFilename = BuildInfoUpdater.BuildPropertiesPath(assetFolder);
        var exists = File.Exists(_settings.BuildInfoFilename);
        AddLine($"{(exists ? ".file" : "ERROR")}", $"update {_settings.BuildInfoFilename}");
        if (!exists)
        {
            AddError($"assetFolder {assetFolder}");
        }
        if (_settings.CopyFilesBefore.Count > 0)
        {
            // This checks some CopyFiles validity as well.
            var copyFiles = Files.GetCopyFiles(_settings);
            foreach (var tuple in copyFiles)
            {
                exists = File.Exists(tuple.Item1);
                AddLine($"{(exists ? ".file" : "ERROR")}", $"copy {tuple.Item1} to {tuple.Item2}");
            }
        }
        if (_settings.RevertFilesAfter.Count > 0)
        {
            foreach (var file in _settings.RevertFilesAfter)
            {
                var path = Path.Combine(".", file);
                exists = File.Exists(path);
                AddLine($"{(exists ? ".file" : "ERROR")}", $"git revert {path}");
            }
        }
        if (_settings.HasBuildTarget(BuildName.Android))
        {
            _settings.AndroidSettingsFileName = Files.GetAndroidSettingsFileName(_settings.WorkingDirectory);
            exists = File.Exists(_settings.AndroidSettingsFileName);
            AddLine($"{(exists ? ".file" : "ERROR")}", $"android {_settings.AndroidSettingsFileName}");
        }
        if (_settings.HasPostProcessingFor(BuildName.WebGL))
        {
            postProcessToolStripMenuItem.Enabled = true;
            AddLine(".file", $"webgl host {_settings.WebGlHostName}");
            AddLine(".file", $"webgl html {_settings.WebGlBuildHistoryHtml}");
            AddLine(".file", $"webgl json {_settings.WebGlBuildHistoryJson}");
            AddLine(".file", $"webgl href {_settings.WebGlBuildHistoryUrl}");
            AddLine(".file", $"webgl build {_settings.WebGlBuildDirName}");
            AddLine(".file", $"webgl dist {_settings.WebGlDistFolderName}");
        }
        var setUnityExecutablePath =
            !string.IsNullOrEmpty(_settings.UnityPath) && !string.IsNullOrEmpty(_settings.UnityEditorVersion);
        if (setUnityExecutablePath)
        {
            _settings.UnityExecutable =
                BuildSettings.ExpandUnityPath(_settings.UnityPath, _settings.UnityEditorVersion);
            exists = File.Exists(_settings.UnityExecutable);
            AddLine($"{(exists ? ".file" : "ERROR")}", $"UNITY {_settings.UnityExecutable}");
            if (!exists)
            {
                AddError(
                    $"UnityExecutable not found for path '{_settings.UnityPath}' and version '{_settings.UnityEditorVersion}'");
            }
        }
        else
        {
            AddError(
                $"UnityExecutable error with path '{_settings.UnityPath}' and version '{_settings.UnityEditorVersion}'");
        }
    }

    [SuppressMessage("ReSharper", "NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract")]
    public static void OutputListener(string prefix, string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }
        if (string.IsNullOrEmpty(prefix) || !prefix.StartsWith("ERROR"))
        {
            AddLine(prefix, line);
        }
        AddLine(prefix ?? "ERROR", line);
    }

    public static void ExitListener(string processPrefix, string commandPrefix, int exitCode)
    {
        if (exitCode == 0)
        {
            AddLine(processPrefix, commandPrefix, Color.Blue);
        }
        AddLine(processPrefix, $"{commandPrefix} exit: {exitCode}", Color.Magenta, Color.Blue);
    }

    private void ClearLines()
    {
        if (InvokeRequired)
        {
            Invoke(() => ClearLines);
            return;
        }
        _listView.ClearLines();
    }

    public static void AddExitCode(string prefix, int exitCode, bool isSuccess, bool showSuccess = true)
    {
        if (isSuccess && !showSuccess)
        {
            return;
        }
        if (isSuccess)
        {
            AddLine(prefix, "exited successfully", Color.Green);
        }
        else
        {
            AddLine(prefix, $"execution failed ({exitCode})", Color.Red, Color.Green);
        }
    }

    public static void AddError(string message)
    {
        AddLine("ERROR", message, Color.Red);
    }

    private static void AddLine(string content)
    {
        AddLine("info", content);
    }

    public static void AddLine(string prefix, string line)
    {
        AddLine(prefix, line, Color.Gray, Color.Black);
    }

    private static void AddLine(string prefix, string line, Color lineColor)
    {
        AddLine(prefix, line, lineColor, lineColor);
    }

    private static void AddLine(string prefix, string line, Color prefixColor, Color lineColor)
    {
        if (_instance.InvokeRequired)
        {
            _instance.Invoke(() => AddLine(prefix, line, prefixColor, lineColor));
            return;
        }
        line = $"{DateTime.Now:HH:mm:ss} {line}";
        _instance._listView.AddLine(prefix ?? "---", line ?? "---", prefixColor, lineColor);
    }
}