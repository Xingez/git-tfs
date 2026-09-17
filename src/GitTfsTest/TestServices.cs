
namespace GitTfs.Test
{
    using global::GitTfs.Core.Changes.Git;
    using global::GitTfs.Core;
    using global::GitTfs.Core.TfsInterop;
    using global::GitTfs.Util;
    using global::GitTfs.VsFake;
    using global::Microsoft.Extensions.DependencyInjection;
    internal static class TestServices
    {
        public static IServiceProvider Create()
        {
            var services = new ServiceCollection();
            var catalog = new ServiceCatalog();
            services.AddSingleton(catalog);
            services.AddGitTfsServices(catalog, typeof(Program).Assembly, typeof(TfsHelper).Assembly);
            services.AddTransient<IGitHelpers, GitHelpers>();
            Program.AddGitChangeTypes(catalog);
            services.AddSingleton<Script>(_ => new Script());
            services.AddSingleton<ITfsHelper, TfsHelper>();
            return services.BuildServiceProvider();
        }
    }
}
