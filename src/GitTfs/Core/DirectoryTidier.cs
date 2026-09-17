
namespace GitTfs.Core
{
    using global::GitTfs.Core.TfsInterop;
    public class DirectoryTidier : ITfsWorkspaceModifier, IDisposable
    {
        private enum FileOperation
        {
            Add,
            Remove,
            RenameFrom,
            RenameTo,
            Edit,
            EditAndRenameFrom,
        }

        private readonly ITfsWorkspaceModifier workspaceField;
        private readonly Func<IEnumerable<TfsTreeEntry>> getInitialTfsTreeField;
        private List<string> filesInTfsField;
        private readonly Dictionary<string, FileOperation> fileOperationsField;
        private bool disposedField;

        public DirectoryTidier(ITfsWorkspaceModifier workspace, Func<IEnumerable<TfsTreeEntry>> getInitialTfsTree)
        {
            workspaceField = workspace;
            getInitialTfsTreeField = getInitialTfsTree;
            fileOperationsField = new Dictionary<string, FileOperation>(StringComparer.InvariantCultureIgnoreCase);
        }

        public void Dispose()
        {
            if (disposedField)
                return;
            disposedField = true;

            var candidateDirectories = CalculateCandidateDirectories();
            if (!candidateDirectories.Any())
                return;

            filesInTfsField = getInitialTfsTreeField().Where(entry => entry.Item.ItemType == TfsItemType.File).Select(entry => entry.FullName.ToLowerInvariant()).ToList();

            foreach (var fileAndOperation in fileOperationsField)
            {
                if (fileAndOperation.Value == FileOperation.Remove)
                    filesInTfsField.Remove(fileAndOperation.Key.ToLowerInvariant());
                else if (fileAndOperation.Value == FileOperation.Add || fileAndOperation.Value == FileOperation.RenameTo)
                    filesInTfsField.Add(fileAndOperation.Key.ToLowerInvariant());
            }

            var deletedDirs = new List<string>();
            foreach (var dir in candidateDirectories.OrderBy(d => d, StringComparer.InvariantCultureIgnoreCase))
            {
                DeleteEmptyDir(dir, deletedDirs);
            }
        }

        private void DeleteEmptyDir(string dirName, List<string> deletedDirs)
        {
            if (dirName == null)
                return;
            var downcasedDirName = dirName.ToLowerInvariant();
            if (!HasEntryInDir(downcasedDirName))
            {
                DeleteEmptyDir(GetDirectoryName(dirName), deletedDirs);
                if (!IsDirDeletedAlready(downcasedDirName, deletedDirs))
                {
                    workspaceField.Delete(dirName);
                    deletedDirs.Add(downcasedDirName);
                }
            }
        }

        private bool IsDirDeletedAlready(string downcasedDirName, IEnumerable<string> deletedDirs) => deletedDirs.Any(t => downcasedDirName.StartsWith(t + "/") || t == downcasedDirName);

        private string GetDirectoryName(string path)
        {
            var separatorIndex = path.LastIndexOf('/');
            if (separatorIndex == -1)
                return null;
            return path.Substring(0, separatorIndex);
        }

        private bool HasEntryInDir(string dirName)
        {
            dirName = dirName + "/";
            return filesInTfsField.Any(file => file.StartsWith(dirName));
        }

        string ITfsWorkspaceModifier.GetLocalPath(string path) => workspaceField.GetLocalPath(path);

        void ITfsWorkspaceModifier.Add(string path)
        {
            workspaceField.Add(path);
            fileOperationsField.Add(path, FileOperation.Add);
        }

        void ITfsWorkspaceModifier.Edit(string path)
        {
            workspaceField.Edit(path);
            fileOperationsField.Add(path, FileOperation.Edit);
        }

        void ITfsWorkspaceModifier.Delete(string path)
        {
            workspaceField.Delete(path);
            fileOperationsField.Add(path, FileOperation.Remove);
        }

        void ITfsWorkspaceModifier.Rename(string pathFrom, string pathTo, string score)
        {
            workspaceField.Rename(pathFrom, pathTo, score);

            FileOperation pathFromOperation;
            if (fileOperationsField.TryGetValue(pathFrom, out pathFromOperation) &&
                pathFromOperation == FileOperation.Edit)
            {
                fileOperationsField[pathFrom] = FileOperation.EditAndRenameFrom;
            }
            else
            {
                fileOperationsField.Add(pathFrom, FileOperation.RenameFrom);
            }
            fileOperationsField.Add(pathTo, FileOperation.RenameTo);
        }

        private IEnumerable<string> CalculateCandidateDirectories()
        {
            var directoriesWithRemovedFiles = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

            foreach (var removedFilePath in fileOperationsField.Where(x => x.Value == FileOperation.Remove).Select(x => x.Key))
            {
                var directory = GetDirectoryName(removedFilePath);
                if (directory != null)
                {
                    directoriesWithRemovedFiles.Add(directory);
                }
            }

            return directoriesWithRemovedFiles;
        }
    }
}
