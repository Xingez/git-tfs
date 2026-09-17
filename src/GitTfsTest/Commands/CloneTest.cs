using GitTfs.Commands;

namespace GitTfs.Test.Commands
{
    [TestClass]
    public class CloneTest : BaseTest
    {
        [TestMethod]
        [DataRow("-u=login", "--username=xxx")]
        [DataRow("-u login", "--username=xxx")]
        [DataRow("-u  login", "--username=xxx")]
        [DataRow("--username=login", "--username=xxx")]
        [DataRow("--username login", "--username=xxx")]
        [DataRow("--username  login", "--username=xxx")]
        [DataRow("/u=login", "--username=xxx")]
        [DataRow("/u login", "--username=xxx")]
        [DataRow("/u  login", "--username=xxx")]
        [DataRow("/username=login", "--username=xxx")]
        [DataRow("/username login", "--username=xxx")]
        [DataRow("/username  login", "--username=xxx")]

        [DataRow("-p=mypassword", "--password=xxx")]
        [DataRow("-p mypassword", "--password=xxx")]
        [DataRow("-p  mypassword", "--password=xxx")]
        [DataRow("--password=mypassword", "--password=xxx")]
        [DataRow("--password mypassword", "--password=xxx")]
        [DataRow("--password  mypassword", "--password=xxx")]
        [DataRow("/p=mypassword", "--password=xxx")]
        [DataRow("/p mypassword", "--password=xxx")]
        [DataRow("/p  mypassword", "--password=xxx")]
        [DataRow("/password=mypassword", "--password=xxx")]
        [DataRow("/password mypassword", "--password=xxx")]
        [DataRow("/password  mypassword", "--password=xxx")]

        [DataRow("git tfs clone https://tfs/tfs $/repo/branch . --branches=all --username=me --password=ExtraHardPassword",
            "git tfs clone https://tfs/tfs $/repo/branch . --branches=all --username=xxx --password=xxx")]
        [DataRow("git tfs clone https://tfs/tfs $/repo/branch . --username me --password ExtraHardPassword --branches=all",
            "git tfs clone https://tfs/tfs $/repo/branch . --username=xxx --password=xxx --branches=all")]
        [DataRow("git tfs clone --username spraints --password SECRETOMG https://topsecret.com/tfs $/reallysupersecret",
            "git tfs clone --username=xxx --password=xxx https://topsecret.com/tfs $/reallysupersecret")]
        public void ShouldEncodeUserCredentialsInTheCommandLine(string cmd, string output) => Assert.Equal(output, Init.HideUserCredentials(cmd));
    }
}
