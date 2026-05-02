// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Flow.Plugin.VSCodeWorkspaces.WorkspacesHelper
{
    public class VSCodeStorageFile
    {
        [JsonPropertyName("openedPathsList")]
        public OpenedPathsList OpenedPathsList { get; set; }

        [JsonPropertyName("profileAssociations")]
        public ProfileAssociations ProfileAssociations { get; set; }

        [JsonPropertyName("userDataProfiles")]
        public List<UserDataProfile> UserDataProfiles { get; set; }
    }
}
