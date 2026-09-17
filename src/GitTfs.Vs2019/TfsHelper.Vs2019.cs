using GitTfs.Core;
using GitTfs.VsCommon;
using GitTfs;

using Microsoft.Extensions.DependencyInjection;

namespace GitTfs.Vs2019
{
    public class TfsHelper : TfsHelperVS2017Base
    {
        public TfsHelper(TfsApiBridge bridge, IServiceProvider services, Janitor janitor, ConfigProperties properties)
            : base(bridge, services, janitor, properties, 16)
        {
        }
    }
}
