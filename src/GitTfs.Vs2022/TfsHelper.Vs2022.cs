using GitTfs.VsCommon;
using GitTfs.Core;
using GitTfs;

using Microsoft.Extensions.DependencyInjection;

namespace GitTfs.Vs2022
{
    public class TfsHelper : TfsHelperVS2017Base
    {
        public TfsHelper(TfsApiBridge bridge, IServiceProvider services, Janitor janitor, ConfigProperties properties)
            : base(bridge, services, janitor, properties, 17)
        {
        }
    }
}
