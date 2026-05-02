using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
namespace Flow.Plugin.VSCodeWorkspaces.WorkspacesHelper
{
    internal static class GitStatusHelper
    {
        private const int TimeoutMilliseconds = 700;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

        private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static string GetStatus(VsCodeWorkspace workspace)
        {
            if (workspace.WorkspaceLocation is not (WorkspaceLocation.Local or WorkspaceLocation.RemoteWSL))
                return null;

            var workspacePath = GetWorkspaceDirectory(workspace);
            if (string.IsNullOrEmpty(workspacePath))
                return null;

            var cacheKey = GetCacheKey(workspace, workspacePath);
            if (Cache.TryGetValue(cacheKey, out var cacheEntry) &&
                DateTimeOffset.UtcNow - cacheEntry.CreatedAt < CacheDuration)
            {
                return cacheEntry.Status;
            }

            var status = workspace.WorkspaceLocation == WorkspaceLocation.RemoteWSL
                ? GetWslStatus(workspace.ExtraInfo, workspacePath)
                : GetLocalStatus(workspacePath);

            Cache[cacheKey] = new CacheEntry(DateTimeOffset.UtcNow, status);
            return status;
        }

        public static void ClearCache(VsCodeWorkspace workspace)
        {
            var workspacePath = GetWorkspaceDirectory(workspace);
            if (string.IsNullOrEmpty(workspacePath))
                return;

            Cache.Remove(GetCacheKey(workspace, workspacePath));
        }

        private static string GetCacheKey(VsCodeWorkspace workspace, string workspacePath)
        {
            return $"{workspace.WorkspaceLocation}|{workspace.ExtraInfo}|{workspacePath}";
        }

        private static string GetWorkspaceDirectory(VsCodeWorkspace workspace)
        {
            var path = workspace.RelativePath.ToString();

            if (workspace.WorkspaceType == WorkspaceType.Workspace)
            {
                return SystemPath.RealPath(Path.GetDirectoryName(path));
            }

            return SystemPath.RealPath(path);
        }

        private static string GetLocalStatus(string path)
        {
            if (!Directory.Exists(path))
                return null;

            return GetStatus("git", new[] { "-C", path, "status", "--porcelain=v1", "--branch" });
        }

        private static string GetWslStatus(string distro, string path)
        {
            if (string.IsNullOrEmpty(distro))
                return null;

            return GetStatus("wsl.exe", new[] { "-d", distro, "git", "-C", path, "status", "--porcelain=v1", "--branch" });
        }

        private static string GetStatus(string fileName, IEnumerable<string> arguments)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    },
                };

                foreach (var argument in arguments)
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }

                process.Start();

                if (!process.WaitForExit(TimeoutMilliseconds))
                {
                    process.Kill(true);
                    return null;
                }

                if (process.ExitCode != 0)
                    return null;

                return ParseStatus(process.StandardOutput.ReadToEnd());
            }
            catch
            {
                return null;
            }
        }

        private static string ParseStatus(string output)
        {
            var branch = string.Empty;
            var modified = 0;
            var untracked = 0;

            using var reader = new StringReader(output);
            while (reader.ReadLine() is { } line)
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    branch = ParseBranch(line);
                    continue;
                }

                if (line.StartsWith("??", StringComparison.Ordinal))
                {
                    untracked++;
                }
                else if (line.Length >= 2 && (line[0] != ' ' || line[1] != ' '))
                {
                    modified++;
                }
            }

            if (string.IsNullOrEmpty(branch))
                return null;

            var parts = new List<string> { branch };
            if (modified > 0)
                parts.Add($"{modified} modified");
            if (untracked > 0)
                parts.Add($"{untracked} untracked");
            if (parts.Count == 1)
                parts.Add("clean");

            return string.Join(" · ", parts);
        }

        private static string ParseBranch(string branchLine)
        {
            var branch = branchLine.Substring(3);
            var separatorIndex = branch.IndexOf("...", StringComparison.Ordinal);
            if (separatorIndex >= 0)
                branch = branch.Substring(0, separatorIndex);

            return branch.Trim();
        }

        private record CacheEntry(DateTimeOffset CreatedAt, string Status);
    }
}
