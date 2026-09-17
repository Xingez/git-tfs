
namespace GitTfs.Test.Util
{
    using global::GitTfs.Commands;
    using global::GitTfs.Util;
    using global::Moq;
    using global::GitTfs.Test;
    [TestClass]
    public class GitTfsCommandRunnerTests : BaseTest
    {
        #region Base implementation of GitTfsCommand, for tests

        public class TestCommandBase : GitTfsCommand
        {
            internal enum Form { List, Split }
            internal class Invocation
            {
                public Form Form { get; private set; }
                public IList<string> Args { get; private set; }
                private Invocation() { }
                public static Invocation List(IList<string> args) => new Invocation { Form = Form.List, Args = args };
                public static Invocation Split(params string[] args) => new Invocation { Form = Form.Split, Args = args };
            }
            internal List<Invocation> Calls = new List<Invocation>();

            public OptionSet OptionSet { get; set; }
        }
        #endregion

        private readonly MoqAutoMocker<GitTfsCommandRunner> mocksField;

        public GitTfsCommandRunnerTests()
        {
            mocksField = new MoqAutoMocker<GitTfsCommandRunner>();
        }

        private IList<string> Args(params string[] args) => args;

        public class UsesList : TestCommandBase
        {
            public int Run(IList<string> args)
            {
                Calls.Add(Invocation.List(args));
                return 99;
            }
        }

        [TestMethod]
        public void ReturnsCommandReturnValue() => Assert.Equal(99, mocksField.ClassUnderTest.Run(new UsesList(), Args()));

        [TestMethod]
        public void CallsListWithZeroArgs()
        {
            var command = new UsesList();
            var args = Args();
            mocksField.ClassUnderTest.Run(command, args);
            Assert.Single(command.Calls);
            Assert.Equal(TestCommandBase.Form.List, command.Calls[0].Form);
            Assert.Same(args, command.Calls[0].Args);
        }

        public class UsesOverloads : TestCommandBase
        {
            public int Run(string a)
            {
                Calls.Add(Invocation.Split(a));
                return 89;
            }
            public int Run(string a, string b)
            {
                Calls.Add(Invocation.Split(a, b));
                return 88;
            }
        }

        [TestMethod]
        public void CallsOverloadWithOneArg()
        {
            var command = new UsesOverloads();
            var args = Args("a");
            mocksField.ClassUnderTest.Run(command, args);
            Assert.Single(command.Calls);
            Assert.Equal(TestCommandBase.Form.Split, command.Calls[0].Form);
            Assert.Equal(args, command.Calls[0].Args);
        }

        [TestMethod]
        public void CallsOverloadWithTwoArgs()
        {
            var command = new UsesOverloads();
            var args = Args("a", "b");
            mocksField.ClassUnderTest.Run(command, args);
            Assert.Single(command.Calls);
            Assert.Equal(TestCommandBase.Form.Split, command.Calls[0].Form);
            Assert.Equal(args, command.Calls[0].Args);
        }

        [TestMethod]
        public void ReturnsHelpForTooFewArgs()
        {
            Mock.Get(mocksField.Get<IHelpHelper>()).Setup(x => x.ShowHelpForInvalidArguments(It.IsAny<GitTfsCommand>())).Returns(33);
            Assert.Equal(33, mocksField.ClassUnderTest.Run(new UsesOverloads(), Args()));
        }

        [TestMethod]
        public void ReturnsHelpForTooManyArgs()
        {
            Mock.Get(mocksField.Get<IHelpHelper>()).Setup(x => x.ShowHelpForInvalidArguments(It.IsAny<GitTfsCommand>())).Returns(33);
            Assert.Equal(33, mocksField.ClassUnderTest.Run(new UsesOverloads(), Args("a", "b", "c")));
        }

        public class UsesOverloadsOrDefault : TestCommandBase
        {
            public int Run()
            {
                Calls.Add(Invocation.Split());
                return 79;
            }
            public int Run(string a)
            {
                Calls.Add(Invocation.Split(a));
                return 78;
            }
            public int Run(IList<string> args)
            {
                Calls.Add(Invocation.List(args));
                return 77;
            }
        }

        [TestMethod]
        public void CallsOverloadOrDefaultWithZeroArgs()
        {
            var command = new UsesOverloadsOrDefault();
            var args = Args();
            mocksField.ClassUnderTest.Run(command, args);
            Assert.Single(command.Calls);
            Assert.Equal(TestCommandBase.Form.Split, command.Calls[0].Form);
            Assert.Equal(args, command.Calls[0].Args);
        }

        [TestMethod]
        public void CallsOverloadOrDefaultWithOneArg()
        {
            var command = new UsesOverloadsOrDefault();
            var args = Args("a");
            mocksField.ClassUnderTest.Run(command, args);
            Assert.Single(command.Calls);
            Assert.Equal(TestCommandBase.Form.Split, command.Calls[0].Form);
            Assert.Equal(args, command.Calls[0].Args);
        }

        [TestMethod]
        public void CallsOverloadOrDefaultWithTwoArgs()
        {
            var command = new UsesOverloadsOrDefault();
            var args = Args("a", "b");
            mocksField.ClassUnderTest.Run(command, args);
            Assert.Single(command.Calls);
            Assert.Equal(TestCommandBase.Form.List, command.Calls[0].Form);
            Assert.Same(args, command.Calls[0].Args);
        }
    }
}
