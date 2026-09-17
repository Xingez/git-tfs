using GitTfs.Core;
using GitTfs.VsFake;
using Microsoft.Extensions.DependencyInjection;

namespace GitTfs
{
    internal class TfsPlugin : Core.TfsInterop.TfsPlugin
    {
        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.AddSingleton(_ => Script.Load(ScriptPath));
        }

        public override bool IsViable() => ScriptPath.Try(File.Exists);

        internal static string ScriptPath => Environment.GetEnvironmentVariable(Script.EnvVar);
    }
}
