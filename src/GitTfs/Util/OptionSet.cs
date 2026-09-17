
namespace GitTfs.Util;
using global::System.Collections.ObjectModel;
using global::System.ComponentModel;
using global::System.Text;
using global::System.Text.RegularExpressions;

public enum OptionValueType
{
    None,
    Optional,
    Required
}

public delegate void OptionAction<TKey, TValue>(TKey key, TValue value);

public sealed class OptionValueCollection : IList<string>
{
    private readonly List<string> valuesField = new();
    private readonly OptionContext contextField;

    internal OptionValueCollection(OptionContext context) => contextField = context;

    public string this[int index]
    {
        get
        {
            if (contextField.Option == null)
                throw new InvalidOperationException("OptionContext.Option is null.");
            if (index >= contextField.Option.MaxValueCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (contextField.Option.OptionValueType == OptionValueType.Required && index >= valuesField.Count)
                throw new OptionException(
                    $"Missing required value for option '{contextField.OptionName}'.",
                    contextField.OptionName);
            return index >= valuesField.Count ? null : valuesField[index];
        }
        set => valuesField[index] = value;
    }

    public int Count => valuesField.Count;
    public bool IsReadOnly => false;
    public void Add(string item) => valuesField.Add(item);
    public void Clear() => valuesField.Clear();
    public bool Contains(string item) => valuesField.Contains(item);
    public void CopyTo(string[] array, int arrayIndex) => valuesField.CopyTo(array, arrayIndex);
    public IEnumerator<string> GetEnumerator() => valuesField.GetEnumerator();
    public int IndexOf(string item) => valuesField.IndexOf(item);
    public void Insert(int index, string item) => valuesField.Insert(index, item);
    public bool Remove(string item) => valuesField.Remove(item);
    public void RemoveAt(int index) => valuesField.RemoveAt(index);
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public List<string> ToList() => new(valuesField);
    public string[] ToArray() => valuesField.ToArray();
    public override string ToString() => string.Join(", ", valuesField);
}

public sealed class OptionContext
{
    private readonly OptionSet setField;

    public OptionContext(OptionSet set)
    {
        setField = set;
        OptionValues = new OptionValueCollection(this);
    }

    public Option Option { get; set; }
    public string OptionName { get; set; }
    public int OptionIndex { get; set; }
    public OptionSet OptionSet => setField;
    public OptionValueCollection OptionValues { get; }
}

public class OptionException : Exception
{
    public OptionException() { }

    public OptionException(string message, string optionName)
        : base(message) => OptionName = optionName;

    public OptionException(string message, string optionName, Exception innerException)
        : base(message, innerException) => OptionName = optionName;

    public string OptionName { get; }
}

public abstract class Option
{
    private static readonly char[] NameTerminators = { '=', ':' };
    private readonly string[] namesField;
    private readonly string[] valueSeparatorsField;

    protected Option(string prototype, string description, int maxValueCount)
    {
        if (prototype == null)
            throw new ArgumentNullException(nameof(prototype));
        if (prototype.Length == 0)
            throw new ArgumentException("Cannot be the empty string.", nameof(prototype));
        if (maxValueCount < 0)
            throw new ArgumentOutOfRangeException(nameof(maxValueCount));

        Prototype = prototype;
        Description = description;
        MaxValueCount = maxValueCount;
        namesField = prototype.Split('|');
        OptionValueType = ParsePrototype(namesField, maxValueCount, out valueSeparatorsField);

        if (maxValueCount == 0 && OptionValueType != OptionValueType.None)
            throw new ArgumentException("An option without values cannot have a value type.", nameof(maxValueCount));
        if (OptionValueType == OptionValueType.None && maxValueCount > 1)
            throw new ArgumentException("An option without values cannot accept multiple values.", nameof(maxValueCount));
    }

    protected Option(string prototype, string description)
        : this(prototype, description, 1) { }

    public string Prototype { get; }
    public string Description { get; }
    public OptionValueType OptionValueType { get; }
    public int MaxValueCount { get; }

    public string[] GetNames() => (string[])namesField.Clone();
    public string[] GetValueSeparators() => valueSeparatorsField == null ? Array.Empty<string>() : (string[])valueSeparatorsField.Clone();

    internal string[] Names => namesField;
    internal string[] ValueSeparators => valueSeparatorsField;

    internal void Invoke(OptionContext context)
    {
        OnParseComplete(context);
        context.OptionName = null;
        context.Option = null;
        context.OptionValues.Clear();
    }

    protected abstract void OnParseComplete(OptionContext context);

    protected static T Parse<T>(string value, OptionContext context)
    {
        try
        {
            if (value == null)
                return default;
            return (T)TypeDescriptor.GetConverter(typeof(T)).ConvertFromString(value);
        }
        catch (Exception exception)
        {
            throw new OptionException(
                $"Could not convert string `{value}` to type {typeof(T).Name} for option `{context.OptionName}`.",
                context.OptionName,
                exception);
        }
    }

    private static OptionValueType ParsePrototype(string[] names, int maxValueCount, out string[] separators)
    {
        char? valueType = null;
        var parsedSeparators = new List<string>();

        for (var index = 0; index < names.Length; index++)
        {
            var name = names[index];
            if (name.Length == 0)
                throw new ArgumentException("Empty option names are not supported.", "prototype");

            var terminatorIndex = name.IndexOfAny(NameTerminators);
            if (terminatorIndex < 0)
                continue;

            var terminator = name[terminatorIndex];
            names[index] = name.Substring(0, terminatorIndex);
            if (valueType.HasValue && valueType.Value != terminator)
                throw new ArgumentException("Conflicting option types in prototype.", "prototype");
            valueType = terminator;
            AddSeparators(name, terminatorIndex, parsedSeparators);
        }

        if (!valueType.HasValue)
        {
            separators = null;
            return OptionValueType.None;
        }

        if (maxValueCount <= 1 && parsedSeparators.Count > 0)
            throw new ArgumentException("Separators are only supported for options with multiple values.", "prototype");

        if (maxValueCount <= 1)
            separators = null;
        else if (parsedSeparators.Count == 0)
            separators = new[] { ":", "=" };
        else if (parsedSeparators.Count == 1 && parsedSeparators[0].Length == 0)
            separators = null;
        else
            separators = parsedSeparators.ToArray();

        return valueType == '=' ? OptionValueType.Required : OptionValueType.Optional;
    }

    private static void AddSeparators(string name, int terminatorIndex, ICollection<string> separators)
    {
        var start = -1;
        for (var index = terminatorIndex + 1; index < name.Length; index++)
        {
            switch (name[index])
            {
                case '{':
                    if (start >= 0)
                        throw new ArgumentException($"Ill-formed name/value separator in '{name}'.", "prototype");
                    start = index + 1;
                    break;
                case '}':
                    if (start < 0)
                        throw new ArgumentException($"Ill-formed name/value separator in '{name}'.", "prototype");
                    separators.Add(name.Substring(start, index - start));
                    start = -1;
                    break;
                default:
                    if (start < 0)
                        separators.Add(name[index].ToString());
                    break;
            }
        }

        if (start >= 0)
            throw new ArgumentException($"Ill-formed name/value separator in '{name}'.", "prototype");
    }
}

public class OptionSet : Collection<Option>
{
    private readonly Converter<string, string> localizerField;

    public OptionSet()
        : this(value => value) { }

    public OptionSet(Converter<string, string> localizer) => localizerField = localizer ?? throw new ArgumentNullException(nameof(localizer));

    public Converter<string, string> MessageLocalizer => localizerField;

    public new OptionSet Add(Option option)
    {
        if (option == null)
            throw new ArgumentNullException(nameof(option));
        foreach (var name in option.Names)
        {
            if (Contains(name))
                throw new ArgumentException($"An option named '{name}' is already registered.", nameof(option));
        }
        base.Add(option);
        return this;
    }

    public bool Contains(string name) => this.Any(option => option.Names.Contains(name));

    public Option this[string name] => this.First(option => option.Names.Contains(name));

    public OptionSet Add(string prototype, Action<string> action) => Add(prototype, null, action);

    public OptionSet Add(string prototype, string description, Action<string> action)
        => Add(new ActionOption(prototype, description, action));

    public OptionSet Add(string prototype, OptionAction<string, string> action) => Add(prototype, null, action);

    public OptionSet Add(string prototype, string description, OptionAction<string, string> action)
        => Add(new ConvertedActionOption<string, string>(prototype, description, action));

    public OptionSet Add<T>(string prototype, Action<T> action) => Add(prototype, null, action);

    public OptionSet Add<T>(string prototype, string description, Action<T> action)
        => Add(new ConvertedActionOption<T>(prototype, description, action));

    public OptionSet Add<TKey, TValue>(string prototype, OptionAction<TKey, TValue> action)
        => Add(prototype, null, action);

    public OptionSet Add<TKey, TValue>(string prototype, string description, OptionAction<TKey, TValue> action)
        => Add(new ConvertedActionOption<TKey, TValue>(prototype, description, action));

    public List<string> Parse(IEnumerable<string> arguments)
    {
        if (arguments == null)
            throw new ArgumentNullException(nameof(arguments));

        var context = CreateOptionContext();
        context.OptionIndex = -1;
        var unprocessed = new List<string>();
        var processOptions = true;

        foreach (var argument in arguments)
        {
            context.OptionIndex++;
            if (argument == "--")
            {
                processOptions = false;
                continue;
            }

            if (!processOptions)
            {
                unprocessed.Add(argument);
                continue;
            }

            if (context.Option != null)
            {
                ParseValue(argument, context);
                continue;
            }

            if (!Parse(argument, context))
                unprocessed.Add(argument);
        }

        if (context.Option != null)
            context.Option.Invoke(context);

        return unprocessed;
    }

    protected virtual OptionContext CreateOptionContext() => new(this);

    private bool Parse(string argument, OptionContext context)
    {
        var match = Regex.Match(argument ?? string.Empty, "^(?<flag>--|-|/)(?<name>[^:=]+)((?<separator>[:=])(?<value>.*))?$");
        if (!match.Success)
            return false;

        var flag = match.Groups["flag"].Value;
        var name = match.Groups["name"].Value;
        var separator = match.Groups["separator"].Success ? match.Groups["separator"].Value : null;
        var value = match.Groups["value"].Success ? match.Groups["value"].Value : null;

        if (Contains(name))
        {
            var option = this[name];
            context.OptionName = flag + name;
            context.Option = option;
            if (option.OptionValueType == OptionValueType.None)
            {
                context.OptionValues.Add(name);
                option.Invoke(context);
            }
            else
            {
                ParseValue(value, context);
            }
            return true;
        }

        if (ParseBooleanSuffix(argument, name, context))
            return true;

        return ParseBundledValue(flag, name + separator + value, context);
    }

    private void ParseValue(string value, OptionContext context)
    {
        if (value != null)
        {
            var values = context.Option.ValueSeparators == null
                ? new[] { value }
                : value.Split(context.Option.ValueSeparators, StringSplitOptions.None);
            foreach (var item in values)
                context.OptionValues.Add(item);
        }

        if (context.OptionValues.Count == context.Option.MaxValueCount || context.Option.OptionValueType == OptionValueType.Optional)
            context.Option.Invoke(context);
        else if (context.OptionValues.Count > context.Option.MaxValueCount)
            throw new OptionException(
                $"Error: Found {context.OptionValues.Count} option values when expecting {context.Option.MaxValueCount}.",
                context.OptionName);
    }

    private bool ParseBooleanSuffix(string argument, string name, OptionContext context)
    {
        if (name.Length < 2 || (name[name.Length - 1] != '+' && name[name.Length - 1] != '-'))
            return false;

        var baseName = name.Substring(0, name.Length - 1);
        if (!Contains(baseName))
            return false;

        var option = this[baseName];
        context.OptionName = argument;
        context.Option = option;
        context.OptionValues.Add(name[name.Length - 1] == '+' ? argument : null);
        option.Invoke(context);
        return true;
    }

    private bool ParseBundledValue(string flag, string value, OptionContext context)
    {
        if (flag != "-")
            return false;

        for (var index = 0; index < value.Length; index++)
        {
            var name = value[index].ToString();
            if (!Contains(name))
            {
                if (index == 0)
                    return false;
                throw new OptionException($"Cannot bundle unregistered option '-{name}'.", "-" + name);
            }

            var option = this[name];
            var optionName = "-" + name;
            if (option.OptionValueType == OptionValueType.None)
            {
                context.OptionName = optionName;
                context.Option = option;
                context.OptionValues.Add(name);
                option.Invoke(context);
                continue;
            }

            context.OptionName = optionName;
            context.Option = option;
            var inlineValue = value.Substring(index + 1);
            ParseValue(inlineValue.Length == 0 ? null : inlineValue, context);
            return true;
        }

        return true;
    }

    public void WriteOptionDescriptions(TextWriter writer)
    {
        const int optionWidth = 29;
        foreach (var option in this)
        {
            var names = option.Names.Where(name => name != "<>").ToArray();
            if (names.Length == 0)
                continue;

            var prototype = new StringBuilder();
            prototype.Append(names[0].Length == 1 ? "  -" : "      --").Append(names[0]);
            foreach (var name in names.Skip(1))
                prototype.Append(", ").Append(name.Length == 1 ? "-" : "--").Append(name);
            if (option.OptionValueType is OptionValueType.Optional or OptionValueType.Required)
            {
                if (option.OptionValueType == OptionValueType.Optional)
                    prototype.Append('[');
                prototype.Append("=VALUE");
                if (option.MaxValueCount > 1)
                {
                    var separator = option.ValueSeparators?.FirstOrDefault() ?? " ";
                    for (var index = 1; index < option.MaxValueCount; index++)
                        prototype.Append(separator).Append("VALUE").Append(index + 1);
                }
                if (option.OptionValueType == OptionValueType.Optional)
                    prototype.Append(']');
            }

            var text = prototype.ToString();
            if (text.Length < optionWidth)
                writer.Write(new string(' ', optionWidth - text.Length));
            else
            {
                writer.WriteLine();
                writer.Write(new string(' ', optionWidth));
            }

            var description = option.Description ?? string.Empty;
            var lines = description.Split('\n');
            writer.WriteLine(lines[0]);
            foreach (var line in lines.Skip(1))
                writer.WriteLine(new string(' ', optionWidth + 2) + line);
        }
    }

    private sealed class ActionOption : Option
    {
        private readonly Action<string> actionField;

        public ActionOption(string prototype, string description, Action<string> action)
            : base(prototype, description, 1) => actionField = action ?? throw new ArgumentNullException(nameof(action));

        protected override void OnParseComplete(OptionContext context) => actionField(context.OptionValues[0]);
    }

    private sealed class ConvertedActionOption<T> : Option
    {
        private readonly Action<T> actionField;

        public ConvertedActionOption(string prototype, string description, Action<T> action)
            : base(prototype, description, 1) => actionField = action ?? throw new ArgumentNullException(nameof(action));

        protected override void OnParseComplete(OptionContext context)
            => actionField(Parse<T>(context.OptionValues[0], context));
    }

    private sealed class ConvertedActionOption<TKey, TValue> : Option
    {
        private readonly OptionAction<TKey, TValue> actionField;

        public ConvertedActionOption(string prototype, string description, OptionAction<TKey, TValue> action)
            : base(prototype, description, 2) => actionField = action ?? throw new ArgumentNullException(nameof(action));

        protected override void OnParseComplete(OptionContext context)
            => actionField(Parse<TKey>(context.OptionValues[0], context), Parse<TValue>(context.OptionValues[1], context));
    }
}
