
namespace GitTfs
{
    using global::GitTfs.Core;
    using global::GitTfs.VsFake;
    using global::Microsoft.Extensions.DependencyInjection;
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
