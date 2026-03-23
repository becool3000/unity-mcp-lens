using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Unity.AI.Generators.UI.Utilities
{
    static class BindingListExtensions
    {
        /// <summary>
        /// Replaces items in the BindingList with those in newList (unique replacement).
        /// If frontPredicate is not null, performs a stable partition so that items for which
        /// frontPredicate(item) == true appear at the front, preserving their original relative order.
        /// If frontPredicate is null, no partitioning occurs.
        /// Newer items are inserted at the front of each partition.
        /// </summary>
        public static void ReplaceRangeUnique<T>(
            this BindingList<T> bindingList,
            IEnumerable<T> newList,
            Func<T, bool> frontPredicate = null)
            where T : IEquatable<T>
        {
            if (bindingList == null) throw new ArgumentNullException(nameof(bindingList));
            if (newList == null) throw new ArgumentNullException(nameof(newList));

            bindingList.RaiseListChangedEvents = false;
            try
            {
                // Prepare hash sets for fast lookup.
                var newSet = new HashSet<T>(newList);
                var bindingSet = new HashSet<T>(bindingList);

                // 1) Remove items not in the new set.
                // (Using ToArray so that we don't modify the collection while iterating.)
                var toRemove = bindingList.Where(item => !newSet.Contains(item)).ToArray();
                foreach (var item in toRemove)
                {
                    bindingList.Remove(item);
                }

                // 2) Insert new items that are not in the binding list.
                // (Again, using the hash set for membership tests.)
                var toAdd = newList.Where(item => !bindingSet.Contains(item));
                // Reverse the new items so we can insert the newer items at the front and keep their relative ordering intact
                foreach (var item in toAdd.Reverse())
                {
                    bindingList.Insert(0, item);
                }

                // 3) If frontPredicate is provided (not null), perform a stable partition:
                if (frontPredicate != null)
                {
                    // stable partition:
                    //  - Mark items that match frontPredicate as group 0
                    //  - All others as group 1
                    //  - then preserve original order when grouping
                    var stableSorted = bindingList
                        .Select((x, index) => new { Value = x, Index = index })
                        .OrderBy(e => frontPredicate(e.Value) ? 0 : 1)
                        .ThenBy(e => e.Index)
                        .Select(e => e.Value)
                        .ToList();

                    bindingList.Clear();
                    foreach (var item in stableSorted)
                    {
                        bindingList.Add(item);
                    }
                }
            }
            finally
            {
                // Turn event notifications back on, then refresh once.
                bindingList.RaiseListChangedEvents = true;
                bindingList.ResetBindings();
            }
        }

        /// <summary>
        /// Replaces items in the BindingList with those in newList (unique replacement) and
        /// reorders the entire list to follow newList’s ordering. If frontPredicate is provided,
        /// items for which frontPredicate(item) is true are placed in their own group at the front,
        /// preserving relative order from newList.
        /// </summary>
        /// <remarks>SLOW, do not use</remarks>
        public static void ReplaceRangeUniqueAndSort<T>(
            this BindingList<T> bindingList,
            IEnumerable<T> newList,
            Func<T, bool> frontPredicate = null)
            where T : IEquatable<T>
        {
            if (bindingList == null)
                throw new ArgumentNullException(nameof(bindingList));
            if (newList == null)
                throw new ArgumentNullException(nameof(newList));

            bindingList.RaiseListChangedEvents = false;
            try
            {
                // Create a set of items from newList so we can quickly check membership.
                var newSet = new HashSet<T>(newList);

                // 1) Remove any items in bindingList that are no longer in newList.
                foreach (var item in bindingList.Where(item => !newSet.Contains(item)).ToArray())
                {
                    bindingList.Remove(item);
                }

                // 2) Add any new items that are in newList but missing from bindingList.
                // (Because newList defines our master order, add them in order.)
                var bindingSet = new HashSet<T>(bindingList);
                foreach (var item in newList.Where(item => !bindingSet.Contains(item)))
                {
                    bindingList.Add(item);
                }

                // 3) Reorder bindingList to match the ordering of newList.
                // First, collect the items in the order defined by newList.
                var ordered = newList.Where(bindingList.Contains);

                // 4) If frontPredicate is provided, partition the ordered list.
                if (frontPredicate != null)
                {
                    ordered = ordered.Where(frontPredicate).Concat(ordered.Where(item => !frontPredicate(item)));
                }

                var newOrder = ordered.ToArray();

                // Now clear bindingList and rebuild it with the new order.
                bindingList.Clear();
                foreach (var item in newOrder)
                {
                    bindingList.Add(item);
                }
            }
            finally
            {
                bindingList.RaiseListChangedEvents = true;
                bindingList.ResetBindings();
            }
        }
    }
}
