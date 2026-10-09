namespace GitTfs.Test.Core
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Assert = GitTfs.Test.TestAssert;
    using GitTfs.Core;
    using System;
    using System.IO;

    [TestClass]
    [DoNotParallelize]
    public sealed class GitTfsSettingsTests
    {
        [TestMethod]
        public void EnvironmentSettingsOverrideAppSettings()
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), "git-tfs-settings-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(settingsDirectory);
            var settingsPath = Path.Combine(settingsDirectory, "appsettings.json");
            File.WriteAllText(settingsPath, """
                {
                  "TargetServer": "https://config.example.com",
                  "api-version": "7.0",
                  "pat": "config-pat",
                  "debug": false,
                  "proxy": null
                }
                """);

            var names = new[]
            {
                "GIT_TFS_APPSETTINGS",
                "GIT_TFS_TARGET_SERVER",
                "GIT_TFS_API_VERSION",
                "GIT_TFS_PAT",
                "GIT_TFS_BATCH_SIZE",
                "GIT_TFS_DEBUG",
                "GIT_TFS_PROXY",
                "GIT_TFS_HTTP_CAPTURE"
            };
            var previousValues = new string[names.Length];
            for (var index = 0; index < names.Length; index++)
                previousValues[index] = Environment.GetEnvironmentVariable(names[index]);

            try
            {
                Environment.SetEnvironmentVariable("GIT_TFS_APPSETTINGS", settingsPath);
                Environment.SetEnvironmentVariable("GIT_TFS_TARGET_SERVER", "https://env.example.com/");
                Environment.SetEnvironmentVariable("GIT_TFS_API_VERSION", "7.1");
                Environment.SetEnvironmentVariable("GIT_TFS_PAT", "env-pat");
                Environment.SetEnvironmentVariable("GIT_TFS_BATCH_SIZE", "5");
                Environment.SetEnvironmentVariable("GIT_TFS_DEBUG", "true");
                Environment.SetEnvironmentVariable("GIT_TFS_PROXY", "none");
                Environment.SetEnvironmentVariable("GIT_TFS_HTTP_CAPTURE", settingsDirectory);

                var settings = GitTfsSettings.Load();

                Assert.Equal("https://env.example.com", settings.TargetServer);
                Assert.Equal("7.1", settings.ApiVersion);
                Assert.Equal("env-pat", settings.Pat);
                Assert.Equal(5, settings.BatchSize);
                Assert.True(settings.Debug);
                Assert.Equal("none", settings.Proxy);
                Assert.Equal(settingsDirectory, settings.HttpCaptureDirectory);
            }
            finally
            {
                for (var index = 0; index < names.Length; index++)
                    Environment.SetEnvironmentVariable(names[index], previousValues[index]);
                Directory.Delete(settingsDirectory, recursive: true);
            }
        }
    }
}
