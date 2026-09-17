namespace GitTfs.Util
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class PluggableAttribute : Attribute
    {
        public PluggableAttribute(string name) => Name = name;

        public string Name { get; }
    }
}
