plugins_dir := "/mnt/c/Program Files (x86)/steam/steamapps/common/OPERATOR/BepInEx/plugins"

default: deploy

build:
    dotnet build OPERATOR_Mods.slnx --configuration Release

deploy: build
    cp OPERATOR.Binoculars/bin/Release/net6.0/OPERATOR.Binoculars.dll "{{plugins_dir}}/"
    cp OPERATOR.Common/bin/Release/net6.0/OPERATOR.Common.dll "{{plugins_dir}}/"
    cp OPERATOR.Debug/bin/Release/net6.0/OPERATOR.Debug.dll "{{plugins_dir}}/"
    cp OPERATOR.MagCheck/bin/Release/net6.0/OPERATOR.MagCheck.dll "{{plugins_dir}}/"
    cp OPERATOR.PlayerLoadouts/bin/Release/net6.0/OPERATOR.PlayerLoadouts.dll "{{plugins_dir}}/"
    cp OPERATOR.UsableVlite/bin/Release/net6.0/OPERATOR.UsableVlite.dll "{{plugins_dir}}/"
    # MessagePack 2.5.x deps for OPERATOR.Common.Networking (Immutable is in-box on .NET 6, not shipped)
    cp OPERATOR.Common/bin/Release/net6.0/MessagePack.dll "{{plugins_dir}}/"
    cp OPERATOR.Common/bin/Release/net6.0/MessagePack.Annotations.dll "{{plugins_dir}}/"
    cp OPERATOR.Common/bin/Release/net6.0/Microsoft.NET.StringTools.dll "{{plugins_dir}}/"

clean:
    dotnet clean OPERATOR_Mods.slnx
