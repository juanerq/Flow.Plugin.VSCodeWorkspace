// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Flow.Plugin.VSCodeWorkspaces
{
    using Flow.Launcher.Plugin;
    using Properties;
    using RemoteMachinesHelper;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Windows.Controls;
    using VSCodeHelper;
    using WorkspacesHelper;

    public class Main : IPlugin, IPluginI18n, ISettingProvider, IContextMenu
    {
        internal static PluginInitContext Context { get; private set; }

        private static Settings _settings;

        public string Name => GetTranslatedPluginTitle();

        public string Description => GetTranslatedPluginDescription();

        private VSCodeInstance _defaultInstance;

        private readonly VSCodeWorkspacesApi _workspacesApi = new();

        private readonly VSCodeRemoteMachinesApi _machinesApi = new();

        public List<Result> Query(Query query)
        {
            var results = new List<Result>();
            var workspaces = new List<VsCodeWorkspace>();

            // Search opened workspaces
            if (_settings.DiscoverWorkspaces)
            {
                workspaces.AddRange(_workspacesApi.Workspaces);
            }

            // User defined extra workspaces
            if (_defaultInstance != null)
            {
                workspaces.AddRange(_settings.CustomWorkspaces.Select(uri =>
                    VSCodeWorkspacesApi.ParseVSCodeUri(uri, _defaultInstance)));
            }

            // Simple de-duplication
            var hiddenWorkspaces = new HashSet<string>(_settings.HiddenWorkspaces, StringComparer.OrdinalIgnoreCase);
            results.AddRange(workspaces.Distinct()
                .Where(ws => !hiddenWorkspaces.Contains(ws.Path))
                .Select(CreateWorkspaceResult)
            );

            // Search opened remote machines
            if (_settings.DiscoverMachines)
            {
                _machinesApi.Machines.ForEach(a =>
                {
                    var title = $"{a.Host}";

                    if (!string.IsNullOrEmpty(a.User) && !string.IsNullOrEmpty(a.HostName))
                    {
                        title += $" [{a.User}@{a.HostName}]";
                    }

                    var tooltip = Resources.SSHRemoteMachine;

                    results.Add(new Result
                    {
                        Title = title,
                        SubTitle = Resources.SSHRemoteMachine,
                        Icon = a.VSCodeInstance.RemoteIcon,
                        TitleToolTip = tooltip,
                        Action = c =>
                        {
                            bool hide;
                            try
                            {
                                var process = new ProcessStartInfo
                                {
                                    FileName = a.VSCodeInstance.ExecutablePath,
                                    UseShellExecute = true,
                                    Arguments =
                                        $"--new-window --enable-proposed-api ms-vscode-remote.remote-ssh --remote ssh-remote+{((char)34) + a.Host + ((char)34)}",
                                    WindowStyle = ProcessWindowStyle.Hidden,
                                };
                                Process.Start(process);

                                hide = true;
                            }
                            catch (Win32Exception)
                            {
                                var name = $"{Context.CurrentPluginMetadata.Name}";
                                string msg = Resources.OpenFail;
                                Context.API.ShowMsg(name, msg, string.Empty);
                                hide = false;
                            }

                            return hide;
                        },
                        ContextData = a,
                    });
                });
            }

            if (query.ActionKeyword == string.Empty ||
                (query.ActionKeyword != string.Empty && query.Search != string.Empty))
            {
                results = results.Where(r =>
                {
                    r.Score = Context.API.FuzzySearch(query.Search, r.Title).Score;
                    return r.Score > 0;
                }).ToList();
            }


            return results;
        }

        private static Result CreateWorkspaceResult(VsCodeWorkspace ws)
        {
            var title = $"{ws.FolderName}";
            var typeWorkspace = ws.WorkspaceTypeToString();

            if (ws.WorkspaceLocation != WorkspaceLocation.Local)
            {
                title = $"{title}{(ws.ExtraInfo != null ? $" - {ws.ExtraInfo}" : string.Empty)} ({typeWorkspace})";
            }

            var tooltip =
                $"{Resources.Workspace}{(ws.WorkspaceLocation != WorkspaceLocation.Local ? $" {Resources.In} {typeWorkspace}" : string.Empty)}: {SystemPath.RealPath(ws.RelativePath)}";
            var gitStatus = _settings.ShowGitStatus ? GitStatusHelper.GetStatus(ws) : null;
            var subtitle = string.IsNullOrEmpty(gitStatus)
                ? tooltip
                : $"{tooltip}  |  {gitStatus}";

            return new Result
            {
                Title = title,
                SubTitle = subtitle,
                Icon = ws.VSCodeInstance.WorkspaceIcon,
                TitleToolTip = subtitle,
                Action = c =>
                {
                    try
                    {
                        var modifierKeys = c.SpecialKeyState.ToModifierKeys();
                        if (modifierKeys == System.Windows.Input.ModifierKeys.Control)
                        {
                            Context.API.OpenDirectory(SystemPath.RealPath(ws.RelativePath));
                            return true;
                        }

                        var process = new ProcessStartInfo
                        {
                            FileName = ws.VSCodeInstance.ExecutablePath,
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                        };

                        process.ArgumentList.Add(ws.WorkspaceType == WorkspaceType.Workspace
                            ? "--file-uri"
                            : "--folder-uri");

                        process.ArgumentList.Add(ws.Path);

                        Process.Start(process);
                        return true;
                    }
                    catch (Win32Exception)
                    {
                        var name = $"{Context.CurrentPluginMetadata.Name}";
                        string msg = Resources.OpenFail;
                        Context.API.ShowMsg(name, msg, string.Empty);
                    }

                    return false;
                },
                ContextData = ws,
            };
        }

        public void Init(PluginInitContext context)
        {
            Context = context;
            _settings = context.API.LoadSettingJsonStorage<Settings>();

            VSCodeInstances.LoadVSCodeInstances();

            // Prefer stable version, or the first one we got
            _defaultInstance = VSCodeInstances.Instances.Find(e => e.VSCodeVersion == VSCodeVersion.Stable) ??
                              VSCodeInstances.Instances.FirstOrDefault();
        }

        public Control CreateSettingPanel() => new SettingsView(Context, _settings);

        public void OnCultureInfoChanged(CultureInfo newCulture)
        {
            Resources.Culture = newCulture;
        }

        public string GetTranslatedPluginTitle()
        {
            return Resources.PluginTitle;
        }

        public string GetTranslatedPluginDescription()
        {
            return Resources.PluginDescription;
        }

        public List<Result> LoadContextMenus(Result selectedResult)
        {
            List<Result> results = new();
            if (selectedResult.ContextData is VsCodeWorkspace ws && ws.WorkspaceLocation == WorkspaceLocation.Local)
            {
                results.Add(new Result
                {
                    Title = Resources.OpenFolder,
                    SubTitle = Resources.OpenFolderSubTitle,
                    Icon = ws.VSCodeInstance.WorkspaceIcon,
                    TitleToolTip = Resources.OpenFolderSubTitle,
                    Action = c =>
                    {
                        Context.API.OpenDirectory(SystemPath.RealPath(ws.RelativePath));
                        return true;
                    },
                    ContextData = ws,
                });
            }

            if (selectedResult.ContextData is VsCodeWorkspace workspace)
            {
                results.Add(new Result
                {
                    Title = "Hide workspace",
                    SubTitle = "Remove this workspace from VS Code Workspaces search results",
                    Icon = workspace.VSCodeInstance.WorkspaceIcon,
                    TitleToolTip = "You can restore hidden workspaces from the plugin settings.",
                    Action = c =>
                    {
                        HideWorkspace(workspace);
                        Context.API.ShowMsg(Name, $"Hidden: {workspace.FolderName}", string.Empty);
                        return false;
                    },
                    ContextData = workspace,
                });
            }

            return results;
        }

        private static void HideWorkspace(VsCodeWorkspace workspace)
        {
            var workspacePath = workspace.Path.ToString();

            if (!_settings.HiddenWorkspaces.Contains(workspacePath, StringComparer.OrdinalIgnoreCase))
            {
                _settings.HiddenWorkspaces.Add(workspacePath);
            }

            var customWorkspace = _settings.CustomWorkspaces.FirstOrDefault(uri =>
                string.Equals(uri, workspacePath, StringComparison.OrdinalIgnoreCase));
            if (customWorkspace != null)
            {
                _settings.CustomWorkspaces.Remove(customWorkspace);
            }

            Context.API.SaveSettingJsonStorage<Settings>();
        }
    }
}
