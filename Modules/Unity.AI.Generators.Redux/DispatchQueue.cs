using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.AI.Generators.UIElements.Core;
using Debug = UnityEngine.Debug;

namespace Unity.AI.Generators.Redux
{
    class DispatchQueue
    {
        record QueueItem(Action runAction, StandardAction action, StackFrame frame);

        static int s_MaxItemsProcessedPerDrain = 250;

        readonly Queue<QueueItem> m_Queue = new();
        public bool isProcessing;

        public void Queue(Store store, StandardAction action, params string[] slices) =>
            m_Queue.Enqueue(
                new(() => store.DispatchToSlices(action, slices),
                action,
                ExceptionUtilities.detailedExceptionStack ? new() : null)
            );

        public void Drain(StandardAction sourceAction)
        {
            // If we're already processing the queue, just return. New actions will be added to the queue and processed in FIFO order
            if (isProcessing)
                return;
            if (m_Queue.Count == 0)
                return;

            isProcessing = true;

            try
            {
                ExceptionUtilities.LogRedux($"------------------ Processing dispatch queue after {sourceAction.type} ----------------------");

                // Process all items in the queue, including any that get added during processing
                // This ensures proper FIFO order
                int itemsProcessed = 0;
                while (m_Queue.Count > 0)
                {
                    // Check for potential infinite loops
                    if (itemsProcessed >= s_MaxItemsProcessedPerDrain)
                    {
                        Debug.LogError($"Dispatch queue has processed {itemsProcessed} items which suggests a possible infinite loop. " +
                                      $"Further processing of this queue will be cancelled after action: {sourceAction.type}\n" +
                                      "Possible causes include having actions that continuously dispatch new actions " +
                                      "or a selector's result not compared correctly because the result is a new object." +
                                      "Like an different IEnumerable with identical content.");

                        // Clear the queue to prevent the same infinite loop from happening on the next action dispatch
                        m_Queue.Clear();
                        break;
                    }

                    var item = m_Queue.Dequeue();
                    ExceptionUtilities.LogRedux($"Dispatching (queued) {item.action.type}\n -- Action origin stack:\n{item.frame}");
                    item.runAction();

                    itemsProcessed++;
                }

                ExceptionUtilities.LogRedux($"------------------ Completed processing dispatch queue after {sourceAction.type} ----------------------");
            }
            finally
            {
                isProcessing = false;
            }
        }
    }
}

