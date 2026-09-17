
namespace GitTfs.Vs2022
{
    using global::GitTfs.VsCommon;
    using global::GitTfs.Core;
    using global::GitTfs;

    using global::Microsoft.Extensions.DependencyInjection;
    public class TfsHelper : TfsHelperVS2022Base
    {
        public TfsHelper(TfsApiBridge bridge, IServiceProvider services, Janitor janitor, ConfigProperties properties)
            : base(bridge, services, janitor, properties, 17)
        {
        }
    }
}
