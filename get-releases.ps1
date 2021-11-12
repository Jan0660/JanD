function Build(){
    param(
        [bool] $NativeAOT,
        [string] $Runtime = $null,
        [bool] $SelfContained
    )
    $name = "JanD"
    $cmd = "dotnet publish JanD -c release"
    if($Runtime -ne $null){
        $cmd += " -r $Runtime"
        $name += "-$Runtime"
    }
    if($NativeAOT){
        $name += "-nativeaot"
    }
    if($NativeAOT -eq $false){
        $cmd += " -p:NoNativeAOTPublish=no"
    }
    if($NativeAOT -eq $false -and $SelfContained -eq $true){
        $cmd += " -p:PublishSingleFile=true --self-contained -p:PublishTrimmed=true";
        $name += "-contained"
    }
    if($NativeAOT -eq $false -and $SelfContained -eq $false){
        $cmd += " -p:PublishSingleFile=true --no-self-contained -p:PublishTrimmed=false";
        $name += "-fxdependent"
    }
    $cmd += " -o JanD/bin/release/$name"
    Write-Output "$($name): $cmd"
    Invoke-Expression $cmd
}
function BuildsFor(){
    param(
        [string]$Runtime
    )
    Build -NativeAot $false -Runtime $Runtime -SelfContained $false
    Build -NativeAot $false -Runtime $Runtime -SelfContained $true
}
Remove-Item -Force -Recurse ./JanD/bin/release
Remove-Item -Force -Recurse ./JanD/bin/release-exec

BuildsFor win-x64
BuildsFor linux-x64
BuildsFor linux-musl-x64
BuildsFor linux-arm
BuildsFor linux-arm64
BuildsFor osx-x64
BuildsFor osx-arm64

mkdir ./JanD/bin/release-exec

foreach($dir in (ls ./JanD/bin/release)){
    if($dir.Name.StartsWith("JanD-")){
        echo $dir
        if($dir.Name.Contains("-osx-") -or $dir.Name.Contains("-linux-")){
            # osx and linux
            mv "$($dir.FullName)/JanD" "./JanD/bin/release-exec/$($dir.Name)"
            if($dir.Name.Contains("-contained")){
                gzip "./JanD/bin/release-exec/$($dir.Name)"
            }
        }
        else{
            # windows
            mv "$($dir.FullName)/JanD.exe" "./JanD/bin/release-exec/$($dir.Name).exe"
        }
    }
}
