namespace GitTfs.Test.Core
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Assert = global::GitTfs.Test.TestAssert;
    using global::GitTfs.Core;
    using global::System;
    using global::System.IO;

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
                  "Username": "config-user",
                  "Password": "config-password",
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
                "GIT_TFS_USERNAME",
                "GIT_TFS_PASSWORD",
                "GIT_TFS_PAT",
                "GIT_TFS_BATCH_SIZE",
                "GIT_TFS_DEBUG",
                "GIT_TFS_PROXY"
            };
            var previousValues = new string[names.Length];
            for (var index = 0; index < names.Length; index++)
                previousValues[index] = Environment.GetEnvironmentVariable(names[index]);

            try
            {
                Environment.SetEnvironmentVariable("GIT_TFS_APPSETTINGS", settingsPath);
                Environment.SetEnvironmentVariable("GIT_TFS_TARGET_SERVER", "https://env.example.com/");
                Environment.SetEnvironmentVariable("GIT_TFS_API_VERSION", "7.1");
                Environment.SetEnvironmentVariable("GIT_TFS_USERNAME", "env-user");
                Environment.SetEnvironmentVariable("GIT_TFS_PASSWORD", "env-password");
                Environment.SetEnvironmentVariable("GIT_TFS_PAT", "env-pat");
                Environment.SetEnvironmentVariable("GIT_TFS_BATCH_SIZE", "5");
                Environment.SetEnvironmentVariable("GIT_TFS_DEBUG", "true");
                Environment.SetEnvironmentVariable("GIT_TFS_PROXY", "none");

                var settings = GitTfsSettings.Load();

                Assert.Equal("https://env.example.com", settings.TargetServer);
                Assert.Equal("7.1", settings.ApiVersion);
                Assert.Equal("env-user", settings.Username);
                Assert.Equal("env-password", settings.Password);
                Assert.Equal("env-pat", settings.Pat);
                Assert.Equal(5, settings.BatchSize);
                Assert.True(settings.Debug);
                Assert.Equal("none", settings.Proxy);
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
