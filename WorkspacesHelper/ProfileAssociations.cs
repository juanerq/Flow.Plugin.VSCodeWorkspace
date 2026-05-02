using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Flow.Plugin.VSCodeWorkspaces.WorkspacesHelper
{
    public class ProfileAssociations
    {
        [JsonPropertyName("workspaces")]
        public Dictionary<string, string> Workspaces { get; set; }
    }
}
