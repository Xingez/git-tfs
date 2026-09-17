
namespace GitTfs.VsCommon
{
    using global::System.Collections;

    using global::GitTfs.Core.TfsInterop;

    using global::GitTfs.Util;
    public class TfsApiBridge
    {
        private readonly IServiceProvider servicesField;

        public TfsApiBridge(IServiceProvider services)
        {
            servicesField = services;
        }

        public TWrapper Wrap<TWrapper, TWrapped>(TWrapped wrapped) where TWrapper : class =>
            wrapped == null ? null : servicesField.CreateInstance<TWrapper>(this, wrapped);

        public TWrapper[] Wrap<TWrapper, TWrapped>(IEnumerable wrapped) where TWrapper : class => wrapped == null ? null : wrapped.OfType<TWrapped>().Select(x => Wrap<TWrapper, TWrapped>(x)).ToArray();

        public TTfs Unwrap<TTfs>(object wrapper) where TTfs : class => wrapper == null ? null : (wrapper is TTfs ? (TTfs)wrapper : ((WrapperFor<TTfs>)wrapper).Unwrap());

        public TTfs[] Unwrap<TTfs>(IEnumerable wrappers) where TTfs : class => wrappers == null ? null : wrappers.Cast<object>().Select(x => Unwrap<TTfs>(x)).ToArray();

        public TEnum Convert<TEnum>(object originalEnum) => (TEnum)originalEnum;
    }
}
