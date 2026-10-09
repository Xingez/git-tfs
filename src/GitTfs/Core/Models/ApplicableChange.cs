namespace GitTfs.Util
{
    using LibGit2Sharp;

    public readonly record struct ApplicableChange(ChangeType Type, string GitPath, Mode Mode)
    {
        public static ApplicableChange Update(string path, Mode mode = Mode.NonExecutableFile)
            => new(ChangeType.Update, path, mode);

        public static ApplicableChange Delete(string path)
            => new(ChangeType.Delete, path, default);

        public static ApplicableChange Ignore(string path)
            => new(ChangeType.Ignore, path, default);
    }
}
