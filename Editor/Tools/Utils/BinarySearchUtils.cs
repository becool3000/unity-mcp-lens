using System;

namespace Unity.AI.Assistant.Tools.Editor
{
    internal static class BinarySearchUtils
    {
        /// <summary>
        /// Performs binary search
        /// </summary>
        /// <param name="minSearchValue">The min value of the parameter used for the search</param>
        /// <param name="maxSearchValue">The max value of the parameter used for the search</param>
        /// <param name="generate">A function that generates an output for a given parameter value. That method should throw an exception if the output does not meet the search criteria</param>
        /// <param name="comparer">A function to compare the search parameters</param>
        /// <param name="midFunc">A function to compute the new parameter value from the low and high values of the search segment</param>
        /// <param name="incrementFunc">A function to increment the search bound for the next search segment</param>
        /// <param name="decrementFunc">A function to decrement the search bound for the next search segment</param>
        /// <typeparam name="T">Type of the search parameter</typeparam>
        /// <typeparam name="TResult">Type of the output</typeparam>
        /// <returns>A tuple with the found output, the corresponding parameter value and a bool indicating if the search succeeded or not</returns>
        public static (TResult result, T value, bool found) BinarySearch<T, TResult>(
            T minSearchValue,
            T maxSearchValue,
            Func<T, TResult> generate,
            Func<T, T, int> comparer,
            Func<T, T, T> midFunc,
            Func<T, T> incrementFunc,
            Func<T, T> decrementFunc)
        {
            var bestValue = minSearchValue;
            TResult bestResult = default;
            var found = false;

            var low = minSearchValue;
            var high = maxSearchValue;

            TResult lastCandidate = default;
            while (comparer(low, high) <= 0)
            {
                var mid = midFunc(low, high);

                var isValid = true;
                try
                {
                    lastCandidate = generate(mid);
                }
                catch (Exception)
                {
                    isValid = false;
                }

                if (isValid)
                {
                    bestValue = mid;
                    bestResult = lastCandidate;
                    found = true;

                    // Value is valid but we want the highest one
                    low = incrementFunc(mid);
                }
                else
                {
                    high = decrementFunc(mid);
                }
            }

            return (bestResult, bestValue, found);
        }

        public static (string, int, bool) BinarySearch(Func<int, string> generate, int minSearchValue, int maxSearchValue)
        {
            return BinarySearch(
                minSearchValue: minSearchValue,
                maxSearchValue: maxSearchValue,
                generate: generate,
                comparer: (a, b) => a.CompareTo(b),
                midFunc: (a, b) => (a + b) / 2,
                incrementFunc: v => v + 1,
                decrementFunc: v => v - 1
            );
        }
    }
}
