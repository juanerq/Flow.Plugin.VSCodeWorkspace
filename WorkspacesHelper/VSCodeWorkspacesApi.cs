// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flow.Plugin.VSCodeWorkspaces.VSCodeHelper;
using JetBrains.Annotations;
using Microsoft.Data.Sqlite;

namespace Flow.Plugin.VSCodeWorkspaces.WorkspacesHelper
{
    public class VSCodeWorkspacesApi
    {
        public VSCodeWorkspacesApi()
        {
        }

        public static VsCodeWorkspace ParseVSCodeUri(string uri, VSCodeInstance vscodeInstance)
        {
            if (uri is not null)
            {
                var unescapeUri = Uri.UnescapeDataString(uri);
                var typeWorkspace = WorkspacesHelper.ParseVSCodeUri.GetTypeWorkspace(unescapeUri);
                if (!typeWorkspace.workspaceLocation.HasValue) return null;
                var folderName = Path.GetFileName(unescapeUri);

                // Check we haven't returned '' if we have a path like C:\
                if (string.IsNullOrEmpty(folderName))
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(unescapeUri);
                    folderName = dirInfo.Name.TrimEnd(':');
                }

                return new VsCodeWorkspace()
                {
                    Path = unescapeUri,
                    RelativePath = typeWorkspace.Path,
                    FolderName = folderName,
                    ExtraInfo = typeWorkspace.MachineName,
                    WorkspaceLocation = typeWorkspace.workspaceLocation.Value,
                    VSCodeInstance = vscodeInstance,
                };
            }

            return null;
        }

        public readonly Regex WorkspaceLabelParser = new Regex("(.+?)(\\[.+\\])");

        public List<VsCodeWorkspace> Workspaces
        {
            get
            {
                var results = new List<VsCodeWorkspace>();

                foreach (var vscodeInstance in VSCodeInstances.Instances)
                {
                    // for vscode v1.64.0 or later
                    var stateDatabasePath = GetStateDatabasePath(vscodeInstance);
                    if (stateDatabasePath != null)
                    {
                        var connectionString = new SqliteConnectionStringBuilder
                        {
                            DataSource = stateDatabasePath,
                            Mode = SqliteOpenMode.ReadOnly,
                            Cache = SqliteCacheMode.Shared
                        }.ToString();

                        using var connection = new SqliteConnection(connectionString);
                        connection.Open();
                        var command = connection.CreateCommand();
                        command.CommandText = "SELECT value FROM ItemTable where key = 'history.recentlyOpenedPathsList'";
                        var result = command.ExecuteScalar();
                        if (result != null)
                        {
                            using var historyDoc = JsonDocument.Parse(result.ToString()!);
                            var root = historyDoc.RootElement;
                            if (root.TryGetProperty("entries", out var entries))
                            {
                                foreach (var entry in entries.EnumerateArray())
                                {
                                    if (entry.TryGetProperty("folderUri", out var folderUri) &&
                                        ParseFolderEntry(folderUri, vscodeInstance, entry) is { } folderWorkspace)
                                    {
                                        results.Add(folderWorkspace);
                                    }
                                    else if (entry.TryGetProperty("workspace", out var workspaceInfo) &&
                                             ParseWorkspaceEntry(workspaceInfo, vscodeInstance, entry) is { } workspace)
                                    {
                                        results.Add(workspace);
                                    }
                                }
                            }
                        }
                    }

                    // storage.json contains opened Workspaces
                    var vscodeStorage = Path.Combine(vscodeInstance.AppData, "storage.json");

                    if (File.Exists(vscodeStorage))
                    {
                        var fileContent = File.ReadAllText(vscodeStorage);

                        try
                        {
                            var vscodeStorageFile = JsonSerializer.Deserialize<VSCodeStorageFile>(fileContent);

                            if (vscodeStorageFile != null)
                            {
                                // for previous versions of vscode
                                if (vscodeStorageFile.OpenedPathsList?.Workspaces3 != null)
                                {
                                    results.AddRange(
                                        vscodeStorageFile.OpenedPathsList.Workspaces3
                                            .Select(workspaceUri => ParseVSCodeUri(workspaceUri, vscodeInstance))
                                            .Where(uri => uri != null)
                                            .Select(uri => (VsCodeWorkspace)uri));
                                }

                                // vscode v1.55.0 or later
                                if (vscodeStorageFile.OpenedPathsList?.Entries != null)
                                {
                                    results.AddRange(vscodeStorageFile.OpenedPathsList.Entries
                                        .Select(x => x.FolderUri)
                                        .Select(workspaceUri => ParseVSCodeUri(workspaceUri, vscodeInstance))
                                        .Where(uri => uri != null));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            var message = $"Failed to deserialize ${vscodeStorage}";
                            Main.Context.API.LogException("VSCodeWorkspaceApi", message, ex);
                        }
                    }
                }

                return results;
            }
        }

        [CanBeNull]
        private static string GetStateDatabasePath(VSCodeInstance vscodeInstance)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var oldPath = Path.Combine(vscodeInstance.AppData, "User", "globalStorage", "state.vscdb");

            if (string.IsNullOrEmpty(userProfile))
                return File.Exists(oldPath) ? oldPath : null;

            var sharedStorageFolder = vscodeInstance.VSCodeVersion == VSCodeVersion.Insiders
                ? ".vscode-insiders-shared"
                : ".vscode-shared";

            var newPath = Path.Combine(userProfile, sharedStorageFolder, "sharedStorage", "state.vscdb");
            if (File.Exists(newPath))
                return newPath;

            if (vscodeInstance.VSCodeVersion == VSCodeVersion.Insiders)
            {
                var stableSharedPath = Path.Combine(userProfile, ".vscode-shared", "sharedStorage", "state.vscdb");
                if (File.Exists(stableSharedPath))
                    return stableSharedPath;
            }

            return File.Exists(oldPath) ? oldPath : null;
        }

        [CanBeNull]
        private VsCodeWorkspace ParseWorkspaceEntry(JsonElement workspaceInfo, VSCodeInstance vscodeInstance,
            JsonElement entry)
        {
            if (workspaceInfo.TryGetProperty("configPath", out var configPath))
            {
                var workspace = ParseVSCodeUri(configPath.GetString(), vscodeInstance);
                if (workspace == null)
                    return null;

                if (entry.TryGetProperty("label", out var label))
                {
                    var labelString = label.GetString()!;
                    var matchGroup = WorkspaceLabelParser.Match(labelString);
                    workspace = workspace with
                    {
                        Label = $"{matchGroup.Groups[2]} {matchGroup.Groups[1]}",
                        WorkspaceType = WorkspaceType.Workspace
                    };
                }

                return workspace;
            }

            return null;
        }


        [CanBeNull]
        private VsCodeWorkspace ParseFolderEntry(JsonElement folderUri, VSCodeInstance vscodeInstance,
            JsonElement entry)
        {
            var workspaceUri = folderUri.GetString();
            var workspace = ParseVSCodeUri(workspaceUri, vscodeInstance);
            if (workspace == null)
                return null;

            if (entry.TryGetProperty("label", out var label))
            {
                var labelString = label.GetString()!;
                var matchGroup = WorkspaceLabelParser.Match(labelString);
                workspace = workspace with
                {
                    Label = $"{matchGroup.Groups[2]} {matchGroup.Groups[1]}",
                    WorkspaceType = WorkspaceType.Folder
                };
            }

            return workspace;
        }
    }
}
