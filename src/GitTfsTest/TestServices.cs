using GitTfs.Core.Changes.Git;
using GitTfs.Core;
using GitTfs.Core.TfsInterop;
using GitTfs.Util;
using GitTfs.VsFake;
using Microsoft.Extensions.DependencyInjection;

namespace GitTfs.Test
{
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
