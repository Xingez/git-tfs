
namespace GitTfs.Test.Util
{
    using global::GitTfs.Util;
    [TestClass]
    public class CamelCaseToDelimitedStringConverterTests
    {
        [TestMethod]
        [DataRow("Code Reviewer", "-", "code-reviewer")]
        [DataRow(" Code Reviewer", "-", "code-reviewer")]
        [DataRow("Code Reviewer ", "-", "code-reviewer")]
        [DataRow(" Code Reviewer ", "-", "code-reviewer")]
        [DataRow("Code  Reviewer", "-", "code-reviewer")]
        [DataRow("CodeReviewer", "-", "code-reviewer")]
        [DataRow("Jira Issue ID", "-", "jira-issue-id")]
        [DataRow("Jira Issue Id", "-", "jira-issue-id")]
        [DataRow("Some IPAddress", "-", "some-ip-address")]
        [DataRow("SomeIPAddress", "-", "some-ip-address")]
        [DataRow("JustAName", "-", "just-a-name")]
        [DataRow("AnOtherDelimiter", "#", "an#other#delimiter")]
        public void ReturnsExpectedValue(string stringWithCamelCase, string delimiter, string expected)
        {
            var actual = CamelCaseToDelimitedStringConverter.Convert(stringWithCamelCase, delimiter);
            Assert.Equal(expected, actual);
        }
    }
}
