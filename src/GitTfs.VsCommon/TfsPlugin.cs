
namespace GitTfs
{
    using global::Microsoft.Extensions.DependencyInjection;
    internal class TfsPlugin : Core.TfsInterop.TfsPlugin
    {
        public override IEnumerable<System.Reflection.Assembly> GetServiceAssemblies() => base.GetServiceAssemblies();

        public override void ConfigureServices(IServiceCollection services) => base.ConfigureServices(services);

        public override bool IsViable() => null != typeof(Microsoft.TeamFoundation.Client.TfsTeamProjectCollection).Assembly;
    }
}
