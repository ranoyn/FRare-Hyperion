using System.Reflection;

[assembly: AssemblyTitle(FreeNormalFinder.AppInfo.DisplayName)]
[assembly: AssemblyProduct(FreeNormalFinder.AppInfo.ProductName)]
[assembly: AssemblyDescription("電波人間のRPG FREE!向けの非公式レアキャ検索・出生解析・ドーピングメモツール")]
[assembly: AssemblyVersion(FreeNormalFinder.AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(FreeNormalFinder.AppInfo.Version + ".0")]
[assembly: AssemblyInformationalVersion(FreeNormalFinder.AppInfo.Version)]

namespace FreeNormalFinder {
public static class AppInfo {
    public const string ProductName = "旧F! レアキャHyperion";
    public const string Version = "1.0.0";
    public const string DisplayName = ProductName + " v" + Version;
}
}
