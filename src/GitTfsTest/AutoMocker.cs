
namespace GitTfs.Test
{
    using global::GitTfs;
    using global::GitTfs.Util;
    using global::Microsoft.Extensions.DependencyInjection;
    using global::Moq;
    /// <summary>
    /// Small Moq-backed constructor injector used by unit tests. It keeps the
    /// tests independent of the application's runtime container.
    /// </summary>
    public sealed class MoqAutoMocker<T> where T : class
    {
        private readonly Dictionary<Type, object> objectsField = new();
        private T classUnderTestField;

        public MoqAutoMocker()
        {
            Container = new TestServiceProvider(this);
        }

        public TestServiceProvider Container { get; }

        public T ClassUnderTest => classUnderTestField ??= (T)ActivatorUtilities.CreateInstance(Container, typeof(T));

        public TService Get<TService>() => (TService)Get(typeof(TService));

        public void Inject<TService>(TService service)
        {
            var serviceType = typeof(TService);
            if (service is Mock mock)
            {
                var mockedType = service.GetType().GetGenericArguments().FirstOrDefault() ?? typeof(object);
                var objectProperty = GetObjectProperty(mock.GetType(), mockedType);
                var mockedObject = objectProperty.GetValue(mock);
                objectsField[mockedType] = mockedObject;
                objectsField[serviceType] = service;
                return;
            }

            if (service is not null)
            {
                objectsField[service.GetType()] = service;
                objectsField[serviceType] = service;
            }
        }

        public void RegisterCommand(string name, GitTfsCommand command)
        {
            var catalog = Get<ServiceCatalog>();
            catalog.AddCommand(name, command.GetType());
            objectsField[command.GetType()] = command;
        }

        public void MockObjectFactory()
        {
        }

        private object Get(Type serviceType)
        {
            if (serviceType == typeof(IServiceProvider))
                return Container;
            if (serviceType == typeof(ServiceCatalog))
                return GetCatalog();
            if (serviceType == typeof(GitTfsCommandFactory))
            {
                var factory = new GitTfsCommandFactory(Container, GetCatalog());
                objectsField[serviceType] = factory;
                return factory;
            }
            if (serviceType == typeof(T) && classUnderTestField != null)
                return classUnderTestField;
            if (objectsField.TryGetValue(serviceType, out var existing))
                return existing;

            object value;
            try
            {
                var mockType = typeof(Mock<>).MakeGenericType(serviceType);
                var mock = (Mock)Activator.CreateInstance(mockType, MockBehavior.Loose);
                mock.CallBase = true;
                value = GetObjectProperty(mockType, serviceType).GetValue(mock);
            }
            catch (Exception) when (serviceType.IsClass && !serviceType.IsAbstract)
            {
                value = ActivatorUtilities.CreateInstance(Container, serviceType);
            }

            objectsField[serviceType] = value;
            return value;
        }

        private static System.Reflection.PropertyInfo GetObjectProperty(Type mockType, Type mockedType) =>
            mockType.GetProperties()
                .First(property => property.Name == nameof(Mock<Object>.Object) &&
                                   property.GetIndexParameters().Length == 0 &&
                                   property.PropertyType == mockedType);

        private ServiceCatalog GetCatalog()
        {
            if (!objectsField.TryGetValue(typeof(ServiceCatalog), out var catalog))
            {
                catalog = new ServiceCatalog();
                objectsField[typeof(ServiceCatalog)] = catalog;
            }
            return (ServiceCatalog)catalog;
        }

        public sealed class TestServiceProvider : IServiceProvider
        {
            private readonly MoqAutoMocker<T> ownerField;

            internal TestServiceProvider(MoqAutoMocker<T> owner) => ownerField = owner;

            public object GetService(Type serviceType) => ownerField.GetForProvider(serviceType);
        }

        private object GetForProvider(Type serviceType)
        {
            if (serviceType == typeof(IServiceProvider))
                return Container;
            if (serviceType == typeof(ServiceCatalog))
                return GetCatalog();
            if (serviceType == typeof(T) && classUnderTestField != null)
                return classUnderTestField;
            return Get(serviceType);
        }
    }
}
