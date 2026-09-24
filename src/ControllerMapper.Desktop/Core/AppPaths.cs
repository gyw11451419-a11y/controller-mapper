using System.IO;

namespace ControllerMapper.Desktop.Core;

public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            var overrideDirectory = Environment.GetEnvironmentVariable("CONTROLLER_MAPPER_DATA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(overrideDirectory) && Path.IsPathFullyQualified(overrideDirectory))
                return Path.GetFullPath(overrideDirectory);
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ControllerMapper");
        }
    }
}
