using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// Controller for the mode dropdown.
    /// Wraps an existing DropdownField and binds it to a ModeProvider.
    /// </summary>
    class ModeDropdown : IDisposable
    {
        readonly DropdownField m_Dropdown;
        readonly ModeProvider m_Provider;
        bool m_Disposed;

        public ModeDropdown(DropdownField dropdown, ModeProvider provider)
        {
            m_Dropdown = dropdown ?? throw new ArgumentNullException(nameof(dropdown));
            m_Provider = provider ?? throw new ArgumentNullException(nameof(provider));

            m_Dropdown.RegisterValueChangedCallback(OnDropdownChanged);
            m_Provider.ModeChanged += OnProviderModeChanged;
        }

        /// <summary>
        /// Refresh the dropdown display from the provider's current state.
        /// </summary>
        public void Refresh()
        {
            if (m_Provider.AvailableModes.Count == 0)
            {
                m_Dropdown.choices = new() { "—" };
                m_Dropdown.SetValueWithoutNotify("—");
                return;
            }

            m_Dropdown.choices = m_Provider.AvailableModes
                .Select(m => m.Name)
                .ToList();

            var currentMode = m_Provider.AvailableModes
                .FirstOrDefault(m => m.Id == m_Provider.CurrentModeId);
            m_Dropdown.SetValueWithoutNotify(currentMode?.Name ?? m_Dropdown.choices.FirstOrDefault());
        }

        void OnDropdownChanged(ChangeEvent<string> evt)
        {
            var selected = m_Provider.AvailableModes
                .FirstOrDefault(m => m.Name == evt.newValue);

            if (!string.IsNullOrEmpty(selected?.Id))
                _ = SetModeWithLoggingAsync(selected.Id);
        }

        async Task SetModeWithLoggingAsync(string modeId)
        {
            try
            {
                await m_Provider.SetModeAsync(modeId);
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"[ModeDropdown] Failed to set mode: {ex.Message}");
            }
        }

        void OnProviderModeChanged(string modeId)
        {
            // When mode changes, refresh the dropdown to show all available modes
            // and update the selected value
            Refresh();
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;

            m_Dropdown.UnregisterValueChangedCallback(OnDropdownChanged);
            m_Provider.ModeChanged -= OnProviderModeChanged;
        }
    }
}
