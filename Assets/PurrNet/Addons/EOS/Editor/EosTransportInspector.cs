using PurrNet.Editor;
using PurrNet.Transports;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace PurrNet.EOS.Editor
{
    [CustomEditor(typeof(EosTransport), true)]
    public class EosTransportInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var generic = (GenericTransport)target;
            if (!generic.isSupported)
            {
                GUI.enabled = false;
                base.OnInspectorGUI();

                if (!EditorApplication.isCompiling)
                    GUI.enabled = true;

                EditorGUILayout.HelpBox("EOS is not installed. Please install it to use this transport.",
                    MessageType.Warning);
                if (GUILayout.Button("Add EOS Plugin to Package Manager"))
                {
                    var request = Client.Add("https://github.com/EOS-Contrib/eos_plugin_for_unity_upm.git");
                    EditorApplication.update += CheckEOSRequest;

                    void CheckEOSRequest()
                    {
                        if (!request.IsCompleted)
                            return;

                        EditorApplication.update -= CheckEOSRequest;

                        if (request.Status == StatusCode.Success)
                            Debug.Log("EOS Plugin added successfully.");
                        else
                            Debug.LogError("Failed to add EOS Plugin: " + request.Error.message);
                    }
                }
            }
            else
            {
                base.OnInspectorGUI();
                TransportInspector.DrawTransportStatus(generic);
            }
        }
    }
}
