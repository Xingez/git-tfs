
namespace GitTfs.Core.TfsInterop
{
    public class WrapperFor<TFS_TYPE>
    {
        private readonly TFS_TYPE wrappedField;

        public WrapperFor(TFS_TYPE wrapped)
        {
            wrappedField = wrapped;
        }

        public TFS_TYPE Unwrap() => wrappedField;

        public static TFS_TYPE Unwrap(object wrapper) => ((WrapperFor<TFS_TYPE>)wrapper).Unwrap();
    }
}
