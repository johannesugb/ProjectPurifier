using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectPurifier.Utils
{
    /// <summary>
    /// Static class for combinatorial things like permutations and interrelated helper functions
    /// </summary>
    public static class Combinatorics
    {
        /// <summary>
        /// Randomly permutes the the array using Knuth/Fisher-Yates algorithm (I think).
        /// Shuffles the array directly, i.e. modifies the array
        /// </summary>
        public static void ShuffleInPlace(this int[] array)
        {
            var rnd = new Random(); // Beware that new Random() is seeded on current timestamp.
            int max = array.Length;
            int min = 0;
            while (min < max - 1)
            {
                int i = rnd.Next(min, max); // min inclusive, max exclusive
                // swap
                int tmp = array[i];
                array[i] = array[min];
                array[min] = tmp;
                ++min;
            }
        }

        /// <summary>
        /// Randomly permutes the the array using Knuth/Fisher-Yates algorithm (I think).
        /// Does not modify the input-array, returns the results in a new array.
        /// </summary>
        public static int[] ShuffleCopy(int[] array)
        {
            int[] result = new int[array.Length];
            System.Array.Copy(array, result, array.Length);
            result.ShuffleInPlace();
            return result;
        }

        /// <summary>
        /// Creates an integer array of ascending integers. 
        /// First value is startValue, last value is endValue, step size is 1
        /// </summary>
        public static int[] CreateIntSequence(int startValue, int endValue)
        {
            int[] array = new int[endValue - startValue + 1];
            for (int i = 0; i < array.Length; ++i)
            {
                array[i] = startValue++;
            }
            return array;
        }

        /// <summary>
        /// Takes the first N values of the array and copies them into a new array.
        /// </summary>
        public static int[] CopyFirstNValuesOf(this int[] array, int n)
        {
            if (n > array.Length)
                throw new System.ArgumentException("n > array.Length");

            int[] result = new int[n];
            for (int i = 0; i < result.Length; ++i)
                result[i] = array[i];
            return result;
        }
    }
}
