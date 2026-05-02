using System.Text.Json.Serialization;

namespace Flow.Plugin.VSCodeWorkspaces.WorkspacesHelper
{
    public class UserDataProfile
    {
        [JsonPropertyName("location")]
        public string Location { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }
}
