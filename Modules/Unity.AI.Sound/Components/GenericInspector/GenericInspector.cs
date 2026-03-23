using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Sound.Components
{
    [UxmlElement]
    partial class GenericInspector : VisualElement
    {
        class SerializableObjectContainer : ScriptableObject
        {
            [SerializeReference]
            public object data;
        }
        readonly SerializableObjectContainer m_Container;
        readonly SerializedObject m_SerializedContainer;

        public GenericInspector()
        {
            m_Container = ScriptableObject.CreateInstance<SerializableObjectContainer>();
            m_SerializedContainer = new SerializedObject(m_Container);
        }

        void BuildUI()
        {
            Clear();
            SerializedProperty dataProperty = m_SerializedContainer.FindProperty("data");
            PropertyField dataField = new PropertyField(dataProperty);
            Add(dataField);
            this.Bind(m_SerializedContainer);
        }

        public void SetData<T>(T obj)
        {
            m_Container.data = obj;
            m_SerializedContainer.Update();
            BuildUI();
        }

        public void UpdateData()
        {
            m_SerializedContainer.Update();
        }

        public void Select() => Selection.activeObject = m_Container;
    }
}
