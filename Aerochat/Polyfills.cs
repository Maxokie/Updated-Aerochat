// Polyfills required for using modern C# language features and APIs
// when targeting .NET Framework 4.6.1.
using System.Collections.Generic;
using System.Linq;

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }

    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct |
        AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) { FeatureName = featureName; }
        public string FeatureName { get; }
        public bool IsOptional { get; set; }
        public const string RefStructs = nameof(RefStructs);
        public const string RequiredMembers = nameof(RequiredMembers);
    }
}

namespace System
{
    /// <summary>Polyfill for C# 8 index-from-end operator (^n).</summary>
    public readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Non-negative number required.");
            _value = fromEnd ? ~value : value;
        }

        public static Index Start => new Index(0);
        public static Index End   => new Index(~0);

        public static implicit operator Index(int value) => new Index(value);

        public bool IsFromEnd => _value < 0;
        public int  Value     => IsFromEnd ? ~_value : _value;

        public int GetOffset(int length)
        {
            int offset = _value;
            if (IsFromEnd) offset += length + 1;
            return offset;
        }

        public override string ToString() => IsFromEnd ? $"^{(uint)Value}" : ((uint)_value).ToString();
        public bool Equals(Index other) => _value == other._value;
        public override bool Equals(object value) => value is Index other && _value == other._value;
        public override int GetHashCode() => _value;
    }

    /// <summary>Polyfill for C# 8 range operator (a..b).</summary>
    public readonly struct Range : IEquatable<Range>
    {
        public Range(Index start, Index end) { Start = start; End = end; }

        public Index Start { get; }
        public Index End   { get; }

        public static Range All => new Range(Index.Start, Index.End);
        public static Range StartAt(Index start) => new Range(start, Index.End);
        public static Range EndAt(Index end)     => new Range(Index.Start, end);

        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int start = Start.GetOffset(length);
            int end   = End.GetOffset(length);
            if ((uint)end > (uint)length || (uint)start > (uint)end)
                throw new ArgumentOutOfRangeException(nameof(length));
            return (start, end - start);
        }

        public override string ToString() => $"{Start}..{End}";
        public bool Equals(Range other) => other.Start.Equals(Start) && other.End.Equals(End);
        public override bool Equals(object value) => value is Range other && Equals(other);
        public override int GetHashCode() => Start.GetHashCode() * 31 + End.GetHashCode();
    }
}

namespace Aerochat.Polyfills
{
    internal static class DictionaryExtensions
    {
        public static TValue? GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key)
        {
            return dict.TryGetValue(key, out var value) ? value : default;
        }

        public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue defaultValue)
        {
            return dict.TryGetValue(key, out var value) ? value : defaultValue;
        }

        public static bool TryAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue value)
        {
            if (dict.ContainsKey(key)) return false;
            dict.Add(key, value);
            return true;
        }
    }

    internal static class EnumerableExtensions
    {
        public static IEnumerable<T> TakeLast<T>(this IEnumerable<T> source, int count)
        {
            var list = source.ToList();
            int skip = Math.Max(0, list.Count - count);
            return list.Skip(skip);
        }
    }

    internal static class StringExtensions
    {
        public static bool Contains(this string str, char value)
            => str.IndexOf(value) >= 0;

        public static bool StartsWith(this string str, char value)
            => str.Length > 0 && str[0] == value;

        public static bool EndsWith(this string str, char value)
            => str.Length > 0 && str[str.Length - 1] == value;

        public static string[] Split(this string str, char separator, StringSplitOptions options)
            => str.Split(new[] { separator }, options);

        public static string[] Split(this string str, char separator, int count, StringSplitOptions options)
            => str.Split(new[] { separator }, count, options);
    }
}
