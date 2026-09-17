
namespace GitTfs.Core
{
    using global::LibGit2Sharp;
    public class GitTreeBuilder : IGitTreeBuilder
    {
        private readonly TreeDefinition treeDefinitionField;
        private readonly ObjectDatabase objectDatabaseField;

        public GitTreeBuilder(ObjectDatabase objectDatabase)
        {
            treeDefinitionField = new TreeDefinition();
            objectDatabaseField = objectDatabase;
        }

        public GitTreeBuilder(ObjectDatabase objectDatabase, Tree tree)
        {
            treeDefinitionField = TreeDefinition.From(tree);
            objectDatabaseField = objectDatabase;
        }

        public void Add(string path, string file, LibGit2Sharp.Mode mode)
            => treeDefinitionField.Add(path, file, mode);

        public void Remove(string path) => treeDefinitionField.Remove(path);

        public Tree GetTree() => objectDatabaseField.CreateTree(treeDefinitionField);
    }
}
