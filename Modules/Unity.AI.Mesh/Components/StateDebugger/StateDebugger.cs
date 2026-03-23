using System;
using System.Linq;
using Unity.AI.Mesh.Services.SessionPersistence;
using Unity.AI.Mesh.Services.Stores.Actions;
using Unity.AI.Mesh.Services.Stores.Selectors;
using Unity.AI.Mesh.Services.Stores.Slices;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Mesh.Components
{
    [UxmlElement]
    partial class StateDebugger : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Mesh/Components/StateDebugger/StateDebugger.uxml";
        StateData m_StateData = new();
        StateHistory m_History = new();
        TimelineControls m_Timeline;
        GenericInspector m_Inspector;
        bool m_Pause;
        VisualElement m_StateManager;
        Toggle m_Record;

        public StateDebugger()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_Inspector = this.Q<GenericInspector>("inspector");
            m_Inspector.SetData(m_StateData);

            var button = this.Q<Button>("select");
            button.clicked += () => m_Inspector.Select();
            m_Timeline = this.Q<TimelineControls>("timeline");
            m_Timeline.OnSetTime += SetTime;

            m_StateManager = this.Q<VisualElement>("state-manager");

            HideStateRecording();

            m_Record = this.Q<Toggle>("record");
            m_Record.RegisterValueChangedCallback(evt => this.Dispatch(DebuggerActions.setRecording, evt.newValue));

            this.UseStore(store =>
            {
                InitDebugger(store);
                this.Use(Selectors.SelectRecording, record =>
                {
                    m_Record.SetValueWithoutNotify(record);
                    RefreshRecordingState();
                });
            });
        }

        static void InitDebugger(Store store)
        {
            if (!store.SliceExists(DebuggerActions.slice))
            {
                DebuggerSlice.Create(store);
                MemoryPersistence.Persist(store, DebuggerActions.init, Selectors.SelectDebugger);
            }
        }

        void RefreshRecordingState()
        {
            if (this.GetState().SelectRecording())
                StartRecording();
            else
                StopRecording();
        }

        void StartRecording()
        {
            StopRecording();
            this.GetStore().OnAction += OnAction;
            m_StateManager.RemoveFromClassList("hide");
        }

        void StopRecording()
        {
            m_History.states.Clear();
            m_Timeline.SetMaxRange(0);
            m_Timeline.SetTimeWithoutNotify(0);

            HideStateRecording();
            this.GetStore().OnAction -= OnAction;
        }

        void HideStateRecording()
        {
            m_StateManager.AddToClassList("hide");
        }


        void OnAction(StandardAction action)
        {
            m_StateData.state = this.GetState().SelectAppData(); // Assuming session memory persistence has been updated first
            m_StateData.debugger = this.GetState().SelectDebugger();
            m_Inspector.UpdateData();

            // Don't update history if forcing time update.
            if (m_Pause)
                return;

            OnNewState();
        }

        void OnNewState()
        {
            TrimFuture();
            AddToHistory(m_StateData);
            var time = m_History.states.Count - 1;
            m_Timeline.SetMaxRange(time);
            m_Timeline.SetTimeWithoutNotify(time);
        }

        // If we're at frame 5/10 and a new state comes in, discard everything after 5 and continue from there.
        void TrimFuture() => m_History.states = m_History.states.Take(m_Timeline.Time + 1).ToList();

        void AddToHistory(StateData stateData)
        {
            var stateWrapper = ScriptableObject.CreateInstance<StateWrapper>();
            stateWrapper.data = stateData;
            var deepClone = UnityEngine.Object.Instantiate(stateWrapper);
            m_History.states.Add(deepClone.data);
        }

        async void SetTime(int at)
        {
            if (at >= m_History.states.Count)
                return;

            try
            {
                m_Pause = true;
                var state = m_History.states[at];
                this.Dispatch(AppActions.init, state.state);
                this.Dispatch(DebuggerActions.init, state.debugger with
                {
                    // Don't modify record value on state time changes
                    record = m_Record.value
                });
            }
            finally
            {
                await EditorTask.Delay(5);    // Wait for any state reset side effects.
                m_Pause = false;
            }
        }
    }
}
