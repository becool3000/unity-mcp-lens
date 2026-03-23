using HuggingfaceHub;
using UnityEditor;
using UnityEngine.UIElements;

namespace HFHubClient
{
    sealed class DownloadProgress : IGroupedProgress
    {
        int? m_ProgressId;

        public ProgressBar ProgressBar;

        public void Report(string file, int pct)
        {
            m_ProgressId ??= Progress.Start("Downloading Model", "Downloading model files for Asset Knowledge");

            Progress.Report(m_ProgressId.Value, pct / 100f);

            if (ProgressBar != null)
            {
                // Need to be on main thread to update UI:
                EditorApplication.delayCall += () =>
                {
                    ProgressBar.value = pct;
                };
            }
        }

        public void Done()
        {
            if (m_ProgressId.HasValue)
            {
                Progress.Remove(m_ProgressId.Value);
                m_ProgressId = null;
            }
        }

        ~DownloadProgress()
        {
            Done();
        }
    }
}
