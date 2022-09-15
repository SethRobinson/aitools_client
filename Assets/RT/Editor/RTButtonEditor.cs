#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UI;

//Based on code from Xarbrough: https://answers.unity.com/questions/1226851/addlistener-to-onpointerdown-of-button-instead-of.html

[CustomEditor(typeof(RTButton), true)]
public class RTButtonEditor : ButtonEditor
{
    SerializedProperty _onDownProperty;
    SerializedProperty _onUpProperty;
     
    protected override void OnEnable()
    {
        base.OnEnable();
        _onDownProperty = serializedObject.FindProperty("_onDown");
        _onUpProperty = serializedObject.FindProperty("_onUp");
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        EditorGUILayout.Space();

        serializedObject.Update();
        EditorGUILayout.PropertyField(_onDownProperty);
        EditorGUILayout.PropertyField(_onUpProperty);

        serializedObject.ApplyModifiedProperties();
    }
}
#endif