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
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(15);

        private List<VsCodeWorkspace> _cachedWorkspaces;

        private DateTimeOffset _cacheExpiresAt;

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
                if (_cachedWorkspaces != null && DateTimeOffset.UtcNow < _cacheExpiresAt)
                    return _cachedWorkspaces;

                var results = new List<VsCodeWorkspace>();

                foreach (var vscodeInstance in VSCodeInstances.Instances)
                {
                    var vscodeStorageFile = ReadStorageFile(vscodeInstance);
                    var profileNames = GetProfileNames(vscodeStorageFile);
                    var profileAssociations = vscodeStorageFile?.ProfileAssociations?.Workspaces;

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
                                        ParseFolderEntry(folderUri, vscodeInstance, entry, profileAssociations, profileNames) is { } folderWorkspace)
                                    {
                                        results.Add(folderWorkspace);
                                    }
                                    else if (entry.TryGetProperty("workspace", out var workspaceInfo) &&
                                             ParseWorkspaceEntry(workspaceInfo, vscodeInstance, entry, profileAssociations, profileNames) is { } workspace)
                                    {
                                        results.Add(workspace);
                                    }
                                }
                            }
                        }
                    }

                    if (vscodeStorageFile != null)
                    {
                        // for previous versions of vscode
                        if (vscodeStorageFile.OpenedPathsList?.Workspaces3 != null)
                        {
                            results.AddRange(
                                vscodeStorageFile.OpenedPathsList.Workspaces3
                                    .Select(workspaceUri => WithProfile(ParseVSCodeUri(workspaceUri, vscodeInstance),
                                        workspaceUri?.ToString(), profileAssociations, profileNames))
                                    .Where(uri => uri != null)
                                    .Select(uri => (VsCodeWorkspace)uri));
                        }

                        // vscode v1.55.0 or later
                        if (vscodeStorageFile.OpenedPathsList?.Entries != null)
                        {
                            results.AddRange(vscodeStorageFile.OpenedPathsList.Entries
                                .Select(x => WithProfile(ParseVSCodeUri(x.FolderUri, vscodeInstance), x.FolderUri,
                                    profileAssociations, profileNames))
                                .Where(uri => uri != null));
                        }
                    }
                }

                _cachedWorkspaces = results;
                _cacheExpiresAt = DateTimeOffset.UtcNow.Add(CacheDuration);
                return results;
            }
        }

        public void ClearCache()
        {
            _cachedWorkspaces = null;
            _cacheExpiresAt = DateTimeOffset.MinValue;
        }

        [CanBeNull]
        private static VSCodeStorageFile ReadStorageFile(VSCodeInstance vscodeInstance)
        {
            var storageFiles = new[]
            {
                Path.Combine(vscodeInstance.AppData, "storage.json"),
                Path.Combine(vscodeInstance.AppData, "User", "globalStorage", "storage.json")
            };

            VSCodeStorageFile storageFile = null;
            foreach (var vscodeStorage in storageFiles)
            {
                if (!File.Exists(vscodeStorage))
                    continue;

                try
                {
                    storageFile = MergeStorageFiles(storageFile,
                        JsonSerializer.Deserialize<VSCodeStorageFile>(File.ReadAllText(vscodeStorage)));
                }
                catch (Exception ex)
                {
                    var message = $"Failed to deserialize ${vscodeStorage}";
                    Main.Context.API.LogException("VSCodeWorkspaceApi", message, ex);
                }
            }

            return storageFile;
        }

        private static VSCodeStorageFile MergeStorageFiles(VSCodeStorageFile current, VSCodeStorageFile next)
        {
            if (current == null)
                return next;
            if (next == null)
                return current;

            return new VSCodeStorageFile
            {
                OpenedPathsList = current.OpenedPathsList ?? next.OpenedPathsList,
                ProfileAssociations = current.ProfileAssociations ?? next.ProfileAssociations,
                UserDataProfiles = current.UserDataProfiles ?? next.UserDataProfiles
            };
        }

        private static Dictionary<string, string> GetProfileNames(VSCodeStorageFile vscodeStorageFile)
        {
            var profileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["__default__profile__"] = "Default"
            };

            if (vscodeStorageFile?.UserDataProfiles == null)
                return profileNames;

            foreach (var profile in vscodeStorageFile.UserDataProfiles)
            {
                if (!string.IsNullOrEmpty(profile.Location) && !string.IsNullOrEmpty(profile.Name))
                    profileNames[profile.Location] = profile.Name;
            }

            return profileNames;
        }

        private static VsCodeWorkspace WithProfile(VsCodeWorkspace workspace, string workspaceUri,
            Dictionary<string, string> profileAssociations, Dictionary<string, string> profileNames)
        {
            if (workspace == null || string.IsNullOrEmpty(workspaceUri) || profileAssociations == null)
                return workspace;

            var normalizedWorkspaceUri = Uri.UnescapeDataString(workspaceUri);
            if (!TryGetProfileId(profileAssociations, workspaceUri, out var profileId) &&
                !TryGetProfileId(profileAssociations, normalizedWorkspaceUri, out profileId) &&
                !TryGetProfileId(profileAssociations, workspace.Path.ToString(), out profileId))
            {
                return workspace;
            }

            return workspace with
            {
                ProfileName = profileNames.TryGetValue(profileId, out var profileName) ? profileName : profileId
            };
        }

        private static bool TryGetProfileId(Dictionary<string, string> profileAssociations, string workspaceUri,
            out string profileId)
        {
            profileId = null;

            return !string.IsNullOrEmpty(workspaceUri) &&
                   profileAssociations.TryGetValue(workspaceUri, out profileId);
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
            JsonElement entry, Dictionary<string, string> profileAssociations, Dictionary<string, string> profileNames)
        {
            if (workspaceInfo.TryGetProperty("configPath", out var configPath))
            {
                var configPathString = configPath.GetString();
                var workspace = WithProfile(ParseVSCodeUri(configPathString, vscodeInstance), configPathString,
                    profileAssociations, profileNames);
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
            JsonElement entry, Dictionary<string, string> profileAssociations, Dictionary<string, string> profileNames)
        {
            var workspaceUri = folderUri.GetString();
            var workspace = WithProfile(ParseVSCodeUri(workspaceUri, vscodeInstance), workspaceUri, profileAssociations,
                profileNames);
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
