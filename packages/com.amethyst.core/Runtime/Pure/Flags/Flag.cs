using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Amethyst.Flags
{
    /// <summary>
    /// A named switch that selects game behavior (FLAGS.md). Declare each one as a static
    /// readonly field through <see cref="Bool"/>, <see cref="Int"/>, <see cref="Float"/>,
    /// <see cref="String"/> or <see cref="Enum{T}"/>, on one statement, and read its
    /// <c>Value</c> wherever the behavior branches.
    /// </summary>
    public abstract class Flag
    {
        static readonly Regex NamePattern = new Regex(@"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$");
        static readonly Dictionary<string, Flag> ByName = new Dictionary<string, Flag>();
        static readonly List<Flag> Declared = new List<Flag>();

        protected Flag(string name, string about)
        {
            if (!NamePattern.IsMatch(name))
                throw new ArgumentException(
                    $"flag '{name}' needs a dotted lowercase name, like 'sword.arc'"
                );
            Name = name;
            About = about;
            lock (ByName)
            {
                if (ByName.ContainsKey(name))
                    throw new InvalidOperationException($"flag '{name}' is declared twice");
                ByName.Add(name, this);
                Declared.Add(this);
            }
        }

        public string Name { get; }
        public string About { get; }
        public abstract string TypeName { get; }
        public abstract string DefaultText { get; }

        /// <summary>The kebab-case values an enum flag takes; empty for other types.</summary>
        public virtual IReadOnlyList<string> Choices => Array.Empty<string>();

        /// <summary>The current value as text.</summary>
        public abstract string Text { get; }

        /// <summary>Why <paramref name="text"/> is not a value of this flag, or null if it is.</summary>
        public abstract string Check(string text);

        public static IReadOnlyList<Flag> All
        {
            get
            {
                lock (ByName)
                    return Declared.ToArray();
            }
        }

        public static Flag Find(string name)
        {
            lock (ByName)
                return ByName.TryGetValue(name, out var flag) ? flag : null;
        }

        public static Flag<bool> Bool(string name, bool @default, string about) =>
            new Flag<bool>(name, @default, about, "bool", FlagText.TryBool, FlagText.Bool);

        public static Flag<int> Int(string name, int @default, string about) =>
            new Flag<int>(name, @default, about, "int", FlagText.TryInt, FlagText.Int);

        public static Flag<float> Float(string name, float @default, string about) =>
            new Flag<float>(name, @default, about, "float", FlagText.TryFloat, FlagText.Float);

        public static Flag<string> String(string name, string @default, string about) =>
            new Flag<string>(name, @default, about, "string", FlagText.TryString, s => s);

        public static Flag<T> Enum<T>(string name, T @default, string about)
            where T : struct, System.Enum =>
            new Flag<T>(
                name,
                @default,
                about,
                "enum",
                FlagText.TryEnum,
                FlagText.Enum,
                FlagText.Choices<T>()
            );
    }

    public delegate bool FlagParser<T>(string text, out T value);

    public sealed class Flag<T> : Flag
    {
        readonly FlagParser<T> parse;
        readonly Func<T, string> format;
        readonly IReadOnlyList<string> choices;
        T cached;
        long cachedVersion = -1;

        internal Flag(
            string name,
            T @default,
            string about,
            string typeName,
            FlagParser<T> parse,
            Func<T, string> format,
            IReadOnlyList<string> choices = null
        )
            : base(name, about)
        {
            Default = @default;
            TypeName = typeName;
            this.parse = parse;
            this.format = format;
            this.choices = choices ?? Array.Empty<string>();
        }

        public T Default { get; }
        public override string TypeName { get; }
        public override string DefaultText => format(Default);
        public override IReadOnlyList<string> Choices => choices;
        public override string Text => format(Value);

        /// <summary>
        /// The value in <see cref="FlagState.Current"/>: its highest layer's, else the default.
        /// Cached until the state changes, so reading it every frame costs a comparison.
        /// </summary>
        public T Value
        {
            get
            {
                var state = FlagState.Current;
                if (state.Version != cachedVersion)
                {
                    cached =
                        state.TryGet(Name, out var text) && parse(text, out var value)
                            ? value
                            : Default;
                    cachedVersion = state.Version;
                }
                return cached;
            }
        }

        public override string Check(string text) =>
            parse(text, out _)
                ? null
                : $"'{text}' is not a {TypeName} value for {Name}"
                    + (choices.Count > 0 ? $" ({string.Join(", ", choices)})" : "");

        public string Format(T value) => format(value);

        /// <summary>Sets this flag until the returned scope is disposed (tests).</summary>
        public IDisposable Override(T value) => FlagState.Current.Override(Name, format(value));

        public static implicit operator T(Flag<T> flag) => flag.Value;

        public override string ToString() => $"{Name}={Text}";
    }
}
