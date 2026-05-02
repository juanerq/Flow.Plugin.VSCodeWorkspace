HOME=/tmp dotnet restore Flow.Plugin.VSCodeWorkspaces.sln -p:EnableWindowsTargeting=true --packages /tmp/flow-vscodeworkspace-nuget-packages --configfile /tmp/flow-vscodeworkspace-nuget.config

HOME=/tmp dotnet build Flow.Plugin.VSCodeWorkspaces.sln --configuration Debug --no-restore -p:EnableWindowsTargeting=true -p:RestorePackagesPath=/tmp/flow-vscodeworkspace-nuget-packages
