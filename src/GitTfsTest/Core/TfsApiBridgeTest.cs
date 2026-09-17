
namespace GitTfsTest.Core
{
    using global::GitTfs.Core.TfsInterop;
    using global::GitTfs.Test;
    using global::GitTfs.VsCommon;
    [TestClass]
    public class TfsApiBridgeTest : BaseTest
    {
        private readonly MoqAutoMocker<TfsApiBridge> mocksField;

        public TfsApiBridgeTest()
        {
            mocksField = new MoqAutoMocker<TfsApiBridge>();
            mocksField.MockObjectFactory();
        }

        [TestMethod]
        public void ConvertsEnum() => Assert.Equal(OriginalEnum.Value2, mocksField.ClassUnderTest.Convert<OriginalEnum>(WrappedEnum.Value2));

        [TestMethod]
        public void WrapsAndUnwrapsObject()
        {
            var originalObject = new OriginalType();
            var wrappedObject = mocksField.ClassUnderTest.Wrap<WrapperForOriginalType, OriginalType>(originalObject);
            Assert.Equal(originalObject, mocksField.ClassUnderTest.Unwrap<OriginalType>(wrappedObject));
        }

        [TestMethod]
        public void WrapsObjectWithBridge()
        {
            var originalObject = new OriginalType();
            var wrappedObject = mocksField.ClassUnderTest.Wrap<WrapperForOriginalTypeWithBridge, OriginalType>(originalObject);
            Assert.NotNull(wrappedObject.Bridge);
        }

        [TestMethod]
        public void WrapsAndUnwrapsArray()
        {
            var originalObjects = new[] { new OriginalType() };
            var wrappedObjects = mocksField.ClassUnderTest.Wrap<WrapperForOriginalType, OriginalType>(originalObjects);
            Assert.Single(wrappedObjects);
            Assert.Equal(originalObjects[0], mocksField.ClassUnderTest.Unwrap<OriginalType>(wrappedObjects)[0]);
        }

        [TestMethod]
        public void WrapsNullAsNull()
        {
            OriginalType obj = null;
            Assert.Null(mocksField.ClassUnderTest.Wrap<WrapperForOriginalType, OriginalType>(obj));
        }

        [TestMethod]
        public void WrapsNullArrayAsNull()
        {
            OriginalType[] obj = null;
            Assert.Null(mocksField.ClassUnderTest.Wrap<WrapperForOriginalType, OriginalType>(obj));
        }

        [TestMethod]
        public void UnwrapsNullAsNull()
        {
            WrapperForOriginalType obj = null;
            Assert.Null(mocksField.ClassUnderTest.Unwrap<OriginalType>(obj));
        }

        [TestMethod]
        public void UnwrapsNullArrayAsNull()
        {
            WrapperForOriginalType[] obj = null;
            Assert.Null(mocksField.ClassUnderTest.Unwrap<OriginalType>(obj));
        }

        public class OriginalType
        {
            public static int counter;
            public static object lockObject = new object();
            private readonly int idField;
            public OriginalType()
            {
                lock (lockObject)
                {
                    idField = ++counter;
                }
            }
            public override bool Equals(object obj) => obj is OriginalType && ((OriginalType)obj).idField == idField;
            public override int GetHashCode() => idField;
            public override string ToString() => "OriginalObject:" + idField;
        }
        private interface IOriginalType { }
        public class WrapperForOriginalType : WrapperFor<OriginalType>, IOriginalType
        {
            public WrapperForOriginalType(OriginalType o) : base(o) { }
        }
        public class WrapperForOriginalTypeWithBridge : WrapperFor<OriginalType>, IOriginalType
        {
            public WrapperForOriginalTypeWithBridge(OriginalType o, TfsApiBridge b) : base(o)
            {
                Bridge = b;
            }
            public TfsApiBridge Bridge { get; private set; }
        }

        public enum OriginalEnum
        {
            Value1,
            Value2,
        };

        public enum WrappedEnum
        {
            Value1,
            Value2,
        };
    }
}
