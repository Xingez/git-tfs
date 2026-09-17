using System.Collections;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace GitTfs.Test
{
    internal static class TestAssert
    {
        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (expected is IEnumerable expectedItems && actual is IEnumerable actualItems && expected is not string && actual is not string)
            {
                CollectionAssert.AreEqual(ToObjects(expectedItems), ToObjects(actualItems), message);
                return;
            }

            MSTestAssert.AreEqual(expected, actual, message);
        }

        public static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message = null) =>
            CollectionAssert.AreEqual(expected.Cast<object>().ToArray(), actual.Cast<object>().ToArray(), message);

        public static void NotEqual<T>(T notExpected, T actual, string message = null) => MSTestAssert.AreNotEqual(notExpected, actual, message);

        public static void True(bool condition, string message = null) => MSTestAssert.IsTrue(condition, message);

        public static void False(bool condition, string message = null) => MSTestAssert.IsFalse(condition, message);

        public static void Null(object value, string message = null) => MSTestAssert.IsNull(value, message);

        public static void NotNull(object value, string message = null) => MSTestAssert.IsNotNull(value, message);

        public static void Same(object expected, object actual, string message = null) => MSTestAssert.AreSame(expected, actual, message);

        public static void Empty(IEnumerable values)
        {
            if (values.Cast<object>().Any())
                MSTestAssert.Fail("Expected the collection to be empty.");
        }

        public static T Single<T>(IEnumerable<T> values)
        {
            var items = values.Take(2).ToArray();
            if (items.Length != 1)
                MSTestAssert.Fail($"Expected a single item, but found {items.Length}.");
            return items[0];
        }

        public static void Contains(string expectedSubstring, string actualString) => StringAssert.Contains(actualString, expectedSubstring);

        public static void Contains<T>(T expected, IEnumerable<T> values) => CollectionAssert.Contains(values.ToList(), expected);

        public static void DoesNotContain<T>(IEnumerable<T> values, Func<T, bool> predicate)
        {
            if (values.Any(predicate))
                MSTestAssert.Fail("Expected the collection not to contain an item matching the predicate.");
        }

        public static T IsType<T>(object value)
        {
            MSTestAssert.IsNotNull(value);
            MSTestAssert.AreEqual(typeof(T), value.GetType());
            return (T)value;
        }

        public static T Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T exception) when (exception.GetType() == typeof(T))
            {
                return exception;
            }
            catch (Exception exception)
            {
                MSTestAssert.Fail($"Expected {typeof(T).FullName}, but {exception.GetType().FullName} was thrown.");
            }

            MSTestAssert.Fail($"Expected {typeof(T).FullName}, but no exception was thrown.");
            return null;
        }

        public static T Throws<T>(Func<object> action) where T : Exception => Throws<T>((Action)(() =>
        {
            _ = action();
        }));

        private static object[] ToObjects(IEnumerable values) => values.Cast<object>().ToArray();
    }
}
